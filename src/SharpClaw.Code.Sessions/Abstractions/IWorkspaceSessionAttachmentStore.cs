namespace SharpClaw.Code.Sessions.Abstractions;

/// <summary>
/// Persists which session id is explicitly attached for a workspace (multi-session orchestration).
/// </summary>
public interface IWorkspaceSessionAttachmentStore
{
    /// <summary>
    /// Gets the attached session id, if any.
    /// </summary>
    Task<string?> GetAttachedSessionIdAsync(string workspacePath, CancellationToken cancellationToken);

    /// <summary>
    /// Sets or clears the workspace attachment pointer.
    /// </summary>
    Task SetAttachedSessionIdAsync(string workspacePath, string? sessionId, CancellationToken cancellationToken);
}
