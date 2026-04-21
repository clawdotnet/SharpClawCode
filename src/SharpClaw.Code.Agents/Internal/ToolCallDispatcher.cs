using System.Text.Json;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Agents.Internal;

/// <summary>
/// Dispatches tool-use requests from provider events through the permission-aware tool executor
/// and returns content blocks for the provider conversation.
/// </summary>
public sealed class ToolCallDispatcher(
    IToolExecutor toolExecutor,
    ISubAgentOrchestrator subAgentOrchestrator)
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

        if (string.Equals(toolName, SubAgentToolContract.ToolName, StringComparison.OrdinalIgnoreCase))
        {
            return await DispatchSubAgentsAsync(toolUseEvent, toolUseId, toolInputJson, context, cancellationToken).ConfigureAwait(false);
        }

        // Execute the tool — ToolExecutor already publishes ToolStartedEvent and ToolCompletedEvent
        // via IRuntimeEventPublisher, so we do NOT re-publish here to avoid duplicates.
        var envelope = await toolExecutor.ExecuteAsync(toolName, toolInputJson, context, cancellationToken);

        // Convert ToolResult to ContentBlock
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

        // Events are already published by ToolExecutor; return empty list to avoid duplicates.
        return (resultBlock, envelope.Result, []);
    }

    private async Task<(ContentBlock ResultBlock, ToolResult ToolResult, List<RuntimeEvent> Events)> DispatchSubAgentsAsync(
        ProviderEvent toolUseEvent,
        string toolUseId,
        string toolInputJson,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        SubAgentBatchExecutionResult execution;
        try
        {
            var request = JsonSerializer.Deserialize(toolInputJson, ProtocolJsonContext.Default.SubAgentBatchRequest)
                ?? throw new InvalidOperationException("Subagent tool input was empty.");
            execution = await subAgentOrchestrator.ExecuteAsync(request, context, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            return CreateSubAgentFailure(toolUseEvent, toolUseId, $"Invalid subagent request JSON: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            return CreateSubAgentFailure(toolUseEvent, toolUseId, exception.Message);
        }

        var textOutput = FormatSubAgentOutput(execution.Result);
        var payloadJson = JsonSerializer.Serialize(execution.Result, ProtocolJsonContext.Default.SubAgentBatchResult);
        var succeeded = execution.Result.CompletedCount > 0;
        var toolResult = new ToolResult(
            RequestId: toolUseEvent.RequestId,
            ToolName: SubAgentToolContract.ToolName,
            Succeeded: succeeded,
            OutputFormat: Protocol.Enums.OutputFormat.Text,
            Output: textOutput,
            ErrorMessage: succeeded ? null : "All delegated subagent tasks failed.",
            ExitCode: succeeded ? 0 : 1,
            DurationMilliseconds: null,
            StructuredOutputJson: payloadJson);

        var resultBlock = new ContentBlock(
            ContentBlockKind.ToolResult,
            succeeded ? textOutput : toolResult.ErrorMessage,
            toolUseId,
            null,
            null,
            succeeded ? null : true);

        return (resultBlock, toolResult, [.. execution.Events]);
    }

    private static (ContentBlock ResultBlock, ToolResult ToolResult, List<RuntimeEvent> Events) CreateSubAgentFailure(
        ProviderEvent toolUseEvent,
        string toolUseId,
        string message)
    {
        var result = new ToolResult(
            RequestId: toolUseEvent.RequestId,
            ToolName: SubAgentToolContract.ToolName,
            Succeeded: false,
            OutputFormat: Protocol.Enums.OutputFormat.Text,
            Output: null,
            ErrorMessage: message,
            ExitCode: 1,
            DurationMilliseconds: null,
            StructuredOutputJson: null);
        var block = new ContentBlock(ContentBlockKind.ToolResult, message, toolUseId, null, null, true);
        return (block, result, []);
    }

    private static string FormatSubAgentOutput(SubAgentBatchResult result)
    {
        var lines = new List<string>
        {
            $"Delegated tasks completed: {result.CompletedCount} succeeded, {result.FailedCount} failed."
        };

        foreach (var task in result.Tasks)
        {
            lines.Add(string.Empty);
            lines.Add($"Task {task.TaskId}: {task.Goal}");
            lines.Add($"Expected output: {task.ExpectedOutput}");
            lines.Add(task.Succeeded
                ? $"Result: {task.Output}"
                : $"Error: {task.ErrorMessage}");
        }

        return string.Join(Environment.NewLine, lines);
    }
}
