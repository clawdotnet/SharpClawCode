using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Sessions.Abstractions;

/// <summary>
/// Persists runtime checkpoints for recovery.
/// </summary>
public interface ICheckpointStore
{
    /// <summary>
    /// Saves a runtime checkpoint.
    /// </summary>
    /// <param name="workspacePath">The workspace root path.</param>
    /// <param name="checkpoint">The checkpoint to save.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task SaveAsync(string workspacePath, RuntimeCheckpoint checkpoint, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the latest checkpoint for a session.
    /// </summary>
    /// <param name="workspacePath">The workspace root path.</param>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The latest checkpoint, if any.</returns>
    Task<RuntimeCheckpoint?> GetLatestAsync(string workspacePath, string sessionId, CancellationToken cancellationToken);
}
