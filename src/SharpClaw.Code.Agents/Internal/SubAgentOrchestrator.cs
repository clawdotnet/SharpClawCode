using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Agents;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Agents.Internal;

/// <summary>
/// Executes delegated subagent tasks as bounded read-only child runs.
/// </summary>
public sealed class SubAgentOrchestrator(
    IServiceProvider serviceProvider,
    IToolExecutor toolExecutor,
    ILogger<SubAgentOrchestrator> logger) : ISubAgentOrchestrator
{
    /// <inheritdoc />
    public async Task<SubAgentBatchExecutionResult> ExecuteAsync(
        SubAgentBatchRequest request,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        if (request.Tasks is not { Length: > 0 })
        {
            throw new InvalidOperationException("The subagent request must include at least one task.");
        }

        if (request.Tasks.Length > SubAgentToolContract.MaxTasks)
        {
            throw new InvalidOperationException($"The subagent request exceeds the limit of {SubAgentToolContract.MaxTasks} tasks.");
        }

        var runs = request.Tasks
            .Select((task, index) => ExecuteSingleAsync(task, index, context, cancellationToken))
            .ToArray();
        var completedRuns = await Task.WhenAll(runs).ConfigureAwait(false);

        var taskResults = completedRuns.Select(static run => run.TaskResult).ToArray();
        var events = completedRuns.SelectMany(static run => run.Events).ToArray();
        var result = new SubAgentBatchResult(
            Tasks: taskResults,
            CompletedCount: taskResults.Count(static task => task.Succeeded),
            FailedCount: taskResults.Count(static task => !task.Succeeded));

        return new SubAgentBatchExecutionResult(result, events);
    }

    private async Task<SingleTaskExecutionResult> ExecuteSingleAsync(
        SubAgentTaskRequest task,
        int index,
        ToolExecutionContext parentContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        var goal = task.Goal?.Trim();
        var expectedOutput = task.ExpectedOutput?.Trim();
        if (string.IsNullOrWhiteSpace(goal) || string.IsNullOrWhiteSpace(expectedOutput))
        {
            throw new InvalidOperationException("Each subagent task requires both goal and expectedOutput.");
        }

        var taskId = $"subtask-{index + 1:D2}-{Guid.NewGuid():N}";
        var delegatedTask = new DelegatedTaskContract(
            taskId,
            goal,
            expectedOutput,
            NormalizeConstraints(task.Constraints));

        try
        {
            var subAgentWorker = serviceProvider.GetRequiredService<SubAgentWorker>();
            var result = await subAgentWorker.RunAsync(
                new AgentRunContext(
                    SessionId: parentContext.SessionId,
                    TurnId: parentContext.TurnId,
                    Prompt: goal,
                    WorkingDirectory: parentContext.WorkingDirectory,
                    Model: string.IsNullOrWhiteSpace(parentContext.Model) ? "default" : parentContext.Model!,
                    PermissionMode: PermissionMode.ReadOnly,
                    OutputFormat: OutputFormat.Text,
                    ToolExecutor: toolExecutor,
                    Metadata: BuildChildMetadata(parentContext),
                    ParentAgentId: parentContext.AgentId,
                    DelegatedTask: delegatedTask,
                    PrimaryMode: PrimaryMode.Plan,
                    ToolMutationRecorder: null,
                    ConversationHistory: null,
                    IsInteractive: false,
                    ApprovalSettings: ApprovalSettings.Empty),
                cancellationToken).ConfigureAwait(false);

            return new SingleTaskExecutionResult(
                new SubAgentTaskResult(
                    TaskId: taskId,
                    Goal: goal,
                    ExpectedOutput: expectedOutput,
                    Succeeded: true,
                    Output: string.IsNullOrWhiteSpace(result.Output) ? "(no output)" : result.Output.Trim(),
                    ErrorMessage: null,
                    AgentId: result.AgentId),
                result.Events ?? []);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Delegated subagent task {TaskId} failed for session {SessionId}, turn {TurnId}.",
                taskId,
                parentContext.SessionId,
                parentContext.TurnId);

            return new SingleTaskExecutionResult(
                new SubAgentTaskResult(
                    TaskId: taskId,
                    Goal: goal,
                    ExpectedOutput: expectedOutput,
                    Succeeded: false,
                    Output: null,
                    ErrorMessage: exception.Message,
                    AgentId: SubAgentWorker.SubAgentId),
                []);
        }
    }

    private static string[] NormalizeConstraints(string[]? constraints)
        => constraints?
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray()
           ?? [];

    private static Dictionary<string, string> BuildChildMetadata(ToolExecutionContext parentContext)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        if (parentContext.Metadata is not null
            && parentContext.Metadata.TryGetValue("provider", out var provider)
            && !string.IsNullOrWhiteSpace(provider))
        {
            metadata["provider"] = provider;
        }

        metadata[SharpClawWorkflowMetadataKeys.AgentAllowedToolsJson] = JsonSerializer.Serialize(
            SubAgentToolContract.AllowedReadOnlyTools,
            ProtocolJsonContext.Default.StringArray);
        return metadata;
    }

    private sealed record SingleTaskExecutionResult(
        SubAgentTaskResult TaskResult,
        IReadOnlyList<RuntimeEvent> Events);
}
