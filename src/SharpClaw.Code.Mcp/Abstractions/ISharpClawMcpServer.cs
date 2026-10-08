using SharpClaw.Code.Mcp.Models;

namespace SharpClaw.Code.Mcp.Abstractions;

/// <summary>Hosts the inbound SharpClaw tool surface without starting agent or outbound MCP lifecycles.</summary>
public interface ISharpClawMcpServer
{
    /// <summary>Runs until cancellation or transport closure; stdout is reserved for stdio protocol traffic.</summary>
    Task RunAsync(SharpClawMcpServerOptions options, CancellationToken cancellationToken);
}
