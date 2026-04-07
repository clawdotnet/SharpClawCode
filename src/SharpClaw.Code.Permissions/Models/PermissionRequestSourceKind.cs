namespace SharpClaw.Code.Permissions.Models;

/// <summary>
/// Identifies the caller category that requested tool execution.
/// </summary>
public enum PermissionRequestSourceKind
{
    /// <summary>
    /// The runtime itself initiated the request.
    /// </summary>
    Runtime,

    /// <summary>
    /// A plugin initiated the request.
    /// </summary>
    Plugin,

    /// <summary>
    /// An MCP server initiated the request.
    /// </summary>
    Mcp,
}
