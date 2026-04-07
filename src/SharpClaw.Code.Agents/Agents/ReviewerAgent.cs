using SharpClaw.Code.Agents.Abstractions;

namespace SharpClaw.Code.Agents.Agents;

/// <summary>
/// Provides code-review oriented analysis.
/// </summary>
public sealed class ReviewerAgent(IAgentFrameworkBridge agentFrameworkBridge) : SharpClawAgentBase(agentFrameworkBridge)
{
    /// <inheritdoc />
    public override string AgentId => "reviewer-agent";

    /// <inheritdoc />
    public override string AgentKind => "reviewer";

    /// <inheritdoc />
    protected override string Name => "Reviewer Agent";

    /// <inheritdoc />
    protected override string Description => "Reviews implementation choices for correctness and regressions.";

    /// <inheritdoc />
    protected override string Instructions => "You are SharpClaw Code's reviewer agent. Focus on defects, regression risk, missing tests, and unclear behavior.";
}
