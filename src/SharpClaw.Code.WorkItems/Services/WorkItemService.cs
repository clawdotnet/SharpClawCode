using System.Text;
using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.WorkItems.Abstractions;

namespace SharpClaw.Code.WorkItems.Services;

/// <summary>
/// Imports work items into session metadata and exports session summaries.
/// </summary>
public sealed class WorkItemService(
    IWorkItemRegistry registry,
    ISessionStore sessionStore,
    IEventStore eventStore,
    IRuntimeEventPublisher eventPublisher,
    IPathService pathService,
    ISystemClock systemClock,
    ISessionUpdateCoordinator? sessionUpdateCoordinator = null) : IWorkItemService
{
    private Task<T> RunScopedAsync<T>(string workspace, string sessionId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        => sessionUpdateCoordinator is null
            ? action(cancellationToken)
            : sessionUpdateCoordinator.RunAsync(workspace, sessionId, action, cancellationToken);

    /// <inheritdoc />
    public async Task<WorkItemImportResult> ImportAsync(WorkItemImportRequest request, CancellationToken cancellationToken)
    {
        var workspace = pathService.GetFullPath(request.WorkspacePath);
        var provider = registry.Resolve(request.Provider, request.IdOrUrl)
            ?? throw new InvalidOperationException($"No work-item provider can import '{request.IdOrUrl}'.");
        var workItem = await provider.ImportAsync(request with { WorkspacePath = workspace }, cancellationToken).ConfigureAwait(false);
        var (resolved, created) = await ResolveSessionAsync(workspace, request.SessionId, workItem, cancellationToken).ConfigureAwait(false);

        // Read-modify-write under the session update scope so a concurrently running turn cannot overwrite
        // the imported work item with its stale copy of the session, and concurrent imports serialize.
        var updated = await RunScopedAsync(workspace, resolved.Id, async ct =>
        {
            var session = await sessionStore.GetByIdAsync(workspace, resolved.Id, ct).ConfigureAwait(false) ?? resolved;
            var metadata = session.Metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(session.Metadata, StringComparer.Ordinal);
            metadata[SharpClawWorkflowMetadataKeys.WorkItemJson] = JsonSerializer.Serialize(workItem, ProtocolJsonContext.Default.WorkItem);
            metadata[SharpClawWorkflowMetadataKeys.WorkItemGoal] = workItem.Title;

            var next = session with
            {
                Title = workItem.Title,
                Metadata = metadata,
                UpdatedAtUtc = systemClock.UtcNow,
            };
            await sessionStore.SaveAsync(workspace, next, ct).ConfigureAwait(false);
            return next;
        }, cancellationToken).ConfigureAwait(false);
        await PublishAsync(
            workspace,
            updated.Id,
            new WorkItemImportedEvent(CreateIdentifier("event"), updated.Id, null, systemClock.UtcNow, workItem),
            cancellationToken).ConfigureAwait(false);
        return new WorkItemImportResult(workItem, updated.Id, created);
    }

    /// <inheritdoc />
    public async Task<WorkItemSummaryExport> ExportSummaryAsync(WorkItemExportRequest request, string workspaceRoot, CancellationToken cancellationToken)
    {
        var workspace = pathService.GetFullPath(workspaceRoot);
        var session = string.IsNullOrWhiteSpace(request.SessionId)
            ? await sessionStore.GetLatestAsync(workspace, cancellationToken).ConfigureAwait(false)
            : await sessionStore.GetByIdAsync(workspace, request.SessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(request.SessionId)
                ? "No session found to export."
                : $"Session '{request.SessionId}' was not found.");
        }

        var workItem = ReadWorkItem(session);
        var provider = string.IsNullOrWhiteSpace(request.Provider)
            ? workItem?.Provider ?? string.Empty
            : request.Provider;
        var events = await eventStore.ReadAllAsync(workspace, session.Id, cancellationToken).ConfigureAwait(false);
        var markdown = RenderMarkdown(session, workItem, events);
        var content = string.Equals(request.ExportFormat, "json", StringComparison.OrdinalIgnoreCase)
            ? JsonSerializer.Serialize(new Dictionary<string, string>
            {
                ["sessionId"] = session.Id,
                ["title"] = workItem?.Title ?? session.Title,
                ["markdown"] = markdown
            }, ProtocolJsonContext.Default.DictionaryStringString)
            : markdown;
        var export = new WorkItemSummaryExport(session.Id, request.ExportFormat, content, workItem);
        await PublishAsync(
            workspace,
            session.Id,
            new WorkItemSummaryExportedEvent(CreateIdentifier("event"), session.Id, null, systemClock.UtcNow, provider, request.ExportFormat),
            cancellationToken).ConfigureAwait(false);
        return export;
    }

    private async Task<(ConversationSession Session, bool Created)> ResolveSessionAsync(string workspace, string? sessionId, WorkItem workItem, CancellationToken cancellationToken)
    {
        ConversationSession? session = null;
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            session = await sessionStore.GetByIdAsync(workspace, sessionId, cancellationToken).ConfigureAwait(false);
            if (session is null)
            {
                throw new InvalidOperationException($"Session '{sessionId}' was not found.");
            }

            return (session, false);
        }

        session ??= await sessionStore.GetLatestAsync(workspace, cancellationToken).ConfigureAwait(false);
        if (session is not null)
        {
            return (session, false);
        }

        var created = new ConversationSession(
            CreateIdentifier("session"),
            workItem.Title,
            SessionLifecycleState.Active,
            PermissionMode.WorkspaceWrite,
            OutputFormat.Text,
            workspace,
            workspace,
            systemClock.UtcNow,
            systemClock.UtcNow,
            null,
            null,
            new Dictionary<string, string>());
        await sessionStore.SaveAsync(workspace, created, cancellationToken).ConfigureAwait(false);
        await PublishAsync(workspace, created.Id, new SessionCreatedEvent(CreateIdentifier("event"), created.Id, null, systemClock.UtcNow, created), cancellationToken).ConfigureAwait(false);
        return (created, true);
    }

    private static WorkItem? ReadWorkItem(ConversationSession session)
    {
        if (session.Metadata is null || !session.Metadata.TryGetValue(SharpClawWorkflowMetadataKeys.WorkItemJson, out var json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.WorkItem);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string RenderMarkdown(ConversationSession session, WorkItem? workItem, IReadOnlyList<RuntimeEvent> events)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Work Summary: {workItem?.Title ?? session.Title}");
        sb.AppendLine();
        if (workItem is not null)
        {
            sb.AppendLine($"- Provider: {workItem.Provider}");
            sb.AppendLine($"- Work item: {workItem.Id}");
            if (!string.IsNullOrWhiteSpace(workItem.Url))
            {
                sb.AppendLine($"- URL: {workItem.Url}");
            }

            sb.AppendLine();
        }

        var completedTurns = events.OfType<TurnCompletedEvent>().ToArray();
        if (completedTurns.Length > 0)
        {
            sb.AppendLine("## Completed Turns");
            foreach (var turn in completedTurns)
            {
                sb.AppendLine($"- Turn {turn.Turn.SequenceNumber}: {turn.Summary ?? Truncate(turn.Turn.Output)}");
            }
        }
        else
        {
            sb.AppendLine("No completed turns were found for this session.");
        }

        return sb.ToString();
    }

    private ValueTask PublishAsync(string workspace, string sessionId, RuntimeEvent runtimeEvent, CancellationToken cancellationToken)
        => eventPublisher.PublishAsync(runtimeEvent, new RuntimeEventPublishOptions(workspace, sessionId, PersistToSessionStore: true), cancellationToken);

    private static string CreateIdentifier(string prefix) => $"{prefix}_{Guid.NewGuid():N}";

    private static string Truncate(string? value, int max = 160)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "completed";
        }

        return value.Length <= max ? value : value[..max];
    }
}
