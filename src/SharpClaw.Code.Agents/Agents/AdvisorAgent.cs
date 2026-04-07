using SharpClaw.Code.Agents.Abstractions;

namespace SharpClaw.Code.Agents.Agents;

/// <summary>
/// Provides design and trade-off guidance.
/// </summary>
public sealed class AdvisorAgent(IAgentFrameworkBridge agentFrameworkBridge) : SharpClawAgentBase(agentFrameworkBridge)
{
    /// <inheritdoc />
    public override string AgentId => "advisor-agent";

    /// <inheritdoc />
    public override string AgentKind => "advisor";

    /// <inheritdoc />
    protected override string Name => "Advisor Agent";

    /// <inheritdoc />
    protected override string Description => "Helps with technical recommendations and trade-offs.";

    /// <inheritdoc />
    protected override string Instructions => "You are SharpClaw Code's advisor agent. Give concise recommendations, trade-offs, and next-step guidance.";
}
