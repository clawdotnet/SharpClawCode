using System.Collections.Concurrent;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Telemetry.Metrics;

namespace SharpClaw.Code.Telemetry.Services;

/// <summary>
/// Converts runtime events into normalized usage metering records and metrics.
/// </summary>
public sealed class UsageMeteringService(IUsageMeteringStore store) : IUsageMeteringService, IRuntimeEventSink
{
    private readonly ConcurrentDictionary<string, ToolExecutionStart> toolStarts = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> providerStarts = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<UsageMeteringSummaryReport> GetSummaryAsync(string workspaceRoot, UsageMeteringQuery query, CancellationToken cancellationToken)
        => store.GetSummaryAsync(workspaceRoot, query, cancellationToken);

    /// <inheritdoc />
    public Task<UsageMeteringDetailReport> GetDetailAsync(
        string workspaceRoot,
        UsageMeteringQuery query,
        int limit,
        CancellationToken cancellationToken)
        => store.GetDetailAsync(workspaceRoot, query, limit, cancellationToken);

    /// <inheritdoc />
    public async Task PublishAsync(RuntimeEventEnvelope envelope, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(envelope.WorkspacePath))
        {
            return;
        }

        UsageMeteringRecord? record = envelope.Event switch
        {
            ToolStartedEvent toolStarted => RememberToolStart(toolStarted),
            ProviderStartedEvent providerStarted => RememberProviderStart(providerStarted),
            ProviderCompletedEvent providerCompleted => CreateProviderUsageRecord(envelope, providerCompleted),
            UsageUpdatedEvent usageUpdated => CreateUsageSnapshotRecord(envelope, usageUpdated),
            ToolCompletedEvent toolCompleted => CreateToolExecutionRecord(envelope, toolCompleted),
            TurnCompletedEvent turnCompleted => CreateTurnExecutionRecord(envelope, turnCompleted),
            SessionCreatedEvent sessionCreated => CreateSessionLifecycleRecord(envelope, sessionCreated, "created"),
            SessionForkedEvent sessionForked => CreateSessionLifecycleRecord(envelope, sessionForked, $"forked:{sessionForked.ParentSessionId}"),
            SessionStateChangedEvent sessionStateChanged => CreateSessionLifecycleRecord(envelope, sessionStateChanged, $"state:{sessionStateChanged.CurrentState}"),
            _ => null
        };

        if (record is null)
        {
            return;
        }

