using SharpClaw.Code.Memory.Models;

namespace SharpClaw.Code.Memory.Abstractions;

/// <summary>
/// Loads repo-local SharpClaw memory and settings for prompt context assembly.
/// </summary>
public interface IProjectMemoryService
{
    /// <summary>
    /// Builds the effective project memory context for the supplied workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root to inspect.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The project memory context.</returns>
    Task<ProjectMemoryContext> BuildContextAsync(string workspaceRoot, CancellationToken cancellationToken);
}
