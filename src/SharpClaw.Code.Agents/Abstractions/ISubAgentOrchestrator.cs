using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Agents.Abstractions;

/// <summary>
/// Executes bounded delegated subagent tasks on behalf of a parent agent tool call.
/// </summary>
public interface ISubAgentOrchestrator
{
    /// <summary>
    /// Executes the supplied delegated tasks using the bounded subagent worker.
    /// </summary>
    /// <param name="request">The delegated task batch.</param>
    /// <param name="context">The parent tool execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The batch execution result and emitted runtime events.</returns>
    Task<SubAgentBatchExecutionResult> ExecuteAsync(
        SubAgentBatchRequest request,
        ToolExecutionContext context,
        CancellationToken cancellationToken);
}
