using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Mcp.Abstractions;

/// <summary>
/// Controls the lifecycle of workspace-scoped MCP server processes.
/// </summary>
public interface IMcpServerHost
{
    /// <summary>
    /// Starts a registered MCP server.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local MCP registry.</param>
    /// <param name="serverId">The server identifier to start.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resulting server status.</returns>
    Task<McpServerStatus> StartAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken);

    /// <summary>
    /// Stops a registered MCP server.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local MCP registry.</param>
    /// <param name="serverId">The server identifier to stop.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resulting server status.</returns>
    Task<McpServerStatus> StopAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken);

    /// <summary>
    /// Restarts a registered MCP server.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local MCP registry.</param>
    /// <param name="serverId">The server identifier to restart.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resulting server status.</returns>
    Task<McpServerStatus> RestartAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the latest known status for a server.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local MCP registry.</param>
    /// <param name="serverId">The server identifier to inspect.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resulting server status.</returns>
    Task<McpServerStatus?> GetStatusAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken);
}
