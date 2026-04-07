namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents a registered MCP server definition paired with its latest lifecycle status.
/// </summary>
/// <param name="Definition">The persisted server definition.</param>
/// <param name="Status">The latest persisted lifecycle status.</param>
public sealed record RegisteredMcpServer(
    McpServerDefinition Definition,
    McpServerStatus Status);
