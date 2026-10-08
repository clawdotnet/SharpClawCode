using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Mcp.Models;

namespace SharpClaw.Code.Mcp.Server;

/// <summary>Runs the official SDK transport with an explicit permission-aware tool bridge.</summary>
public sealed partial class SharpClawMcpServer(SharpClawMcpToolBridge bridge) : ISharpClawMcpServer
{
    /// <inheritdoc />
    public async Task RunAsync(SharpClawMcpServerOptions options, CancellationToken cancellationToken)
    {
        bridge.Validate(options);
        if (options.Transport == SharpClawMcpTransport.Http)
        {
            await RunHttpAsync(options, cancellationToken).ConfigureAwait(false);
            return;
        }
        var serverOptions = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "SharpClaw.Code", Version = typeof(SharpClawMcpServer).Assembly.GetName().Version?.ToString() ?? "1.0.0" },
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            Handlers = new McpServerHandlers
            {
                ListToolsHandler = (request, ct) => bridge.ListAsync(options, ct),
                CallToolHandler = (request, ct) => bridge.CallAsync(options, request, ct),
            },
        };
        await using var server = McpServer.Create(new StdioServerTransport(serverOptions, NullLoggerFactory.Instance), serverOptions);
        await server.RunAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Registers the inbound host; shared runtime services must also be registered.</summary>
public static class McpServerServiceCollectionExtensions
{
    /// <summary>Adds the server without starting runtime or outbound MCP hosted services.</summary>
    public static IServiceCollection AddSharpClawMcpServer(this IServiceCollection services)
    {
        services.AddSingleton<SharpClawMcpToolBridge>();
        services.AddSingleton<ISharpClawMcpServer, SharpClawMcpServer>();
        return services;
    }
}
