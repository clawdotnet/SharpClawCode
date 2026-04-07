using SharpClaw.Code.Agents.Abstractions;

namespace SharpClaw.Code.Agents.Agents;

/// <summary>
/// Provides recovery-oriented assistance after interrupted or failed work.
/// </summary>
public sealed class RecoveryAgent(IAgentFrameworkBridge agentFrameworkBridge) : SharpClawAgentBase(agentFrameworkBridge)
{
    /// <inheritdoc />
    public override string AgentId => "recovery-agent";

    /// <inheritdoc />
    public override string AgentKind => "recovery";

    /// <inheritdoc />
    protected override string Name => "Recovery Agent";

    /// <inheritdoc />
    protected override string Description => "Helps recover from interrupted or failed execution states.";

    /// <inheritdoc />
    protected override string Instructions => "You are SharpClaw Code's recovery agent. Help resume work safely using the existing session context and checkpoints.";
}
