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
internal sealed class ToolCallDispatcher(
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
        var toolName = toolUseEvent.ToolName ?? string.Empty;
        var toolInputJson = toolUseEvent.ToolInputJson ?? "{}";
        var toolUseId = toolUseEvent.ToolUseId ?? string.Empty;

        var collectedEvents = new List<RuntimeEvent>();

        // Build a minimal ToolExecutionRequest for the ToolStartedEvent
        var requestId = $"event-{Guid.NewGuid():N}";
        var startRequest = new Protocol.Models.ToolExecutionRequest(
            Id: requestId,
            SessionId: context.SessionId,
            TurnId: context.TurnId,
            ToolName: toolName,
            ArgumentsJson: toolInputJson,
            ApprovalScope: Protocol.Enums.ApprovalScope.ToolExecution,
            WorkingDirectory: context.WorkingDirectory,
            RequiresApproval: false,
            IsDestructive: false);

        // 1. Publish ToolStartedEvent
        var startedEvent = new ToolStartedEvent(
            EventId: $"event-{Guid.NewGuid():N}",
            SessionId: context.SessionId,
            TurnId: context.TurnId,
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Request: startRequest);

        await eventPublisher.PublishAsync(startedEvent, cancellationToken: cancellationToken);
        collectedEvents.Add(startedEvent);

        // 2. Execute the tool
        var envelope = await toolExecutor.ExecuteAsync(toolName, toolInputJson, context, cancellationToken);

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
