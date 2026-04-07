using SharpClaw.Code.Agents.Models;

namespace SharpClaw.Code.Agents.Abstractions;

/// <summary>
/// Encapsulates Microsoft Agent Framework-specific execution details.
/// </summary>
public interface IAgentFrameworkBridge
{
    /// <summary>
    /// Runs a logical SharpClaw agent through the underlying framework bridge.
    /// </summary>
    /// <param name="request">The framework invocation request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resulting agent run result.</returns>
    Task<AgentRunResult> RunAsync(AgentFrameworkRequest request, CancellationToken cancellationToken);
}
