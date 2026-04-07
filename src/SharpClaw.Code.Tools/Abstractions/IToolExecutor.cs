using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.Abstractions;

/// <summary>
/// Mediates permission-checked tool execution.
/// </summary>
public interface IToolExecutor
{
    /// <summary>
    /// Executes a tool through the registry and permission layer.
    /// </summary>
    /// <param name="toolName">The tool name to execute.</param>
    /// <param name="argumentsJson">The JSON-encoded tool arguments.</param>
    /// <param name="context">The tool execution context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The execution envelope containing the request, decision, and result.</returns>
    Task<ToolExecutionEnvelope> ExecuteAsync(
        string toolName,
        string argumentsJson,
        ToolExecutionContext context,
        CancellationToken cancellationToken);
}
