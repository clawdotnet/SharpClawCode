using Microsoft.Extensions.Configuration;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Operational;
using SharpClaw.Code.Runtime.Mutations;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Runtime.Diagnostics;

/// <summary>
/// Coordinates modular checks into versioned operational reports.
/// </summary>
public sealed class OperationalDiagnosticsCoordinator(
    IEnumerable<IOperationalCheck> checks,
    ISystemClock systemClock,
    IPathService pathService,
    ISessionStore sessionStore,
    IMcpRegistry mcpRegistry,
    IPluginManager pluginManager,
    IEventStore eventStore,
    IConfiguration? configuration = null) : IOperationalDiagnosticsCoordinator
{
    private readonly IOperationalCheck[] orderedChecks = checks.ToArray();

    /// <inheritdoc />
    public async Task<DoctorReport> RunDoctorAsync(OperationalDiagnosticsInput input, CancellationToken cancellationToken)
    {
        var workspacePath = pathService.GetFullPath(string.IsNullOrWhiteSpace(input.WorkingDirectory)
            ? pathService.GetCurrentDirectory()
            : input.WorkingDirectory);
        var context = new OperationalDiagnosticsContext(workspacePath, input.Model, input.PermissionMode);
        var results = new List<OperationalCheckItem>();
        foreach (var check in orderedChecks)
        {
            results.Add(await check.ExecuteAsync(context, cancellationToken).ConfigureAwait(false));
        }

        IReadOnlyDictionary<string, string>? configSample = null;
        if (configuration is not null)
        {
            configSample = configuration.AsEnumerable()
                .Where(pair => pair.Key.StartsWith("SharpClaw", StringComparison.OrdinalIgnoreCase))
                .Take(24)
                .ToDictionary(pair => pair.Key, _ => "present", StringComparer.OrdinalIgnoreCase);
        }

        var overall = Reduce(results);
        return new DoctorReport(
            SchemaVersion: "1.0",
            GeneratedAtUtc: systemClock.UtcNow,
            OverallStatus: overall,
            WorkspaceRoot: workspacePath,
            Checks: results,
            ConfigurationKeysSample: configSample);
    }

    /// <inheritdoc />
    public async Task<RuntimeStatusReport> BuildStatusReportAsync(OperationalDiagnosticsInput input, CancellationToken cancellationToken)
    {
        var workspacePath = pathService.GetFullPath(string.IsNullOrWhiteSpace(input.WorkingDirectory)
            ? pathService.GetCurrentDirectory()
            : input.WorkingDirectory);
        var context = new OperationalDiagnosticsContext(workspacePath, input.Model, input.PermissionMode);
        var quickChecks = new List<OperationalCheckItem>();
        foreach (var id in new[] { "workspace.access", "session.store", "mcp.registry", "plugins.registry" })
        {
            var check = orderedChecks.FirstOrDefault(c => c.Id == id);
            if (check is not null)
            {
                quickChecks.Add(await check.ExecuteAsync(context, cancellationToken).ConfigureAwait(false));
            }
        }

        var latest = await sessionStore.GetLatestAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        var mcpServers = await mcpRegistry.ListAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        var plugins = await pluginManager.ListAsync(workspacePath, cancellationToken).ConfigureAwait(false);

        var primaryMode = ResolvePrimaryModeForStatus(input, latest);

        return new RuntimeStatusReport(
            SchemaVersion: "1.0",
            GeneratedAtUtc: systemClock.UtcNow,
            WorkspaceRoot: workspacePath,
            RuntimeState: "ready",
            SelectedModel: string.IsNullOrWhiteSpace(input.Model) ? "default" : input.Model,
            PermissionMode: input.PermissionMode,
            PrimaryMode: primaryMode,
            LatestSessionId: latest?.Id,
            LatestSessionState: latest?.State,
            McpServerCount: mcpServers.Count,
            McpReadyCount: mcpServers.Count(s => s.Status.State == McpLifecycleState.Ready),
            McpFaultedCount: mcpServers.Count(s => s.Status.State == McpLifecycleState.Faulted),
            PluginInstalledCount: plugins.Count,
            PluginEnabledCount: plugins.Count(p => p.State == PluginLifecycleState.Enabled),
            Checks: quickChecks);
    }

    /// <inheritdoc />
    public async Task<SessionInspectionReport?> InspectSessionAsync(
        string? sessionIdOrNullForLatest,
        OperationalDiagnosticsInput input,
        CancellationToken cancellationToken)
    {
        var workspacePath = pathService.GetFullPath(string.IsNullOrWhiteSpace(input.WorkingDirectory)
            ? pathService.GetCurrentDirectory()
            : input.WorkingDirectory);
        var session = string.IsNullOrWhiteSpace(sessionIdOrNullForLatest)
            ? await sessionStore.GetLatestAsync(workspacePath, cancellationToken).ConfigureAwait(false)
            : await sessionStore.GetByIdAsync(workspacePath, sessionIdOrNullForLatest, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return null;
        }

        var events = await eventStore.ReadAllAsync(workspacePath, session.Id, cancellationToken).ConfigureAwait(false);
        var shortTail = events.Count == 0
            ? null
            : string.Join(", ", events.TakeLast(Math.Min(5, events.Count)).Select(SummarizeEvent));

        return new SessionInspectionReport(
            "1.0",
            workspacePath,
            session,
            events.Count,
            shortTail,
            CheckpointMutationCoordinator.ToSnapshot(session));
    }

    private static string SummarizeEvent(RuntimeEvent @event)
        => @event switch
        {
            ToolCompletedEvent => "toolCompleted",
            ToolStartedEvent => "toolStarted",
            TurnStartedEvent => "turnStarted",
            TurnCompletedEvent => "turnCompleted",
            SessionCreatedEvent => "sessionCreated",
            SessionForkedEvent => "sessionForked",
            ProviderStartedEvent => "providerStarted",
            ProviderDeltaEvent => "providerDelta",
            ProviderCompletedEvent => "providerCompleted",
            _ => @event.GetType().Name
        };

    private static PrimaryMode ResolvePrimaryModeForStatus(OperationalDiagnosticsInput input, ConversationSession? latest)
    {
        if (input.PrimaryMode is { } fromInput)
        {
            return fromInput;
        }

        if (latest?.Metadata is not null
            && latest.Metadata.TryGetValue(SharpClawWorkflowMetadataKeys.PrimaryMode, out var stored)
            && Enum.TryParse<PrimaryMode>(stored, ignoreCase: true, out var parsed))
        {
            return parsed;
        }

        return PrimaryMode.Build;
    }

    private static OperationalCheckStatus Reduce(IReadOnlyList<OperationalCheckItem> items)
    {
        if (items.Any(i => i.Status == OperationalCheckStatus.Error))
        {
            return OperationalCheckStatus.Error;
        }

        if (items.Any(i => i.Status == OperationalCheckStatus.Warn))
        {
            return OperationalCheckStatus.Warn;
        }

        return OperationalCheckStatus.Ok;
    }
}
