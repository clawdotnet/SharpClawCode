using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Loads merged SharpClaw configuration from user and workspace JSONC documents.
/// </summary>
public interface ISharpClawConfigService
{
    /// <summary>
    /// Loads the effective configuration snapshot for the workspace.
    /// </summary>
    /// <param name="workspaceRoot">Workspace root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The merged configuration snapshot.</returns>
    Task<SharpClawConfigSnapshot> GetConfigAsync(string workspaceRoot, CancellationToken cancellationToken);
}
