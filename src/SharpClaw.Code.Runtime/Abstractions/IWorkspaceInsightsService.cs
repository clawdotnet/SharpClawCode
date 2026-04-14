using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Builds usage, cost, and execution stats summaries from persisted workspace state.
/// </summary>
public interface IWorkspaceInsightsService
{
    /// <summary>
    /// Builds a usage report for the workspace and its sessions.
    /// </summary>
    Task<WorkspaceUsageReport> BuildUsageReportAsync(string workspaceRoot, string? currentSessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Builds a cost report for the workspace and its sessions.
    /// </summary>
    Task<WorkspaceCostReport> BuildCostReportAsync(string workspaceRoot, string? currentSessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Builds execution counters for the workspace.
    /// </summary>
    Task<WorkspaceStatsReport> BuildStatsReportAsync(string workspaceRoot, string? currentSessionId, CancellationToken cancellationToken);
}
