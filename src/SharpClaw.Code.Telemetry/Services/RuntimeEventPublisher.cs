using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Telemetry.Services;

/// <summary>
/// Default <see cref="IRuntimeEventPublisher" />: ring buffer, optional <see cref="IRuntimeEventPersistence" />, and usage updates from <see cref="UsageUpdatedEvent" />.
/// </summary>
public sealed class RuntimeEventPublisher : IRuntimeEventPublisher
{
    private readonly TelemetryOptions telemetryOptions;
    private readonly IUsageTracker usageTracker;
    private readonly ILogger<RuntimeEventPublisher> logger;
    private readonly IRuntimeEventPersistence? persistence;
    private readonly IRuntimeHostContextAccessor? hostContextAccessor;
    private readonly IRuntimeEventSink[] sinks;
    private readonly object bufferLock = new();
    private readonly List<RuntimeEvent> buffer = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="RuntimeEventPublisher" /> class.
    /// </summary>
    /// <param name="telemetryOptionsAccessor">Buffer and behavior options.</param>
    /// <param name="usageTracker">Session usage aggregation.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="persistence">Optional persistence bridge; omitted in tests or CLI slices without sessions.</param>
    /// <param name="hostContextAccessor">Optional ambient embedded-host context.</param>
    /// <param name="sinks">Optional external runtime event sinks.</param>
    public RuntimeEventPublisher(
        IOptions<TelemetryOptions> telemetryOptionsAccessor,
        IUsageTracker usageTracker,
        ILogger<RuntimeEventPublisher>? logger = null,
        IRuntimeEventPersistence? persistence = null,
        IRuntimeHostContextAccessor? hostContextAccessor = null,
        IEnumerable<IRuntimeEventSink>? sinks = null)
    {
        telemetryOptions = telemetryOptionsAccessor.Value;
        this.usageTracker = usageTracker;
        this.logger = logger ?? NullLogger<RuntimeEventPublisher>.Instance;
        this.persistence = persistence;
        this.hostContextAccessor = hostContextAccessor;
        this.sinks = sinks?.ToArray() ?? [];
    }

    /// <inheritdoc />
    public async ValueTask PublishAsync(
        RuntimeEvent runtimeEvent,
        RuntimeEventPublishOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtimeEvent);
        var routing = options ?? new RuntimeEventPublishOptions();
        AppendToBuffer(runtimeEvent);
        if (runtimeEvent is UsageUpdatedEvent usageEvt)
        {
            usageTracker.ApplyUsage(usageEvt.SessionId, usageEvt.Usage);
        }

        if (routing.ShouldPersist && persistence is not null)
        {
            if (string.IsNullOrWhiteSpace(routing.WorkspacePath) || string.IsNullOrWhiteSpace(routing.SessionId))
            {
                this.logger.LogWarning(
                    "Runtime event {EventId} requested persistence but WorkspacePath or SessionId is null.",
                    runtimeEvent.EventId);
            }
            else
            {
                try
                {
                    await persistence
                        .PersistAsync(routing.WorkspacePath, routing.SessionId, runtimeEvent, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    this.logger.LogError(
                        exception,
                        "Failed to persist runtime event {EventId} for workspace {WorkspacePath} session {SessionId}.",
                        runtimeEvent.EventId,
                        routing.WorkspacePath,
                        routing.SessionId);
                    if (routing.ThrowIfPersistenceFails)
                    {
                        throw;
                    }
                }
            }
        }
        else if (routing.ShouldPersist && persistence is null)
        {
            this.logger.LogDebug(
                "Runtime event {EventId} requested session persistence but no IRuntimeEventPersistence is registered.",
                runtimeEvent.EventId);
        }

        if (sinks.Length > 0)
        {
            var hostContext = routing.HostContext ?? hostContextAccessor?.Current;
            var envelope = new RuntimeEventEnvelope(
                EventType: runtimeEvent.GetType().Name,
                OccurredAtUtc: runtimeEvent.OccurredAtUtc,
                Event: runtimeEvent,
                WorkspacePath: routing.WorkspacePath,
                SessionId: routing.SessionId ?? runtimeEvent.SessionId,
                TenantId: hostContext?.TenantId,
                HostId: hostContext?.HostId);
            foreach (var sink in sinks)
            {
                try
                {
                    await sink.PublishAsync(envelope, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(
                        exception,
                        "Runtime event {EventId} failed to publish to sink {SinkType}.",
                        runtimeEvent.EventId,
                        sink.GetType().Name);
                }
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<RuntimeEvent> GetRecentEventsSnapshot()
    {
        lock (bufferLock)
        {
            return buffer.ToArray();
        }
    }

    private void AppendToBuffer(RuntimeEvent runtimeEvent)
    {
        var capacity = Math.Max(64, telemetryOptions.RuntimeEventRingBufferCapacity);
        lock (bufferLock)
        {
            buffer.Add(runtimeEvent);
            var removeCount = buffer.Count - capacity;
            if (removeCount > 0)
            {
                buffer.RemoveRange(0, removeCount);
            }
        }
    }
}
