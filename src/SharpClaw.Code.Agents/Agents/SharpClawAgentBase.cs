using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Models;

namespace SharpClaw.Code.Agents.Agents;

/// <summary>
/// Provides shared behavior for concrete SharpClaw agents.
/// </summary>
public abstract class SharpClawAgentBase(IAgentFrameworkBridge agentFrameworkBridge) : ISharpClawAgent
{
    /// <inheritdoc />
    public abstract string AgentId { get; }

    /// <inheritdoc />
    public abstract string AgentKind { get; }

    /// <summary>
    /// Gets the human-readable agent name.
    /// </summary>
    protected abstract string Name { get; }

    /// <summary>
    /// Gets the agent description.
    /// </summary>
    protected abstract string Description { get; }

    /// <summary>
    /// Gets the system instructions for the agent.
    /// </summary>
    protected abstract string Instructions { get; }

    /// <inheritdoc />
    public virtual Task<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
        => agentFrameworkBridge.RunAsync(
            new AgentFrameworkRequest(
                AgentId,
                AgentKind,
                Name,
                Description,
                Instructions,
                context),
            cancellationToken);
}
