using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Models;

namespace SharpClaw.Code.Agents.Agents;

/// <summary>
/// Executes bounded delegated tasks with an explicit input/output contract.
/// </summary>
public sealed class SubAgentWorker(IAgentFrameworkBridge agentFrameworkBridge) : SharpClawAgentBase(agentFrameworkBridge)
{
    /// <summary>Stable id for discovery, custom commands, and tests.</summary>
    public const string SubAgentId = "sub-agent-worker";

    /// <inheritdoc />
    public override string AgentId => SubAgentId;

    /// <inheritdoc />
    public override string AgentKind => "subAgent";

    /// <inheritdoc />
    protected override string Name => "Sub-Agent Worker";

    /// <inheritdoc />
    protected override string Description => "Executes bounded delegated tasks on behalf of another agent.";

    /// <inheritdoc />
    protected override string Instructions => "You are a bounded SharpClaw sub-agent. Complete only the delegated task, honor the listed constraints, and return exactly the requested output contract.";

    /// <inheritdoc />
    public override Task<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
    {
        if (context.DelegatedTask is null)
        {
            throw new InvalidOperationException("SubAgentWorker requires a delegated task contract.");
        }

        var taskPrompt = string.Join(
            Environment.NewLine,
            [
                $"Delegated task: {context.DelegatedTask.Goal}",
                $"Expected output: {context.DelegatedTask.ExpectedOutput}",
                "Constraints:",
                .. context.DelegatedTask.Constraints.Select(constraint => $"- {constraint}"),
                "Original request:",
                context.Prompt
            ]);

        return base.RunAsync(context with { Prompt = taskPrompt }, cancellationToken);
    }
}
