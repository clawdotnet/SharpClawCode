using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Abstractions;

/// <summary>
/// Executes hybrid workspace search over indexed chunks and symbols.
/// </summary>
public interface IWorkspaceSearchService
{
    /// <summary>
    /// Searches the indexed workspace.
    /// </summary>
    Task<WorkspaceSearchResult> SearchAsync(
        string workspaceRoot,
        WorkspaceSearchRequest request,
        CancellationToken cancellationToken);
}
