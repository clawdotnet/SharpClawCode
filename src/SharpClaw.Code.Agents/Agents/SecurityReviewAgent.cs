using SharpClaw.Code.Agents.Abstractions;

namespace SharpClaw.Code.Agents.Agents;

/// <summary>
/// Provides security-focused review analysis.
/// </summary>
public sealed class SecurityReviewAgent(IAgentFrameworkBridge agentFrameworkBridge) : SharpClawAgentBase(agentFrameworkBridge)
{
    /// <inheritdoc />
    public override string AgentId => "security-review-agent";

    /// <inheritdoc />
    public override string AgentKind => "securityReview";

    /// <inheritdoc />
    protected override string Name => "Security Review Agent";

    /// <inheritdoc />
    protected override string Description => "Looks for security-sensitive behavior and risky tool usage.";

    /// <inheritdoc />
    protected override string Instructions => "You are SharpClaw Code's security review agent. Focus on dangerous file, shell, network, and trust-boundary issues.";
}
