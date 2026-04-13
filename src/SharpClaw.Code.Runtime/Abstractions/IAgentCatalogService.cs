using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Resolves the effective agent catalog and prompt defaults for a workspace.
/// </summary>
public interface IAgentCatalogService
{
    /// <summary>
    /// Lists the effective agent catalog for the workspace.
    /// </summary>
    Task<IReadOnlyList<AgentCatalogEntry>> ListAsync(string workspaceRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the effective agent entry for a prompt request.
    /// </summary>
    Task<AgentCatalogEntry> ResolveAsync(
        string workspaceRoot,
        string? requestedAgentId,
        string? persistedAgentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the effective primary mode for an agent when the caller did not set one explicitly.
    /// </summary>
    Task<PrimaryMode?> ResolvePrimaryModeDefaultAsync(
        string workspaceRoot,
        string? requestedAgentId,
        string? persistedAgentId,
        CancellationToken cancellationToken);
}
