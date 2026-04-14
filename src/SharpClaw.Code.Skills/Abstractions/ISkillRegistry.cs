using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Skills.Models;

namespace SharpClaw.Code.Skills.Abstractions;

/// <summary>
/// Manages local SharpClaw skill definitions and prompt templates.
/// </summary>
public interface ISkillRegistry
{
    /// <summary>
    /// Lists available skills for the workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local skills directory.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The available skill definitions.</returns>
    Task<IReadOnlyList<SkillDefinition>> ListAsync(string workspaceRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a skill by id or name.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local skills directory.</param>
    /// <param name="skillIdOrName">The skill id or name to resolve.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved skill, if present.</returns>
    Task<ResolvedSkill?> ResolveAsync(string workspaceRoot, string skillIdOrName, CancellationToken cancellationToken);

    /// <summary>
    /// Installs a skill into the local skills directory.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local skills directory.</param>
    /// <param name="request">The install request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The installed skill.</returns>
    Task<ResolvedSkill> InstallAsync(string workspaceRoot, SkillInstallRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Removes an installed skill from the local skills directory.
    /// </summary>
    Task<bool> UninstallAsync(string workspaceRoot, string skillId, CancellationToken cancellationToken);
}
