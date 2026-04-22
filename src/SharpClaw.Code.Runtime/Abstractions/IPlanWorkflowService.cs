using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Provides deep-planning prompt instructions and post-processing for plan-mode turns.
/// </summary>
public interface IPlanWorkflowService
{
    /// <summary>
    /// Builds additional prompt instructions for deep plan generation.
    /// </summary>
    string BuildPromptInstructions();

    /// <summary>
    /// Parses plan-mode model output and synchronizes planning-owned session todos.
    /// </summary>
    Task<PlanExecutionResult> MaterializeAsync(
        string workspacePath,
        string sessionId,
        string userPrompt,
        string modelOutput,
        CancellationToken cancellationToken);

    /// <summary>
    /// Renders a concise human-readable completion message for a deep plan result.
    /// </summary>
    string RenderCompletionMessage(PlanExecutionResult result);
}
