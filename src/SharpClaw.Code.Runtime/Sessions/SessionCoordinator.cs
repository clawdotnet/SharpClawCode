using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Sessions.Storage;

namespace SharpClaw.Code.Runtime.Sessions;

/// <inheritdoc />
public sealed class SessionCoordinator(
    ISessionStore sessionStore,
    IWorkspaceSessionAttachmentStore attachmentStore,
    IPathService pathService) : ISessionCoordinator
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<SessionSummaryRow>> ListSessionsAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var root = pathService.GetFullPath(workspacePath);
        var attached = await attachmentStore.GetAttachedSessionIdAsync(root, cancellationToken).ConfigureAwait(false);
        var sessions = await sessionStore.ListAllAsync(root, cancellationToken).ConfigureAwait(false);
        return sessions
            .Select(s =>
            {
                var parent = s.Metadata?.GetValueOrDefault(SharpClawWorkflowMetadataKeys.ParentSessionId);
                return new SessionSummaryRow(
                    s.Id,
                    s.Title,
                    s.UpdatedAtUtc,
                    s.State,
                    parent,
                    string.Equals(s.Id, attached, StringComparison.Ordinal) ? "attached" : null);
            })
            .ToArray();
    }

    /// <inheritdoc />
    public async Task AttachSessionAsync(string workspacePath, string sessionId, CancellationToken cancellationToken)
    {
        var root = pathService.GetFullPath(workspacePath);
        var session = await sessionStore.GetByIdAsync(root, sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        }

        await attachmentStore.SetAttachedSessionIdAsync(root, sessionId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task DetachSessionAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var root = pathService.GetFullPath(workspacePath);
        return attachmentStore.SetAttachedSessionIdAsync(root, null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<string?> GetAttachedSessionIdAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var root = pathService.GetFullPath(workspacePath);
        return attachmentStore.GetAttachedSessionIdAsync(root, cancellationToken);
    }
}
