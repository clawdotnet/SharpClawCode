using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Mcp.Abstractions;

/// <summary>
/// Stores MCP server definitions and their latest lifecycle status for a workspace.
/// </summary>
public interface IMcpRegistry
{
    /// <summary>
    /// Registers or updates an MCP server definition.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local MCP registry.</param>
    /// <param name="definition">The server definition to persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The registered server snapshot.</returns>
    Task<RegisteredMcpServer> RegisterAsync(string workspaceRoot, McpServerDefinition definition, CancellationToken cancellationToken);

    /// <summary>
    /// Lists all registered MCP servers for the workspace.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local MCP registry.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The registered servers.</returns>
    Task<IReadOnlyList<RegisteredMcpServer>> ListAsync(string workspaceRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a registered MCP server by identifier.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local MCP registry.</param>
    /// <param name="serverId">The server identifier to resolve.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching server, if present.</returns>
    Task<RegisteredMcpServer?> GetAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the latest persisted status for a registered server.
    /// </summary>
    /// <param name="workspaceRoot">The workspace root containing the local MCP registry.</param>
    /// <param name="status">The status to persist.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task UpdateStatusAsync(string workspaceRoot, McpServerStatus status, CancellationToken cancellationToken);
}
