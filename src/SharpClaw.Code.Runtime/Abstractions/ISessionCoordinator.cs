using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Lists workspace sessions and manages explicit attachment semantics.
/// </summary>
public interface ISessionCoordinator
{
    /// <summary>
    /// Returns sessions ordered with the attached id marked when present.
    /// </summary>
    Task<IReadOnlyList<SessionSummaryRow>> ListSessionsAsync(string workspacePath, CancellationToken cancellationToken);

    /// <summary>
    /// Persists an explicit session attachment for the workspace.
    /// </summary>
    Task AttachSessionAsync(string workspacePath, string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Clears explicit attachment (falls back to latest session behavior).
    /// </summary>
    Task DetachSessionAsync(string workspacePath, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the attached session id, if any.
    /// </summary>
    Task<string?> GetAttachedSessionIdAsync(string workspacePath, CancellationToken cancellationToken);
}
