using SharpClaw.Code.Agents.Models;

namespace SharpClaw.Code.Agents.Abstractions;

/// <summary>
/// Represents a logical SharpClaw agent role.
/// </summary>
public interface ISharpClawAgent
{
    /// <summary>
    /// Gets the stable agent identifier.
    /// </summary>
    string AgentId { get; }

    /// <summary>
    /// Gets the logical agent kind.
    /// </summary>
    string AgentKind { get; }

    /// <summary>
    /// Runs the agent for the supplied context.
    /// </summary>
    /// <param name="context">The agent run context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The agent run result.</returns>
    Task<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken);
}
