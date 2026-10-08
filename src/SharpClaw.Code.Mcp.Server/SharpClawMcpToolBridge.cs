using System.Runtime.CompilerServices;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Mcp.Models;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Mutations;
using SharpClaw.Code.Runtime.Turns;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Mcp.Server;

/// <summary>Maps inbound MCP calls to the existing executor and durable mutation coordinator.</summary>
public sealed class SharpClawMcpToolBridge(
    IToolRegistry registry,
    IToolExecutor executor,
    IConversationRuntime runtime,
    ICheckpointStore checkpoints,
    ISessionStore sessions,
    CheckpointMutationCoordinator mutations,
    IRuntimeHostContextAccessor host,
    IPathService paths)
{
    private static readonly string[] ReadTools = ["workspace_search", "symbol_search", "dotnet_solution_inspect", "csharp_symbol_resolve", "csharp_find_references", "csharp_type_hierarchy", "csharp_diagnostics", "verify_workspace"];
    private readonly ConditionalWeakTable<McpServer, ClientState> clients = new();

    /// <summary>Validates the fixed workspace before opening the transport.</summary>
    public void Validate(SharpClawMcpServerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!Directory.Exists(options.WorkspaceRoot)) throw new ArgumentException("MCP workspace must be an existing directory.", nameof(options));
    }

    /// <summary>Lists only the host-selected tools with their existing input contracts.</summary>
    public async ValueTask<ListToolsResult> ListAsync(SharpClawMcpServerOptions options, CancellationToken cancellationToken)
    {
        var allowed = Allowed(options);
        var definitions = await registry.ListAsync(options.WorkspaceRoot, cancellationToken).ConfigureAwait(false);
        return new ListToolsResult
        {
            Tools = definitions.Where(d => allowed.Contains(d.Name, StringComparer.Ordinal)).Select(d => new Tool
            {
                Name = d.Name, Description = d.Description,
                InputSchema = JsonSerializer.Deserialize<JsonElement>(d.InputSchemaJson ?? SearchSchema(d.Name)),
                Annotations = new ToolAnnotations { ReadOnlyHint = !d.IsDestructive && d.ApprovalScope != ApprovalScope.ShellExecution, DestructiveHint = d.IsDestructive, OpenWorldHint = false },
            }).ToList(),
        };
    }

    /// <summary>Executes under noninteractive permissions; hidden tools remain denied on direct calls.</summary>
    public async ValueTask<CallToolResult> CallAsync(SharpClawMcpServerOptions options, RequestContext<CallToolRequestParams> request, CancellationToken cancellationToken)
    {
        var toolName = request.Params?.Name;
        var allowed = Allowed(options);
        if (toolName is null || !allowed.Contains(toolName, StringComparer.Ordinal)) return Error("Tool is not exposed by this host.");
        var state = clients.GetValue(request.Server, static _ => new ClientState());
        await state.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            using var scope = host.BeginScope(options.HostContext);
            var root = paths.GetCanonicalFullPath(options.WorkspaceRoot);
            if (options.PermissionMode != PermissionMode.ReadOnly && state.SessionId is null)
                state.SessionId = (await runtime.CreateSessionAsync(root, options.PermissionMode, OutputFormat.Json, cancellationToken).ConfigureAwait(false)).Id;
            var turnId = $"turn-{Guid.NewGuid():N}";
            var recorder = new TurnMutationAccumulator();
            var context = new ToolExecutionContext(state.SessionId ?? "", turnId, root, root,
                options.PermissionMode, OutputFormat.Json, null, AllowedTools: allowed, IsInteractive: false,
                SourceKind: PermissionRequestSourceKind.McpClient, SourceName: "sharpclaw-mcp-server",
                PrimaryMode: options.PrimaryMode, MutationRecorder: recorder, ApprovalSettings: options.ApprovalSettings);
            try
            {
                var args = request.Params?.Arguments;
                var arguments = args is null ? "{}" : JsonSerializer.Serialize(args);
                var envelope = await executor.ExecuteAsync(toolName, arguments, context, cancellationToken).ConfigureAwait(false);
                var result = envelope.Result;
                return new CallToolResult
                {
                    IsError = !result.Succeeded,
                    Content = [new TextContentBlock { Text = result.Output ?? result.ErrorMessage ?? (result.Succeeded ? "Completed." : "Tool failed.") }],
                    StructuredContent = result.StructuredOutputJson is null ? null : JsonSerializer.Deserialize<JsonElement>(result.StructuredOutputJson),
                };
            }
            finally
            {
                // Independent bounded finalization keeps actual edits recoverable even after cancellation/failure.
                var operations = recorder.ToSnapshot();
                if (operations.Count > 0 && state.SessionId is not null)
                {
                    using var finalization = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var session = await sessions.GetByIdAsync(root, state.SessionId, finalization.Token).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("MCP mutation session disappeared.");
                    var checkpointId = $"checkpoint-{Guid.NewGuid():N}";
                    await checkpoints.SaveAsync(root, new RuntimeCheckpoint(checkpointId, session.Id, turnId, DateTimeOffset.UtcNow,
                        $"MCP {toolName}", checkpointId, "mcp-tool", null), finalization.Token).ConfigureAwait(false);
                    await mutations.ApplyRecordedMutationsAsync(root, session, turnId, checkpointId, operations, finalization.Token).ConfigureAwait(false);
                }
            }
        }
        finally { state.Gate.Release(); }
    }

    private static string[] Allowed(SharpClawMcpServerOptions options)
        => options.AllowMutations && options.PermissionMode != PermissionMode.ReadOnly && options.PrimaryMode == PrimaryMode.Build ? [.. ReadTools, "csharp_rename_symbol"] : ReadTools;

    private static CallToolResult Error(string message) => new() { IsError = true, Content = [new TextContentBlock { Text = message }] };
    private static string SearchSchema(string name) => name == "symbol_search"
        ? """{"type":"object","properties":{"query":{"type":"string"},"kind":{"type":"string"},"limit":{"type":"integer","minimum":1}},"required":["query"]}"""
        : """{"type":"object","properties":{"query":{"type":"string"},"limit":{"type":"integer","minimum":1},"includeSymbols":{"type":"boolean"},"includeSemantic":{"type":"boolean"}},"required":["query"]}""";

    private sealed class ClientState
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public string? SessionId { get; set; }
    }
}
