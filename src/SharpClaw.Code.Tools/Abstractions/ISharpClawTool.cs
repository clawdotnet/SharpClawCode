using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.Abstractions;

/// <summary>
/// Represents an executable SharpClaw tool implementation.
/// </summary>
public interface ISharpClawTool
{
    /// <summary>
    /// Gets the discoverable metadata for the tool.
    /// </summary>
    ToolDefinition Definition { get; }

    /// <summary>
    /// Gets plugin provenance when this tool is surfaced from the plugin subsystem; otherwise <see langword="null"/>.
    /// </summary>
    PluginToolSource? PluginSource { get; }

    /// <summary>
    /// Executes the tool against the supplied context and request.
    /// </summary>
    /// <param name="context">The execution context for the current turn.</param>
    /// <param name="request">The concrete tool execution request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The tool result.</returns>
    Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken);
}
