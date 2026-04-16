using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Abstractions;

/// <summary>
/// Builds and refreshes the persisted workspace knowledge index.
/// </summary>
public interface IWorkspaceIndexService
{
    /// <summary>
    /// Refreshes the workspace index.
    /// </summary>
    Task<WorkspaceIndexRefreshResult> RefreshAsync(string workspaceRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current workspace index status.
    /// </summary>
    Task<WorkspaceIndexStatus> GetStatusAsync(string workspaceRoot, CancellationToken cancellationToken);
}
