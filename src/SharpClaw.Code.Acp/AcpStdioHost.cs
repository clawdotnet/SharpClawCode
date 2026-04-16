using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Acp;

/// <summary>
/// Minimal Agent Client Protocol (ACP) JSON-RPC loop over stdio, aligned with common IDE subprocess integrations.
/// </summary>
public sealed class AcpStdioHost(
    IConversationRuntime conversationRuntime,
    IWorkspaceSessionAttachmentStore attachmentStore,
    IEditorContextBuffer editorContextBuffer,
    IWorkspaceIndexService workspaceIndexService,
    IWorkspaceSearchService workspaceSearchService,
    IPersistentMemoryStore persistentMemoryStore,
    IProviderCatalogService providerCatalogService,
    AcpApprovalCoordinator approvalCoordinator,
    IPathService pathService,
    ILogger<AcpStdioHost> logger)
{
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private Func<JsonObject, Task>? notificationWriter;

    /// <summary>
    /// Processes newline-delimited JSON-RPC requests until the input stream ends.
    /// </summary>
    public async Task RunAsync(TextReader stdin, TextWriter stdout, CancellationToken cancellationToken)
    {
        approvalCoordinator.Configure(
            supportsApprovals: approvalCoordinator.SupportsApprovals,
            notificationWriter: payload => WriteJsonLineAsync(stdout, payload, cancellationToken));
        notificationWriter = payload => WriteJsonLineAsync(stdout, payload, cancellationToken);

        var inFlight = new List<Task>();
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await stdin.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            JsonNode? root;
            try
            {
                root = JsonNode.Parse(line);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "ACP received non-JSON line.");
                await WriteErrorAsync(stdout, null, -32700, "Parse error.", cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (root is not JsonObject requestObject)
            {
                await WriteErrorAsync(stdout, null, -32600, "Invalid request.", cancellationToken).ConfigureAwait(false);
                continue;
            }

            inFlight.Add(ProcessRequestAsync(requestObject, stdout, cancellationToken));
            inFlight.RemoveAll(static task => task.IsCompleted);
        }

        if (inFlight.Count > 0)
        {
            await Task.WhenAll(inFlight).ConfigureAwait(false);
        }
    }

    private async Task ProcessRequestAsync(JsonObject requestObject, TextWriter stdout, CancellationToken cancellationToken)
    {
        var id = requestObject["id"];
        var method = requestObject["method"]?.GetValue<string>();
        if (!string.Equals(requestObject["jsonrpc"]?.GetValue<string>(), "2.0", StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(method)
            || id is null)
        {
            await WriteErrorAsync(stdout, id, -32600, "Invalid request.", cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            var response = await DispatchAsync(method, requestObject["params"], stdout, cancellationToken).ConfigureAwait(false);
            await WriteResponseAsync(stdout, id, response, cancellationToken).ConfigureAwait(false);
        }
        catch (AcpJsonRpcException ex)
        {
            logger.LogWarning(ex, "ACP request failed for method {Method} with JSON-RPC error {Code}.", method, ex.Code);
            await WriteErrorAsync(stdout, id, ex.Code, ex.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ACP request failed for method {Method}.", method);
            await WriteErrorAsync(stdout, id, -32603, ex.Message, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<JsonNode?> DispatchAsync(
        string method,
        JsonNode? parameters,
        TextWriter stdout,
        CancellationToken cancellationToken)
        => method switch
        {
            "initialize" => await Task.FromResult<JsonNode?>(HandleInitialize(parameters)).ConfigureAwait(false),
            "session/new" => await HandleSessionNewAsync(parameters, cancellationToken).ConfigureAwait(false),
            "session/load" => await HandleSessionLoadAsync(parameters, cancellationToken).ConfigureAwait(false),
            "session/prompt" => await HandleSessionPromptAsync(parameters, stdout, cancellationToken).ConfigureAwait(false),
            "models/list" => await HandleModelsListAsync(cancellationToken).ConfigureAwait(false),
            "workspace/index/refresh" => await HandleWorkspaceIndexRefreshAsync(parameters, cancellationToken).ConfigureAwait(false),
            "workspace/search" => await HandleWorkspaceSearchAsync(parameters, cancellationToken).ConfigureAwait(false),
            "memory/list" => await HandleMemoryListAsync(parameters, cancellationToken).ConfigureAwait(false),
            "memory/save" => await HandleMemorySaveAsync(parameters, cancellationToken).ConfigureAwait(false),
            "memory/delete" => await HandleMemoryDeleteAsync(parameters, cancellationToken).ConfigureAwait(false),
            "approval/respond" => await HandleApprovalRespondAsync(parameters).ConfigureAwait(false),
            _ => throw new AcpJsonRpcException(-32601, $"Method '{method}' was not found.")
        };

    private JsonObject HandleInitialize(JsonNode? parameters)
    {
        var supportsApprovals = parameters?["clientCapabilities"]?["approvalRequests"]?.GetValue<bool>() ?? false;
        approvalCoordinator.Configure(
            supportsApprovals,
            payload => notificationWriter is null
                ? Task.CompletedTask
                : notificationWriter(payload));

        var capabilities = new JsonObject
        {
            ["loadSession"] = true,
            ["approvalRequests"] = true,
            ["models"] = true,
            ["workspaceSearch"] = true,
            ["workspaceIndex"] = true,
            ["memory"] = true,
            ["promptCapabilities"] = new JsonObject
            {
                ["image"] = false,
                ["audio"] = false,
                ["embeddedContext"] = true,
            },
        };
        return new JsonObject
        {
            ["protocolVersion"] = "v1",
            ["agentCapabilities"] = capabilities,
            ["serverInfo"] = new JsonObject
            {
                ["name"] = "sharpclaw-code",
                ["version"] = typeof(AcpStdioHost).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            },
        };
    }

    private async Task<JsonObject> HandleSessionNewAsync(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var workspace = RequireWorkspace(parameters);
        var session = await conversationRuntime
            .CreateSessionAsync(workspace, PermissionMode.WorkspaceWrite, OutputFormat.Json, cancellationToken)
            .ConfigureAwait(false);
        await attachmentStore.SetAttachedSessionIdAsync(workspace, session.Id, cancellationToken).ConfigureAwait(false);
        return new JsonObject
        {
            ["sessionId"] = session.Id,
            ["models"] = new JsonObject
            {
                ["current"] = "default",
                ["available"] = JsonSerializer.SerializeToNode(
                    (await providerCatalogService.ListAsync(cancellationToken).ConfigureAwait(false)).ToList(),
                    ProtocolJsonContext.Default.ListProviderModelCatalogEntry),
            },
        };
    }

    private async Task<JsonObject> HandleSessionLoadAsync(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var sessionId = parameters?["sessionId"]?.GetValue<string>() ?? throw new InvalidOperationException("session/load requires sessionId.");
        var workspace = RequireWorkspace(parameters);
        var session = await conversationRuntime
            .GetSessionAsync(workspace, sessionId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidOperationException($"Session '{sessionId}' not found.");
        }

        await attachmentStore.SetAttachedSessionIdAsync(workspace, sessionId, cancellationToken).ConfigureAwait(false);
        return new JsonObject
        {
            ["models"] = new JsonObject
            {
                ["current"] = "default",
            },
        };
    }

    private async Task<JsonObject> HandleSessionPromptAsync(
        JsonNode? parameters,
        TextWriter stdout,
        CancellationToken cancellationToken)
    {
        var sessionId = parameters?["sessionId"]?.GetValue<string>() ?? throw new InvalidOperationException("session/prompt requires sessionId.");
        var workspace = RequireWorkspace(parameters);
        var promptText = ExtractPromptText(parameters?["prompt"]);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["acp"] = "true",
        };
        if (parameters?["model"]?.GetValue<string>() is { Length: > 0 } model)
        {
            metadata["model"] = model;
        }

        if (parameters?["editorContext"] is JsonNode editorContextNode)
        {
            var editorContext = DeserializeEditorContext(editorContextNode, workspace, sessionId);
            editorContextBuffer.Publish(editorContext);
        }

        var turn = await conversationRuntime
            .RunPromptAsync(
                new RunPromptRequest(
                    Prompt: promptText,
                    SessionId: sessionId,
                    WorkingDirectory: workspace,
                    PermissionMode: PermissionMode.WorkspaceWrite,
                    OutputFormat: OutputFormat.Json,
                    Metadata: metadata,
                    IsInteractive: approvalCoordinator.SupportsApprovals),
                cancellationToken)
            .ConfigureAwait(false);

        await WriteJsonLineAsync(
            stdout,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "session/notification",
                ["params"] = new JsonObject
                {
                    ["sessionId"] = sessionId,
                    ["update"] = new JsonObject
                    {
                        ["sessionUpdate"] = "agentMessageChunk",
                        ["chunk"] = new JsonObject
                        {
                            ["content"] = new JsonObject { ["type"] = "text", ["text"] = turn.FinalOutput ?? string.Empty },
                        },
                    },
                },
            },
            cancellationToken).ConfigureAwait(false);

        return new JsonObject
        {
            ["stopReason"] = "endTurn",
        };
    }

    private async Task<JsonNode?> HandleModelsListAsync(CancellationToken cancellationToken)
        => JsonSerializer.SerializeToNode(
            (await providerCatalogService.ListAsync(cancellationToken).ConfigureAwait(false)).ToList(),
            ProtocolJsonContext.Default.ListProviderModelCatalogEntry);

    private async Task<JsonNode?> HandleWorkspaceIndexRefreshAsync(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var workspace = RequireWorkspace(parameters);
        var result = await workspaceIndexService.RefreshAsync(workspace, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToNode(result, ProtocolJsonContext.Default.WorkspaceIndexRefreshResult);
    }

    private async Task<JsonNode?> HandleWorkspaceSearchAsync(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var workspace = RequireWorkspace(parameters);
        var request = new WorkspaceSearchRequest(
            Query: parameters?["query"]?.GetValue<string>() ?? string.Empty,
            Limit: parameters?["limit"]?.GetValue<int?>(),
            IncludeSymbols: parameters?["includeSymbols"]?.GetValue<bool?>() ?? true,
            IncludeSemantic: parameters?["includeSemantic"]?.GetValue<bool?>() ?? true);
        var result = await workspaceSearchService.SearchAsync(workspace, request, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.SerializeToNode(result, ProtocolJsonContext.Default.WorkspaceSearchResult);
    }

    private async Task<JsonNode?> HandleMemoryListAsync(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var workspace = parameters?["cwd"]?.GetValue<string>();
        var normalizedWorkspace = string.IsNullOrWhiteSpace(workspace) ? null : pathService.GetFullPath(workspace);
        var scopeText = parameters?["scope"]?.GetValue<string>();
        var scope = Enum.TryParse<MemoryScope>(scopeText, true, out var parsedScope) ? (MemoryScope?)parsedScope : null;
        var rows = await persistentMemoryStore
            .ListAsync(normalizedWorkspace, scope, parameters?["query"]?.GetValue<string>(), parameters?["limit"]?.GetValue<int?>() ?? 20, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.SerializeToNode(rows.ToList(), ProtocolJsonContext.Default.ListMemoryEntry);
    }

    private async Task<JsonNode?> HandleMemorySaveAsync(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var workspace = parameters?["cwd"]?.GetValue<string>();
        var normalizedWorkspace = string.IsNullOrWhiteSpace(workspace) ? null : pathService.GetFullPath(workspace);
        var request = parameters?["request"]?.Deserialize(ProtocolJsonContext.Default.MemorySaveRequest)
            ?? throw new InvalidOperationException("memory/save requires request.");
        var now = DateTimeOffset.UtcNow;
        var entry = new MemoryEntry(
            Id: $"memory-{Guid.NewGuid():N}",
            Scope: request.Scope,
            Content: request.Content,
            Source: request.Source,
            SourceSessionId: parameters?["sessionId"]?.GetValue<string>(),
            SourceTurnId: null,
            Tags: request.Tags ?? [],
            Confidence: request.Confidence,
            RelatedFilePath: request.RelatedFilePath,
            RelatedSymbolName: request.RelatedSymbolName,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);
        var saved = await persistentMemoryStore
            .SaveAsync(request.Scope == MemoryScope.Project ? normalizedWorkspace : null, entry, cancellationToken)
            .ConfigureAwait(false);
        return JsonSerializer.SerializeToNode(saved, ProtocolJsonContext.Default.MemoryEntry);
    }

    private async Task<JsonNode?> HandleMemoryDeleteAsync(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var workspace = parameters?["cwd"]?.GetValue<string>();
        var normalizedWorkspace = string.IsNullOrWhiteSpace(workspace) ? null : pathService.GetFullPath(workspace);
        var scope = Enum.Parse<MemoryScope>(parameters?["scope"]?.GetValue<string>() ?? MemoryScope.Project.ToString(), true);
        var id = parameters?["id"]?.GetValue<string>() ?? throw new InvalidOperationException("memory/delete requires id.");
        var deleted = await persistentMemoryStore
            .DeleteAsync(scope == MemoryScope.Project ? normalizedWorkspace : null, scope, id, cancellationToken)
            .ConfigureAwait(false);
        return new JsonObject { ["deleted"] = deleted };
    }

    private Task<JsonNode?> HandleApprovalRespondAsync(JsonNode? parameters)
    {
        var requestId = parameters?["requestId"]?.GetValue<string>() ?? throw new InvalidOperationException("approval/respond requires requestId.");
        var approved = parameters?["approved"]?.GetValue<bool>() ?? false;
        var remember = parameters?["remember"]?.GetValue<bool>() ?? false;
        var resolved = approvalCoordinator.TryResolve(requestId, approved, remember);
        return Task.FromResult<JsonNode?>(new JsonObject { ["resolved"] = resolved });
    }

    private string RequireWorkspace(JsonNode? parameters)
    {
        var cwd = parameters?["cwd"]?.GetValue<string>() ?? throw new InvalidOperationException("cwd is required.");
        return pathService.GetFullPath(cwd);
    }

    private static EditorContextPayload DeserializeEditorContext(JsonNode node, string workspaceRoot, string sessionId)
    {
        if (node.Deserialize(ProtocolJsonContext.Default.EditorContextPayload) is { } payload)
        {
            return string.IsNullOrWhiteSpace(payload.SessionId) ? payload with { SessionId = sessionId } : payload;
        }

        return new EditorContextPayload(
            WorkspaceRoot: workspaceRoot,
            CurrentFilePath: node["currentFilePath"]?.GetValue<string>(),
            Selection: node["selection"]?.Deserialize(ProtocolJsonContext.Default.TextSelectionRange),
            SessionId: sessionId);
    }

    private static string ExtractPromptText(JsonNode? promptNode)
    {
        if (promptNode is JsonValue v && v.TryGetValue<string>(out var single))
        {
            return single;
        }

        if (promptNode is JsonArray arr)
        {
            var parts = new List<string>();
            foreach (var item in arr)
            {
                if (item?["type"]?.GetValue<string>() is "text"
                    && item["text"]?.GetValue<string>() is { } text)
                {
                    parts.Add(text);
                }
            }

            return string.Join(Environment.NewLine, parts);
        }

        throw new InvalidOperationException("session/prompt requires a text prompt payload.");
    }

    private async Task WriteJsonLineAsync(TextWriter stdout, JsonNode node, CancellationToken cancellationToken)
    {
        await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await stdout.WriteLineAsync(node.ToJsonString()).ConfigureAwait(false);
            await stdout.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private Task WriteResponseAsync(TextWriter stdout, JsonNode id, JsonNode? result, CancellationToken cancellationToken)
        => WriteJsonLineAsync(
            stdout,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id.DeepClone(),
                ["result"] = result ?? new JsonObject(),
            },
            cancellationToken);

    private Task WriteErrorAsync(TextWriter stdout, JsonNode? id, int code, string message, CancellationToken cancellationToken)
        => WriteJsonLineAsync(
            stdout,
            new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id?.DeepClone(),
                ["error"] = new JsonObject
                {
                    ["code"] = code,
                    ["message"] = message,
                },
            },
            cancellationToken);

    private sealed class AcpJsonRpcException(int code, string message) : Exception(message)
    {
        public int Code { get; } = code;
    }
}
