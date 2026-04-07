using System.ComponentModel;
using ModelContextProtocol.Server;

namespace SharpClaw.Code.Mcp.FixtureServer;

/// <summary>
/// Minimal MCP tools exposed by the integration-test fixture (stdio server).
/// </summary>
[McpServerToolType]
public sealed class EchoTools
{
    /// <summary>
    /// Returns the input unchanged (used to verify list_tools includes at least one tool).
    /// </summary>
    [McpServerTool, Description("Echoes the message back.")]
    public static string Echo(
        [Description("Text to echo.")] string message)
        => message;
}