        await store.AppendAsync(envelope.WorkspacePath, record, cancellationToken).ConfigureAwait(false);
        RecordMetrics(record);
    }

    private UsageMeteringRecord? RememberToolStart(ToolStartedEvent startedEvent)
    {
        toolStarts[startedEvent.Request.Id] = new ToolExecutionStart(
            startedEvent.OccurredAtUtc,
            startedEvent.Request.ApprovalScope);
        return null;
    }

    private UsageMeteringRecord? RememberProviderStart(ProviderStartedEvent startedEvent)
    {
        var queue = providerStarts.GetOrAdd(CreateProviderKey(startedEvent), static _ => new ConcurrentQueue<DateTimeOffset>());
        queue.Enqueue(startedEvent.OccurredAtUtc);
        return null;
    }

    private static UsageMeteringRecord CreateUsageSnapshotRecord(RuntimeEventEnvelope envelope, UsageUpdatedEvent usageUpdated)
        => new(
            Id: usageUpdated.EventId,
            Kind: UsageMeteringRecordKind.UsageSnapshot,
            OccurredAtUtc: usageUpdated.OccurredAtUtc,
            TenantId: envelope.TenantId,
            HostId: envelope.HostId,
            WorkspaceRoot: envelope.WorkspacePath,
            SessionId: usageUpdated.SessionId,
            TurnId: usageUpdated.TurnId,
            Usage: usageUpdated.Usage,
            Detail: "usage-updated");

    private UsageMeteringRecord CreateProviderUsageRecord(RuntimeEventEnvelope envelope, ProviderCompletedEvent completedEvent)
    {
        long? durationMilliseconds = null;
        if (providerStarts.TryGetValue(CreateProviderKey(completedEvent), out var queue)
            && queue.TryDequeue(out var startedAtUtc))
        {
            durationMilliseconds = Math.Max(0L, (long)(completedEvent.OccurredAtUtc - startedAtUtc).TotalMilliseconds);
        }

        return new UsageMeteringRecord(
            Id: completedEvent.EventId,
            Kind: UsageMeteringRecordKind.ProviderUsage,
            OccurredAtUtc: completedEvent.OccurredAtUtc,
            TenantId: envelope.TenantId,
            HostId: envelope.HostId,
            WorkspaceRoot: envelope.WorkspacePath,
            SessionId: completedEvent.SessionId,
            TurnId: completedEvent.TurnId,
            ProviderName: completedEvent.ProviderName,
            Model: completedEvent.Model,
            DurationMilliseconds: durationMilliseconds,
            Usage: completedEvent.Usage,
            Detail: completedEvent.Kind);
    }

    private UsageMeteringRecord CreateToolExecutionRecord(RuntimeEventEnvelope envelope, ToolCompletedEvent completedEvent)
    {
        var durationMilliseconds = completedEvent.Result.DurationMilliseconds;
        SharpClaw.Code.Protocol.Enums.ApprovalScope? approvalScope = null;
        if (toolStarts.TryRemove(completedEvent.Result.RequestId, out var started))
        {
            approvalScope = started.ApprovalScope;
            if (durationMilliseconds is null)
            {
                durationMilliseconds = Math.Max(0L, (long)(completedEvent.OccurredAtUtc - started.OccurredAtUtc).TotalMilliseconds);
            }
        }

        return new UsageMeteringRecord(
            Id: completedEvent.EventId,
            Kind: UsageMeteringRecordKind.ToolExecution,
            OccurredAtUtc: completedEvent.OccurredAtUtc,
            TenantId: envelope.TenantId,
            HostId: envelope.HostId,
            WorkspaceRoot: envelope.WorkspacePath,
            SessionId: completedEvent.SessionId,
            TurnId: completedEvent.TurnId,
            ToolName: completedEvent.Result.ToolName,
            ApprovalScope: approvalScope,
            Succeeded: completedEvent.Result.Succeeded,
            DurationMilliseconds: durationMilliseconds,
            Detail: completedEvent.Result.ErrorMessage);
    }

    private static UsageMeteringRecord CreateTurnExecutionRecord(RuntimeEventEnvelope envelope, TurnCompletedEvent completedEvent)
    {
        long? durationMilliseconds = null;
        if (completedEvent.Turn.CompletedAtUtc is { } completedAtUtc)
        {
            durationMilliseconds = Math.Max(0L, (long)(completedAtUtc - completedEvent.Turn.StartedAtUtc).TotalMilliseconds);
        }

        return new UsageMeteringRecord(
            Id: completedEvent.EventId,
            Kind: UsageMeteringRecordKind.TurnExecution,
            OccurredAtUtc: completedEvent.OccurredAtUtc,
            TenantId: envelope.TenantId,
            HostId: envelope.HostId,
            WorkspaceRoot: envelope.WorkspacePath,
            SessionId: completedEvent.SessionId,
            TurnId: completedEvent.TurnId,
            Succeeded: completedEvent.Succeeded,
            DurationMilliseconds: durationMilliseconds,
            Usage: completedEvent.Turn.Usage,
            Detail: completedEvent.Summary);
    }

    private static UsageMeteringRecord CreateSessionLifecycleRecord(RuntimeEventEnvelope envelope, RuntimeEvent runtimeEvent, string detail)
        => new(
            Id: runtimeEvent.EventId,
            Kind: UsageMeteringRecordKind.SessionLifecycle,
            OccurredAtUtc: runtimeEvent.OccurredAtUtc,
            TenantId: envelope.TenantId,
            HostId: envelope.HostId,
            WorkspaceRoot: envelope.WorkspacePath,
            SessionId: runtimeEvent.SessionId,
            TurnId: runtimeEvent.TurnId,
            Detail: detail);

    private static void RecordMetrics(UsageMeteringRecord record)
    {
        if (record.Kind == UsageMeteringRecordKind.ProviderUsage)
        {
            if (record.Usage is { } usage)
            {
                SharpClawMeterSource.InputTokens.Add(usage.InputTokens);
                SharpClawMeterSource.OutputTokens.Add(usage.OutputTokens);
            }

            if (record.DurationMilliseconds is { } providerDuration)
            {
                SharpClawMeterSource.ProviderDuration.Record(providerDuration);
            }

            return;
        }

        if (record.Kind == UsageMeteringRecordKind.ToolExecution)
        {
            SharpClawMeterSource.ToolInvocations.Add(1);
            if (record.DurationMilliseconds is { } toolDuration)
            {
                SharpClawMeterSource.ToolDuration.Record(toolDuration);
            }

            return;
        }

        if (record.Kind == UsageMeteringRecordKind.TurnExecution
            && record.DurationMilliseconds is { } turnDuration)
        {
            SharpClawMeterSource.TurnDuration.Record(turnDuration);
        }
    }

    private static string CreateProviderKey(ProviderStartedEvent startedEvent)
        => string.Join("::", startedEvent.SessionId, startedEvent.TurnId ?? string.Empty, startedEvent.ProviderName, startedEvent.Model);

    private static string CreateProviderKey(ProviderCompletedEvent completedEvent)
        => string.Join("::", completedEvent.SessionId, completedEvent.TurnId ?? string.Empty, completedEvent.ProviderName, completedEvent.Model);

    private sealed record ToolExecutionStart(
        DateTimeOffset OccurredAtUtc,
        SharpClaw.Code.Protocol.Enums.ApprovalScope ApprovalScope);
}
