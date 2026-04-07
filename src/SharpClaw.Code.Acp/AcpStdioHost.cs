using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Acp;

/// <summary>
/// Minimal Agent Client Protocol (ACP) JSON-RPC loop over stdio, aligned with common IDE subprocess integrations.
/// </summary>
/// <remarks>
/// Intentionally unsupported (JSON-RPC errors or no-ops): streaming tool execution updates, interactive permission prompts,
/// image/audio prompt parts, MCP hot-plug, session/cancel reliability, and vendor extensions.
/// </remarks>
public sealed class AcpStdioHost(
    IConversationRuntime conversationRuntime,
    IWorkspaceSessionAttachmentStore attachmentStore,
    IPathService pathService,
    ILogger<AcpStdioHost> logger)
{
    /// <summary>
    /// Processes newline-delimited JSON-RPC requests until the input stream ends.
    /// </summary>
    public async Task RunAsync(TextReader stdin, TextWriter stdout, CancellationToken cancellationToken)
    {
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
                continue;
            }

            if (root is null)
            {
                continue;
            }

            var method = root["method"]?.GetValue<string>();
            var id = root["id"];

            if (method is null || id is null)
            {
                continue;
            }

            try
            {
                var response = await DispatchAsync(method, root["params"], stdout, cancellationToken).ConfigureAwait(false);
                await WriteResponseAsync(stdout, id, response).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ACP request failed for method {Method}.", method);
                await WriteErrorAsync(stdout, id, -32603, ex.Message).ConfigureAwait(false);
            }

            await stdout.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<JsonNode?> DispatchAsync(
        string method,
        JsonNode? parameters,
        TextWriter stdout,
        CancellationToken cancellationToken)
        => method switch
        {
            "initialize" => await Task.FromResult<JsonNode?>(BuildInitializeResult()).ConfigureAwait(false),
            "session/new" => await HandleSessionNewAsync(parameters, cancellationToken).ConfigureAwait(false),
            "session/load" => await HandleSessionLoadAsync(parameters, cancellationToken).ConfigureAwait(false),
            "session/prompt" => await HandleSessionPromptAsync(parameters, stdout, cancellationToken).ConfigureAwait(false),
            _ => throw new InvalidOperationException($"Unsupported ACP method '{method}'.")
        };

    private static JsonObject BuildInitializeResult()
    {
        var capabilities = new JsonObject
        {
            ["loadSession"] = true,
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
        var cwd = parameters?["cwd"]?.GetValue<string>() ?? throw new InvalidOperationException("session/new requires cwd.");
        var workspace = pathService.GetFullPath(cwd);
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
                ["available"] = new JsonArray(),
            },
        };
    }

    private async Task<JsonObject> HandleSessionLoadAsync(JsonNode? parameters, CancellationToken cancellationToken)
    {
        var sessionId = parameters?["sessionId"]?.GetValue<string>() ?? throw new InvalidOperationException("session/load requires sessionId.");
        var cwd = parameters?["cwd"]?.GetValue<string>() ?? throw new InvalidOperationException("session/load requires cwd.");
        var workspace = pathService.GetFullPath(cwd);
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
        var cwd = parameters?["cwd"]?.GetValue<string>() ?? throw new InvalidOperationException("session/prompt requires cwd.");
        var workspace = pathService.GetFullPath(cwd);

        var promptText = ExtractPromptText(parameters?["prompt"]);

        var turn = await conversationRuntime
            .RunPromptAsync(
                new RunPromptRequest(
                    Prompt: promptText,
                    SessionId: sessionId,
                    WorkingDirectory: workspace,
                    PermissionMode: PermissionMode.WorkspaceWrite,
                    OutputFormat: OutputFormat.Json,
                    Metadata: new Dictionary<string, string> { ["acp"] = "true" }),
                cancellationToken)
            .ConfigureAwait(false);

        var chunk = new JsonObject
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
        };
        await stdout.WriteLineAsync(chunk.ToJsonString()).ConfigureAwait(false);

        return new JsonObject
        {
            ["stopReason"] = "endTurn",
        };
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

    private static async Task WriteResponseAsync(TextWriter stdout, JsonNode id, JsonNode? result)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["result"] = result ?? new JsonObject(),
        };
        await stdout.WriteLineAsync(response.ToJsonString()).ConfigureAwait(false);
    }

    private static async Task WriteErrorAsync(TextWriter stdout, JsonNode id, int code, string message)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message,
            },
        };
        await stdout.WriteLineAsync(response.ToJsonString()).ConfigureAwait(false);
    }
}
