using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Builds a lightweight diagnostics snapshot for the active workspace.
/// </summary>
public interface IWorkspaceDiagnosticsService
{
    /// <summary>
    /// Builds or returns a cached diagnostics snapshot for the workspace.
    /// </summary>
    Task<WorkspaceDiagnosticsSnapshot> BuildSnapshotAsync(string workspaceRoot, CancellationToken cancellationToken);
}
