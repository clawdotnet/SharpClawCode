using SharpClaw.Code.Runtime.Context;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Loads persistent user and workspace instruction files that should be injected into prompt context.
/// </summary>
public interface IInstructionRuleService
{
    /// <summary>
    /// Loads the active instruction rules for the supplied workspace.
    /// </summary>
    /// <param name="workspaceRoot">The normalized workspace root.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A snapshot of discovered instruction documents.</returns>
    Task<InstructionRuleSnapshot> LoadAsync(string workspaceRoot, CancellationToken cancellationToken);
}
