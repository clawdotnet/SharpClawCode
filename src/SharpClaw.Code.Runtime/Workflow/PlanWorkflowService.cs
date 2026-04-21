using System.Text;
using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Runtime.Workflow;

/// <summary>
/// Parses structured plan-mode output and synchronizes planning-owned session todos.
/// </summary>
public sealed class PlanWorkflowService(
    ITodoService todoService,
    ISessionStore sessionStore,
    IFileSystem fileSystem,
    IPathService pathService,
    IRuntimeStoragePathResolver storagePathResolver,
    ISystemClock systemClock) : IPlanWorkflowService
{
    private const string PlanningOwnerAgentId = "deep-planning";

    /// <inheritdoc />
    public string BuildPromptInstructions()
        => """
            Plan mode is active.

            Think deeply about constraints, sequencing, and risk. Respond with JSON only. Do not include prose before or after the JSON.

            Required JSON shape:
            {
              "summary": "One-paragraph plan summary",
              "assumptions": ["Key assumption"],
              "risks": ["Important delivery or implementation risk"],
              "nextAction": "Highest-leverage next step",
              "tasks": [
                {
                  "id": "PLAN-001",
                  "title": "Actionable task title",
                  "status": "open",
                  "details": "Optional implementation detail",
                  "doneCriteria": "Optional completion criteria"
                }
              ]
            }

            Keep tasks concrete and implementation-oriented. Use status values: open, inProgress, blocked, or done.
            """;

    /// <inheritdoc />
    public async Task<PlanExecutionResult> MaterializeAsync(
        string workspacePath,
        string sessionId,
        string userPrompt,
        string modelOutput,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(userPrompt);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelOutput);

        var payload = DeserializePayload(modelOutput);
        ValidatePayload(payload);

        var sync = await todoService
            .SyncManagedSessionTodosAsync(
                workspacePath,
                sessionId,
                PlanningOwnerAgentId,
                payload.Tasks.Select(task => new ManagedTodoSeed(task.Id.Trim(), FormatTodoTitle(task), task.Status)).ToArray(),
                cancellationToken,
                assumeSessionLockHeld: true)
            .ConfigureAwait(false);

        await PersistSessionSummaryAsync(workspacePath, sessionId, payload, cancellationToken, assumeSessionLockHeld: true).ConfigureAwait(false);

        return new PlanExecutionResult(
            payload.Summary.Trim(),
            payload.Assumptions.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()).ToArray(),
            payload.Risks.Where(static value => !string.IsNullOrWhiteSpace(value)).Select(static value => value.Trim()).ToArray(),
            payload.NextAction.Trim(),
            payload.Tasks.Select(NormalizeTask).ToArray(),
            sync);
    }

    /// <inheritdoc />
    public string RenderCompletionMessage(PlanExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var builder = new StringBuilder();
        builder.AppendLine("Deep plan ready.")
            .Append("Summary: ").AppendLine(result.Summary)
            .Append("Next: ").AppendLine(result.NextAction)
            .Append("Todo sync: ")
            .Append(result.TodoSync.AddedCount)
            .Append(" added, ")
            .Append(result.TodoSync.UpdatedCount)
            .Append(" updated, ")
            .Append(result.TodoSync.RemovedCount)
            .AppendLine(" removed.");

        if (result.Assumptions.Count > 0)
        {
            builder.AppendLine().AppendLine("Assumptions:");
            foreach (var assumption in result.Assumptions)
            {
                builder.Append("- ").AppendLine(assumption);
            }
        }

        if (result.Risks.Count > 0)
        {
            builder.AppendLine().AppendLine("Risks:");
            foreach (var risk in result.Risks)
            {
                builder.Append("- ").AppendLine(risk);
            }
        }

        if (result.Tasks.Count > 0)
        {
            builder.AppendLine().AppendLine("Tasks:");
            foreach (var task in result.Tasks)
            {
                builder.Append("- [")
                    .Append(task.Status)
                    .Append("] ")
                    .Append(task.Id)
                    .Append(": ")
                    .AppendLine(task.Title);
            }
        }

        return builder.ToString().TrimEnd();
    }

    private PlanGenerationPayload DeserializePayload(string modelOutput)
    {
        var candidates = new List<string> { modelOutput.Trim() };
        if (TryStripCodeFence(modelOutput) is { } stripped)
        {
            candidates.Add(stripped);
        }

        if (TryExtractJsonObject(modelOutput) is { } extracted)
        {
            candidates.Add(extracted);
        }

        foreach (var candidate in candidates.Where(static value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
        {
            try
            {
                var payload = JsonSerializer.Deserialize(candidate, ProtocolJsonContext.Default.PlanGenerationPayload);
                if (payload is not null)
                {
                    return payload;
                }
            }
            catch (JsonException)
            {
                // Try the next candidate.
            }
        }

        throw new InvalidOperationException("Plan mode expected a valid structured JSON response containing summary, risks, nextAction, and tasks.");
    }

    private static void ValidatePayload(PlanGenerationPayload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);

        if (string.IsNullOrWhiteSpace(payload.Summary)
            || string.IsNullOrWhiteSpace(payload.NextAction)
            || payload.Assumptions is null
            || payload.Risks is null
            || payload.Tasks is null)
        {
            throw new InvalidOperationException("Plan mode output was incomplete. Summary, nextAction, and task collection are required.");
        }

        if (payload.Tasks.Any(static task =>
                string.IsNullOrWhiteSpace(task.Id)
                || string.IsNullOrWhiteSpace(task.Title)))
        {
            throw new InvalidOperationException("Plan mode tasks must contain non-empty ids and titles.");
        }
    }

    private async Task PersistSessionSummaryAsync(
        string workspacePath,
        string sessionId,
        PlanGenerationPayload payload,
        CancellationToken cancellationToken,
        bool assumeSessionLockHeld)
    {
        var normalizedWorkspacePath = pathService.GetFullPath(workspacePath);
        if (!assumeSessionLockHeld)
        {
            await using var gate = await fileSystem
                .AcquireExclusiveFileLockAsync(storagePathResolver.GetSessionTurnLockPath(normalizedWorkspacePath, sessionId), cancellationToken)
                .ConfigureAwait(false);
            await PersistSessionSummaryCoreAsync(normalizedWorkspacePath, sessionId, payload, cancellationToken).ConfigureAwait(false);
            return;
        }

        await PersistSessionSummaryCoreAsync(normalizedWorkspacePath, sessionId, payload, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistSessionSummaryCoreAsync(
        string normalizedWorkspacePath,
        string sessionId,
        PlanGenerationPayload payload,
        CancellationToken cancellationToken)
    {
        var session = await sessionStore.GetByIdAsync(normalizedWorkspacePath, sessionId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Session '{sessionId}' was not found.");
        var metadata = session.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
        metadata[SharpClawWorkflowMetadataKeys.DeepPlanningSummary] = payload.Summary.Trim();
        metadata[SharpClawWorkflowMetadataKeys.DeepPlanningNextAction] = payload.NextAction.Trim();

        var updated = session with
        {
            UpdatedAtUtc = systemClock.UtcNow,
            Metadata = metadata,
        };

        await sessionStore.SaveAsync(normalizedWorkspacePath, updated, cancellationToken).ConfigureAwait(false);
    }

    private static PlanTaskItem NormalizeTask(PlanTaskItem task)
        => task with
        {
            Id = task.Id.Trim(),
            Title = task.Title.Trim(),
            Details = string.IsNullOrWhiteSpace(task.Details) ? null : task.Details.Trim(),
            DoneCriteria = string.IsNullOrWhiteSpace(task.DoneCriteria) ? null : task.DoneCriteria.Trim(),
        };

    private static string FormatTodoTitle(PlanTaskItem task)
        => $"{task.Id.Trim()}: {task.Title.Trim()}";

    private static string? TryStripCodeFence(string value)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("```", StringComparison.Ordinal))
        {
            return null;
        }

        var firstNewLine = trimmed.IndexOf('\n');
        if (firstNewLine < 0)
        {
            return null;
        }

        return trimmed[(firstNewLine + 1)..^3].Trim();
    }

    private static string? TryExtractJsonObject(string value)
    {
        var start = value.IndexOf('{');
        var end = value.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        return value[start..(end + 1)].Trim();
    }
}
