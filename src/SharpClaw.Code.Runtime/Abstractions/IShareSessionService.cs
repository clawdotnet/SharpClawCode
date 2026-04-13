using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Creates, resolves, and removes self-hosted session share snapshots.
/// </summary>
public interface IShareSessionService
{
    /// <summary>
    /// Creates or refreshes a share snapshot for the session.
    /// </summary>
    Task<ShareSessionRecord> CreateShareAsync(string workspaceRoot, string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the share snapshot for the session when one exists.
    /// </summary>
    Task<bool> RemoveShareAsync(string workspaceRoot, string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a shared session snapshot by share id.
    /// </summary>
    Task<SharedSessionSnapshot?> GetSharedSnapshotAsync(string workspaceRoot, string shareId, CancellationToken cancellationToken);
}
