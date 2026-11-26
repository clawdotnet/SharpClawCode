using SharpClaw.Code.Mcp.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Mcp.Abstractions;

/// <summary>
/// Starts and stops process-hosted MCP servers.
/// </summary>
public interface IMcpProcessSupervisor
{
    /// <summary>
    /// Starts the process for the supplied MCP server definition.
    /// </summary>
    /// <param name="definition">The server definition to start.</param>
    /// <param name="workingDirectory">The working directory to use.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resulting process start details.</returns>
    Task<McpProcessStartResult> StartAsync(McpServerDefinition definition, string workingDirectory, CancellationToken cancellationToken);

    /// <summary>
    /// Stops a previously started MCP session (SDK client disposal and/or OS process termination).
    /// </summary>
    /// <param name="request">How to locate the running session.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task StopAsync(McpProcessStopRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Reports whether a session handle returned by <see cref="StartAsync"/> is still live in this process.
    /// Used to detect persisted "ready" statuses that outlived the process that created them.
    /// </summary>
    /// <param name="sessionHandle">The session handle.</param>
    /// <returns><see langword="true"/> when the session is tracked and usable.</returns>
    bool IsSessionActive(long sessionHandle) => true;
}
