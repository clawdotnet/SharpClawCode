using System.Text.Json;
using System.Text.Json.Serialization;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Sessions.Concurrency;

/// <summary>
/// Coordinates session snapshot writes made outside a running turn with the turn's own writes.
/// </summary>
/// <remarks>
/// <para>
/// A prompt turn holds the session turn lock for its whole duration and saves an in-memory copy of the
/// session that was read when the turn started. Writers that run concurrently (permission/trust changes,
/// model preferences, todo edits, work-item imports, ...) would otherwise be overwritten by that stale copy.
/// </para>
/// <para>
/// Out-of-turn writers run inside <see cref="RunAsync{T}"/>, which holds a short per-session snapshot lock and
/// records each metadata key (and the title) they change in a per-session overlay as
/// <c>{ start value, latest value }</c>. Every other save goes through <see cref="ConcurrencySafeSessionStore"/>, which takes
/// the same lock and, for each overlay key, replaces a value that still equals the start value (a stale copy)
/// with the latest value, while keeping values the saver changed deliberately. The turn resets the overlay when it
/// starts and ends.
/// </para>
/// </remarks>
public static class SessionUpdateScope
{
    /// <summary>Overlay key used for the session title.</summary>
    internal const string TitleKey = "@title";

    private static readonly AsyncLocal<ScopeMarker?> CurrentScope = new();

    /// <summary>
    /// Runs an out-of-turn session read-modify-write while holding the session snapshot lock. Saves performed
    /// by <paramref name="action"/> for this session are recorded in the overlay so a concurrently running
    /// turn cannot overwrite them.
    /// </summary>
    /// <typeparam name="T">The action result type.</typeparam>
    /// <param name="fileSystem">File system used for locking.</param>
    /// <param name="storagePathResolver">Storage path resolver.</param>
    /// <param name="workspacePath">Normalized workspace path.</param>
    /// <param name="sessionId">Session id.</param>
    /// <param name="action">The read-modify-write to run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The action result.</returns>
    public static async Task<T> RunAsync<T>(
        IFileSystem fileSystem,
        IRuntimeStoragePathResolver storagePathResolver,
        string workspacePath,
        string sessionId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (IsActive(workspacePath, sessionId))
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }

        await using var gate = await fileSystem
            .AcquireExclusiveFileLockAsync(GetLockPath(storagePathResolver, workspacePath, sessionId), cancellationToken)
            .ConfigureAwait(false);
        var previous = CurrentScope.Value;
        CurrentScope.Value = new ScopeMarker(workspacePath, sessionId, previous);
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CurrentScope.Value = previous;
        }
    }

    /// <summary>
    /// Clears the overlay for a session. Called by the turn runtime, while holding the turn lock, when a turn
    /// starts (before it re-reads the session) and after it has persisted its final state.
    /// </summary>
    /// <param name="fileSystem">File system.</param>
    /// <param name="storagePathResolver">Storage path resolver.</param>
    /// <param name="workspacePath">Normalized workspace path.</param>
    /// <param name="sessionId">Session id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task ResetOverlayAsync(
        IFileSystem fileSystem,
        IRuntimeStoragePathResolver storagePathResolver,
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
    {
        await using var gate = await fileSystem
            .AcquireExclusiveFileLockAsync(GetLockPath(storagePathResolver, workspacePath, sessionId), cancellationToken)
            .ConfigureAwait(false);
        fileSystem.TryDeleteFile(GetOverlayPath(storagePathResolver, workspacePath, sessionId));
    }

    internal static bool IsActive(string workspacePath, string sessionId)
    {
        for (var scope = CurrentScope.Value; scope is not null; scope = scope.Previous)
        {
            if (string.Equals(scope.SessionId, sessionId, StringComparison.Ordinal)
                && string.Equals(scope.WorkspacePath, workspacePath, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    internal static string GetLockPath(IRuntimeStoragePathResolver resolver, string workspacePath, string sessionId)
        => Path.Combine(resolver.GetSessionRoot(workspacePath, sessionId), ".snapshot.lock");

    internal static string GetOverlayPath(IRuntimeStoragePathResolver resolver, string workspacePath, string sessionId)
        => Path.Combine(resolver.GetSessionRoot(workspacePath, sessionId), ".metadata-overlay.json");

    internal static async Task<Dictionary<string, SessionOverlayEntry>> ReadOverlayAsync(
        IFileSystem fileSystem,
        IRuntimeStoragePathResolver resolver,
        string workspacePath,
        string sessionId,
        CancellationToken cancellationToken)
    {
        var content = await fileSystem
            .ReadAllTextIfExistsAsync(GetOverlayPath(resolver, workspacePath, sessionId), cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new Dictionary<string, SessionOverlayEntry>(StringComparer.Ordinal);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(content, SessionOverlayJsonContext.Default.DictionaryStringSessionOverlayEntry);
            return parsed is null
                ? new Dictionary<string, SessionOverlayEntry>(StringComparer.Ordinal)
                : new Dictionary<string, SessionOverlayEntry>(parsed, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // A corrupt overlay only loses concurrent-edit protection; never block saves because of it.
            return new Dictionary<string, SessionOverlayEntry>(StringComparer.Ordinal);
        }
    }

    internal static Task WriteOverlayAsync(
        IFileSystem fileSystem,
        IRuntimeStoragePathResolver resolver,
        string workspacePath,
        string sessionId,
        Dictionary<string, SessionOverlayEntry> overlay,
        CancellationToken cancellationToken)
        => fileSystem.WriteAllTextAsync(
            GetOverlayPath(resolver, workspacePath, sessionId),
            JsonSerializer.Serialize(overlay, SessionOverlayJsonContext.Default.DictionaryStringSessionOverlayEntry),
            cancellationToken);

    internal static string? ReadValue(ConversationSession session, string key)
        => key == TitleKey
            ? session.Title
            : session.Metadata is not null && session.Metadata.TryGetValue(key, out var value) ? value : null;

    internal static IEnumerable<string> EnumerateKeys(ConversationSession session)
    {
        yield return TitleKey;
        if (session.Metadata is null)
        {
            yield break;
        }

        foreach (var key in session.Metadata.Keys)
        {
            yield return key;
        }
    }

    private sealed record ScopeMarker(string WorkspacePath, string SessionId, ScopeMarker? Previous);
}

/// <summary>
/// One overlay entry: the value a key had before out-of-turn edits began, and its latest intended value.
/// A <see langword="null"/> value means the key is absent.
/// </summary>
/// <param name="Start">Value before the first concurrent edit.</param>
/// <param name="Value">Latest intended value.</param>
public sealed record SessionOverlayEntry(string? Start, string? Value);

[JsonSerializable(typeof(Dictionary<string, SessionOverlayEntry>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class SessionOverlayJsonContext : JsonSerializerContext;
