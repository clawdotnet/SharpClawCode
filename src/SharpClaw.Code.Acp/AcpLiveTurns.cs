using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Turns;

namespace SharpClaw.Code.Acp;

/// <summary>Routes opt-in controls only to prompts registered on this stdio connection.</summary>
internal sealed class AcpLiveTurns(ILiveTurnControlService? controls, Func<JsonObject, Task> notify, CancellationToken disconnected)
{
    private sealed record ActivePrompt(string Workspace, string SessionId, LiveTurnHandle Handle);
    private readonly ConcurrentDictionary<string, ActivePrompt> active = new(StringComparer.Ordinal);

    internal async Task<JsonObject> RunAsync(JsonNode id, string workspace, string sessionId, Func<CancellationToken, Task<JsonObject>> run)
    {
        if (controls is null) throw new AcpRequestException(-32601, "Live turn control is unavailable.");
        using var handle = controls.Begin(disconnected);
        var key = id.ToJsonString();
        if (!active.TryAdd(key, new(Path.TrimEndingDirectorySeparator(workspace), sessionId, handle)))
            throw new AcpRequestException(-32600, "This prompt request ID is already active.");

        var progress = PublishProgressAsync(handle, id, sessionId);
        JsonObject result;
        try
        {
            try
            {
                result = await run(handle.CancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (handle.CancellationToken.IsCancellationRequested)
            {
                result = new JsonObject { ["stopReason"] = "cancelled" };
            }
            catch (Exception ex)
            {
                var data = new JsonObject();
                AddPending(data, handle.CloseSteering());
                throw new AcpRequestException(-32603, ex.Message, data, ex);
            }
            AddPending(result, handle.CloseSteering());
        }
        finally
        {
            active.TryRemove(key, out _);
            handle.Dispose(); // Complete the event stream and drain it before replying.
            await progress.ConfigureAwait(false);
        }
        return result;
    }

    internal JsonObject Control(string workspace, JsonNode? parameters, bool cancel)
    {
        var sessionId = parameters?["sessionId"]?.GetValue<string>();
        var id = parameters?["promptRequestId"];
        if (string.IsNullOrEmpty(sessionId) || id is not JsonValue)
            throw new AcpRequestException(-32602, "sessionId and promptRequestId are required.");
        var text = cancel ? null : parameters?["text"]?.GetValue<string>();
        if (!cancel && string.IsNullOrWhiteSpace(text))
            throw new AcpRequestException(-32602, "A steering message requires text.");
        if (!active.TryGetValue(id.ToJsonString(), out var target) || target.SessionId != sessionId ||
            !string.Equals(target.Workspace, Path.TrimEndingDirectorySeparator(workspace),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return new JsonObject { ["accepted"] = false };

        if (cancel) target.Handle.Cancel();
        return new JsonObject { ["accepted"] = cancel || target.Handle.TrySteer(text!) };
    }

    /// <summary>
    /// Handles a spec-shaped <c>session/cancel</c> notification, which carries only <c>sessionId</c>:
    /// cancels every live-controlled prompt for that session on this connection, optionally narrowed by <c>cwd</c>.
    /// </summary>
    internal int CancelSession(JsonNode? parameters, string? workspace)
    {
        var sessionId = parameters?["sessionId"] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
        if (string.IsNullOrEmpty(sessionId)) return 0;
        workspace = workspace is null ? null : Path.TrimEndingDirectorySeparator(workspace);
        var cancelled = 0;
        foreach (var prompt in active.Values)
        {
            if (prompt.SessionId != sessionId) continue;
            if (workspace is not null && !string.Equals(prompt.Workspace, workspace,
                    OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
            prompt.Handle.Cancel();
            cancelled++;
        }

        return cancelled;
    }

    private static void AddPending(JsonObject result, IReadOnlyList<string> pending)
    {
        if (pending.Count == 0) return;
        var array = new JsonArray();
        foreach (var message in pending) array.Add(message);
        result["pendingSteering"] = array;
    }

    private async Task PublishProgressAsync(LiveTurnHandle handle, JsonNode id, string sessionId)
    {
        await foreach (var envelope in handle.ReadEventsAsync().ConfigureAwait(false))
        {
            if (envelope.SessionId != sessionId) continue;
            JsonObject? update = envelope.Event switch
            {
                TurnStartedEvent => new() { ["kind"] = "turnStarted" },
                ToolStartedEvent tool => new() { ["kind"] = "toolStarted", ["toolName"] = tool.Request.ToolName },
                ToolCompletedEvent tool => new() { ["kind"] = "toolCompleted", ["toolName"] = tool.Result.ToolName, ["succeeded"] = tool.Result.Succeeded },
                ContextCompactedEvent => new() { ["kind"] = "contextCompacted" },
                TurnSteeredEvent => new() { ["kind"] = "steeringAccepted" },
                _ => null
            };
            if (update is null) continue;
            update["sessionUpdate"] = "turnProgress";
            update["turnId"] = envelope.Event.TurnId;
            await notify(new JsonObject
            {
                ["jsonrpc"] = "2.0", ["method"] = "session/notification",
                ["params"] = new JsonObject { ["sessionId"] = sessionId, ["promptRequestId"] = id.DeepClone(), ["update"] = update }
            }).ConfigureAwait(false);
        }
    }
}

/// <summary>Maps an ACP failure to a JSON-RPC error with optional recoverable client data.</summary>
internal sealed class AcpRequestException(int code, string message, JsonObject? details = null, Exception? inner = null) : Exception(message, inner)
{
    internal int Code { get; } = code;
    internal JsonObject? Details { get; } = details;
}
