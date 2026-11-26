using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Sessions.Concurrency;

/// <summary>
/// Decorates an <see cref="ISessionStore"/> so session snapshot saves cannot silently discard concurrent
/// out-of-turn edits. See <see cref="SessionUpdateScope"/> for the protocol.
/// </summary>
public sealed class ConcurrencySafeSessionStore(
    ISessionStore inner,
    IFileSystem fileSystem,
    IRuntimeStoragePathResolver storagePathResolver) : ISessionStore
{
    /// <inheritdoc />
    public async Task SaveAsync(string workspacePath, ConversationSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (SessionUpdateScope.IsActive(workspacePath, session.Id))
        {
            // The snapshot lock is already held by the enclosing out-of-turn update.
            await SaveAndRecordOverlayAsync(workspacePath, session, cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var gate = await fileSystem
            .AcquireExclusiveFileLockAsync(SessionUpdateScope.GetLockPath(storagePathResolver, workspacePath, session.Id), cancellationToken)
            .ConfigureAwait(false);
        await SaveApplyingOverlayAsync(workspacePath, session, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<ConversationSession?> GetByIdAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
        => inner.GetByIdAsync(workspacePath, sessionId, cancellationToken);

    /// <inheritdoc />
    public Task<ConversationSession?> GetLatestAsync(string workspacePath, CancellationToken cancellationToken)
        => inner.GetLatestAsync(workspacePath, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ConversationSession>> ListAllAsync(string workspacePath, CancellationToken cancellationToken)
        => inner.ListAllAsync(workspacePath, cancellationToken);

    private async Task SaveAndRecordOverlayAsync(string workspacePath, ConversationSession session, CancellationToken cancellationToken)
    {
        var previous = await inner.GetByIdAsync(workspacePath, session.Id, cancellationToken).ConfigureAwait(false);
        await inner.SaveAsync(workspacePath, session, cancellationToken).ConfigureAwait(false);
        if (previous is null)
        {
            return;
        }

        var overlay = await SessionUpdateScope
            .ReadOverlayAsync(fileSystem, storagePathResolver, workspacePath, session.Id, cancellationToken)
            .ConfigureAwait(false);
        var changed = false;
        foreach (var key in SessionUpdateScope.EnumerateKeys(previous).Concat(SessionUpdateScope.EnumerateKeys(session)).Distinct(StringComparer.Ordinal))
        {
            var before = SessionUpdateScope.ReadValue(previous, key);
            var after = SessionUpdateScope.ReadValue(session, key);
            if (string.Equals(before, after, StringComparison.Ordinal))
            {
                continue;
            }

            overlay[key] = overlay.TryGetValue(key, out var existing)
                ? existing with { Value = after }
                : new SessionOverlayEntry(before, after);
            changed = true;
        }

        if (changed)
        {
            await SessionUpdateScope
                .WriteOverlayAsync(fileSystem, storagePathResolver, workspacePath, session.Id, overlay, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task SaveApplyingOverlayAsync(string workspacePath, ConversationSession session, CancellationToken cancellationToken)
    {
        var overlay = await SessionUpdateScope
            .ReadOverlayAsync(fileSystem, storagePathResolver, workspacePath, session.Id, cancellationToken)
            .ConfigureAwait(false);
        if (overlay.Count == 0)
        {
            await inner.SaveAsync(workspacePath, session, cancellationToken).ConfigureAwait(false);
            return;
        }

        var metadata = session.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
        var title = session.Title;
        var overlayChanged = false;
        foreach (var (key, entry) in overlay.ToArray())
        {
            var current = key == SessionUpdateScope.TitleKey
                ? title
                : metadata.TryGetValue(key, out var value) ? value : null;
            if (string.Equals(current, entry.Start, StringComparison.Ordinal))
            {
                // The saver holds a copy from before the concurrent edit: carry the edit forward.
                if (key == SessionUpdateScope.TitleKey)
                {
                    title = entry.Value ?? title;
                }
                else if (entry.Value is null)
                {
                    metadata.Remove(key);
                }
                else
                {
                    metadata[key] = entry.Value;
                }
            }
            else if (!string.Equals(current, entry.Value, StringComparison.Ordinal))
            {
                // The saver changed this key deliberately after reading the edit: it becomes the latest value.
                overlay[key] = entry with { Value = current };
                overlayChanged = true;
            }
        }

        await inner.SaveAsync(workspacePath, session with { Title = title, Metadata = metadata }, cancellationToken).ConfigureAwait(false);
        if (overlayChanged)
        {
            await SessionUpdateScope
                .WriteOverlayAsync(fileSystem, storagePathResolver, workspacePath, session.Id, overlay, cancellationToken)
                .ConfigureAwait(false);
        }
    }
}
