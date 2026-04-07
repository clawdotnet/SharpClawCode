using SharpClaw.Code.Agents.Abstractions;

namespace SharpClaw.Code.Agents.Agents;

/// <summary>
/// The default coding agent used for prompt execution.
/// </summary>
public sealed class PrimaryCodingAgent(IAgentFrameworkBridge agentFrameworkBridge) : SharpClawAgentBase(agentFrameworkBridge)
{
    /// <inheritdoc />
    public override string AgentId => "primary-coding-agent";

    /// <inheritdoc />
    public override string AgentKind => "primaryCoding";

    /// <inheritdoc />
    protected override string Name => "Primary Coding Agent";

    /// <inheritdoc />
    protected override string Description => "Handles the default coding workflow for prompt execution.";

    /// <inheritdoc />
    protected override string Instructions => "You are SharpClaw Code's primary coding agent. Produce concise, actionable coding help and prefer using the configured tools when they are needed.";
}
