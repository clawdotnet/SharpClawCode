using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SharpClaw.Code.Infrastructure;
using SharpClaw.Code.Permissions;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.BuiltIn;
using SharpClaw.Code.Tools.Execution;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Registry;

namespace McpToolAgent;

/// <summary>
/// Executes opt-in, anonymous Parallel MCP tools through SharpClaw's permission-aware executor.
/// </summary>
public static class ParallelSearchExample
{
    /// <summary>The anonymous Streamable HTTP MCP endpoint.</summary>
    public static readonly Uri Endpoint = new("https://search.parallel.ai/mcp");

    /// <summary>Identifies this example on discovery and tool requests.</summary>
    public const string UserAgent = "SharpClaw.Code-McpToolAgent/0.1.0";

    /// <summary>
    /// Discovers the supported tools, registers their schemas, and executes a single request.
    /// No saved credentials or provider configuration are loaded.
    /// </summary>
    /// <param name="toolName">The local name: parallel_web_search or parallel_web_fetch.</param>
    /// <param name="argumentsJson">Arguments matching the discovered remote tool schema.</param>
    /// <param name="context">The caller's workspace, allowlist, and permission context.</param>
    /// <param name="cancellationToken">Cancels discovery and execution.</param>
    /// <param name="httpClient">Optional caller-owned HTTP client, primarily for transport testing.</param>
    /// <returns>The permission decision and tool result, including remote errors.</returns>
    public static async Task<ToolExecutionEnvelope> ExecuteAsync(
        string toolName,
        string argumentsJson,
        ToolExecutionContext context,
        CancellationToken cancellationToken,
        HttpClient? httpClient = null)
    {
        if (toolName is not ("parallel_web_search" or "parallel_web_fetch"))
        {
            throw new ArgumentException("Choose parallel_web_search or parallel_web_fetch.", nameof(toolName));
        }

        var options = new HttpClientTransportOptions
        {
            Endpoint = Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            ConnectionTimeout = TimeSpan.FromSeconds(30),
            AdditionalHeaders = new Dictionary<string, string> { ["User-Agent"] = UserAgent }
        };
        var transport = httpClient is null
            ? new HttpClientTransport(options)
            : new HttpClientTransport(options, httpClient, ownsHttpClient: false);
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        var discovered = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var remoteName = toolName["parallel_".Length..];
        var remoteTool = discovered.Single(tool => tool.Name == remoteName);
        var registry = new ToolRegistry([new ParallelMcpTool(client, remoteTool)]);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSharpClawInfrastructure();
        services.AddSharpClawPermissions();
        using var provider = services.BuildServiceProvider();
        var executor = new ToolExecutor(registry, provider.GetRequiredService<IPermissionPolicyEngine>());
        return await executor.ExecuteAsync(toolName, argumentsJson, context, cancellationToken);
    }

    private sealed class ParallelMcpTool(McpClient client, McpClientTool remoteTool) : SharpClawToolBase
    {
        public override ToolDefinition Definition { get; } = new(
            "parallel_" + remoteTool.Name,
            remoteTool.Description ?? "Search or fetch public web content through Parallel MCP.",
            ApprovalScope.ToolExecution,
            IsDestructive: false,
            RequiresApproval: false,
            InputTypeName: "ParallelMcpArguments",
            InputDescription: "Arguments follow the discovered MCP input schema.",
            Tags: ["web", "network", "parallel", "mcp"],
            InputSchemaJson: remoteTool.JsonSchema.GetRawText());

        public override async Task<ToolResult> ExecuteAsync(
            ToolExecutionContext context,
            ToolExecutionRequest request,
            CancellationToken cancellationToken)
        {
            var arguments = JsonSerializer.Deserialize<Dictionary<string, object?>>(request.ArgumentsJson)
                ?? throw new ArgumentException("Tool arguments must be a JSON object.");
            var result = await client.CallToolAsync(remoteTool.Name, arguments, cancellationToken: cancellationToken);
            var output = string.Join(Environment.NewLine,
                result.Content.OfType<TextContentBlock>().Select(block => block.Text));
            if (result.IsError == true)
            {
                return CreateFailureResult(context, request, output);
            }

            return CreateSuccessResult(context, request, output, result);
        }
    }
}
