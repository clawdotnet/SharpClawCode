using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Runtime.Workflow;

/// <summary>
/// Reconstructs workspace usage, cost, and execution statistics from durable session state.
/// </summary>
public sealed class WorkspaceInsightsService(
    ISessionStore sessionStore,
    IEventStore eventStore,
    IWorkspaceSessionAttachmentStore attachmentStore,
    IUsageTracker usageTracker,
    IPathService pathService,
    ITodoService todoService) : IWorkspaceInsightsService
{
    /// <inheritdoc />
    public async Task<WorkspaceUsageReport> BuildUsageReportAsync(
        string workspaceRoot,
        string? currentSessionId,
        CancellationToken cancellationToken)
    {
        var state = await BuildStateAsync(workspaceRoot, currentSessionId, cancellationToken).ConfigureAwait(false);
        var workspaceTotal = state.Sessions
            .Select(static item => item.Usage)
            .Aggregate(new UsageSnapshot(0, 0, 0, 0, null), MergeUsage);

        return new WorkspaceUsageReport(
            state.WorkspaceRoot,
            state.CurrentSessionId,
            state.AttachedSessionId,
            workspaceTotal,
            state.Sessions);
    }

    /// <inheritdoc />
    public async Task<WorkspaceCostReport> BuildCostReportAsync(
        string workspaceRoot,
        string? currentSessionId,
        CancellationToken cancellationToken)
    {
        var state = await BuildStateAsync(workspaceRoot, currentSessionId, cancellationToken).ConfigureAwait(false);
        var sessionCosts = state.Sessions
            .Select(item => new SessionCostReport(
                item.SessionId,
                item.Title,
                item.IsAttached,
                item.IsCurrent,
                item.Usage.EstimatedCostUsd))
            .ToArray();

        decimal? totalCost = sessionCosts
            .Select(static item => item.EstimatedCostUsd)
            .Where(static item => item.HasValue)
            .Aggregate(
                seed: (decimal?)null,
                func: static (current, next) => (current ?? 0m) + next!.Value);

        return new WorkspaceCostReport(
            state.WorkspaceRoot,
            state.CurrentSessionId,
            state.AttachedSessionId,
            totalCost,
            sessionCosts);
    }

    /// <inheritdoc />
    public async Task<WorkspaceStatsReport> BuildStatsReportAsync(
        string workspaceRoot,
        string? currentSessionId,
        CancellationToken cancellationToken)
    {
        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        var sessions = await sessionStore.ListAllAsync(normalizedWorkspaceRoot, cancellationToken).ConfigureAwait(false);
        var attachedSessionId = await attachmentStore.GetAttachedSessionIdAsync(normalizedWorkspaceRoot, cancellationToken).ConfigureAwait(false);
        var effectiveCurrentSessionId = ResolveCurrentSessionId(currentSessionId, attachedSessionId, sessions);

        var turnStartedCount = 0;
        var turnCompletedCount = 0;
        var toolExecutionCount = 0;
        var providerRequestCount = 0;
        var sharedSessionCount = 0;
        var activeTodoCount = 0;

        foreach (var session in sessions)
        {
            if (session.Metadata?.ContainsKey(SharpClawWorkflowMetadataKeys.ShareId) == true)
            {
                sharedSessionCount++;
            }

            activeTodoCount += ReadSessionTodos(session).Count(static item => item.Status != TodoStatus.Done);

            var events = await eventStore.ReadAllAsync(normalizedWorkspaceRoot, session.Id, cancellationToken).ConfigureAwait(false);
            turnStartedCount += events.OfType<TurnStartedEvent>().Count();
            turnCompletedCount += events.OfType<TurnCompletedEvent>().Count();
            toolExecutionCount += events.OfType<ToolCompletedEvent>().Count();
            providerRequestCount += events.OfType<ProviderStartedEvent>().Count();
        }

        var workspaceTodos = await todoService.GetSnapshotAsync(normalizedWorkspaceRoot, null, cancellationToken).ConfigureAwait(false);
        activeTodoCount += workspaceTodos.WorkspaceTodos.Count(static item => item.Status != TodoStatus.Done);

        return new WorkspaceStatsReport(
            normalizedWorkspaceRoot,
            effectiveCurrentSessionId,
            attachedSessionId,
            sessions.Count,
            turnStartedCount,
            turnCompletedCount,
            toolExecutionCount,
            providerRequestCount,
            sharedSessionCount,
            activeTodoCount);
    }

    private async Task<WorkspaceUsageState> BuildStateAsync(
        string workspaceRoot,
        string? currentSessionId,
        CancellationToken cancellationToken)
    {
        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        var sessions = await sessionStore.ListAllAsync(normalizedWorkspaceRoot, cancellationToken).ConfigureAwait(false);
        var attachedSessionId = await attachmentStore.GetAttachedSessionIdAsync(normalizedWorkspaceRoot, cancellationToken).ConfigureAwait(false);
        var effectiveCurrentSessionId = ResolveCurrentSessionId(currentSessionId, attachedSessionId, sessions);

        var reports = new List<SessionUsageReport>(sessions.Count);
        foreach (var session in sessions)
        {
            var usage = await GetUsageForSessionAsync(normalizedWorkspaceRoot, session.Id, cancellationToken).ConfigureAwait(false);
            reports.Add(
                new SessionUsageReport(
                    session.Id,
                    session.Title,
                    string.Equals(session.Id, attachedSessionId, StringComparison.Ordinal),
                    string.Equals(session.Id, effectiveCurrentSessionId, StringComparison.Ordinal),
                    usage));
        }

        return new WorkspaceUsageState(
            normalizedWorkspaceRoot,
            effectiveCurrentSessionId,
            attachedSessionId,
            reports);
    }

    private async Task<UsageSnapshot> GetUsageForSessionAsync(string workspaceRoot, string sessionId, CancellationToken cancellationToken)
    {
        var inMemory = usageTracker.TryGetCumulative(sessionId);
        if (inMemory is not null)
        {
            return inMemory;
        }

        var events = await eventStore.ReadAllAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);
        return events.OfType<UsageUpdatedEvent>()
            .Select(static item => item.Usage)
            .Aggregate(new UsageSnapshot(0, 0, 0, 0, null), MergeUsage);
    }

    private static IReadOnlyList<TodoItem> ReadSessionTodos(ConversationSession session)
    {
        if (session.Metadata is null
            || !session.Metadata.TryGetValue(SharpClawWorkflowMetadataKeys.SessionTodosJson, out var todosJson)
            || string.IsNullOrWhiteSpace(todosJson))
        {
            return [];
        }

        return System.Text.Json.JsonSerializer.Deserialize(todosJson, Protocol.Serialization.ProtocolJsonContext.Default.ListTodoItem) ?? [];
    }

    private static string? ResolveCurrentSessionId(
        string? currentSessionId,
        string? attachedSessionId,
        IReadOnlyList<ConversationSession> sessions)
    {
        if (!string.IsNullOrWhiteSpace(currentSessionId))
        {
            return currentSessionId;
        }

        if (!string.IsNullOrWhiteSpace(attachedSessionId))
        {
            return attachedSessionId;
        }

        return sessions.FirstOrDefault()?.Id;
    }

    private static UsageSnapshot MergeUsage(UsageSnapshot previous, UsageSnapshot delta)
    {
        decimal? cost = (previous.EstimatedCostUsd, delta.EstimatedCostUsd) switch
        {
            (decimal a, decimal b) => a + b,
            (decimal a, null) => a,
            (null, decimal b) => b,
            _ => null
        };

        return new UsageSnapshot(
            previous.InputTokens + delta.InputTokens,
            previous.OutputTokens + delta.OutputTokens,
            previous.CachedInputTokens + delta.CachedInputTokens,
            previous.TotalTokens + delta.TotalTokens,
            cost);
    }

    private sealed record WorkspaceUsageState(
        string WorkspaceRoot,
        string? CurrentSessionId,
        string? AttachedSessionId,
        IReadOnlyList<SessionUsageReport> Sessions);
}
