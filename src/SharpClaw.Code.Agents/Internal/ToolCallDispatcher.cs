using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Agents.Internal;

/// <summary>
/// Dispatches tool-use requests from provider events through the permission-aware tool executor
/// and returns content blocks for the provider conversation.
/// </summary>
public sealed class ToolCallDispatcher(
    IToolExecutor toolExecutor,
    IRuntimeEventPublisher eventPublisher)
{
    /// <summary>
    /// Executes a tool call and returns a tool-result content block.
    /// </summary>
    public async Task<(ContentBlock ResultBlock, ToolResult ToolResult, List<RuntimeEvent> Events)> DispatchAsync(
        ProviderEvent toolUseEvent,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(toolUseEvent.ToolName))
        {
            return (new ContentBlock(ContentBlockKind.ToolResult, "Tool call missing required tool name.", toolUseEvent.ToolUseId, null, null, true),
                new ToolResult("unknown", "unknown", false, Protocol.Enums.OutputFormat.Text, null, "Tool call missing required tool name.", 1, null, null), []);
        }

        if (string.IsNullOrWhiteSpace(toolUseEvent.ToolUseId))
        {
            return (new ContentBlock(ContentBlockKind.ToolResult, "Tool call missing required tool use ID.", null, null, null, true),
                new ToolResult("unknown", toolUseEvent.ToolName, false, Protocol.Enums.OutputFormat.Text, null, "Tool call missing required tool use ID.", 1, null, null), []);
        }

        var toolName = toolUseEvent.ToolName;
        var toolInputJson = toolUseEvent.ToolInputJson ?? "{}";
        var toolUseId = toolUseEvent.ToolUseId;

        var collectedEvents = new List<RuntimeEvent>();

        // 1. Execute the tool (this builds the real ToolExecutionRequest internally with correct approval/destructive metadata)
        var envelope = await toolExecutor.ExecuteAsync(toolName, toolInputJson, context, cancellationToken);

        // 2. Publish ToolStartedEvent using the real request from the executor (has correct approval scope, destructive flag, etc.)
        var startedEvent = new ToolStartedEvent(
            EventId: $"event-{Guid.NewGuid():N}",
            SessionId: context.SessionId,
            TurnId: context.TurnId,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Request: envelope.Request);

        await eventPublisher.PublishAsync(startedEvent, cancellationToken: cancellationToken);
        collectedEvents.Add(startedEvent);

        // 3. Publish ToolCompletedEvent
        var completedEvent = new ToolCompletedEvent(
            EventId: $"event-{Guid.NewGuid():N}",
            SessionId: context.SessionId,
            TurnId: context.TurnId,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Result: envelope.Result);

        await eventPublisher.PublishAsync(completedEvent, cancellationToken: cancellationToken);
        collectedEvents.Add(completedEvent);

        // 4. Convert ToolResult to ContentBlock
        ContentBlock resultBlock;
        if (envelope.Result.Succeeded)
        {
            resultBlock = new ContentBlock(
                ContentBlockKind.ToolResult,
                envelope.Result.Output,
                toolUseId,
                null,
                null,
                null);
        }
        else
        {
            resultBlock = new ContentBlock(
                ContentBlockKind.ToolResult,
                envelope.Result.ErrorMessage ?? "Tool execution failed",
                toolUseId,
                null,
                null,
                true);
        }

        // 5. Return
        return (resultBlock, envelope.Result, collectedEvents);
    }
}
