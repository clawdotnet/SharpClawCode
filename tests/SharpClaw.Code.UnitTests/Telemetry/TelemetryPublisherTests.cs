using FluentAssertions;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Export;
using SharpClaw.Code.Telemetry.Services;

namespace SharpClaw.Code.UnitTests.Telemetry;

/// <summary>
/// Verifies runtime event publishing, usage aggregation, and JSON trace export.
/// </summary>
public sealed class TelemetryPublisherTests
{
    /// <summary>
    /// Ensures published events appear in the ring buffer and usage totals merge.
    /// </summary>
    [Fact]
    public async Task RuntimeEventPublisher_should_buffer_events_and_merge_usage()
    {
        var usageTracker = new UsageTracker();
        var publisher = new RuntimeEventPublisher(
            Options.Create(new TelemetryOptions { RuntimeEventRingBufferCapacity = 100 }),
            usageTracker);

        var usage = new UsageSnapshot(1, 2, 0, 3, 0.5m);
        await publisher.PublishAsync(
            new UsageUpdatedEvent(
                EventId: "e1",
                SessionId: "s1",
                TurnId: "t1",
                OccurredAtUtc: DateTimeOffset.UtcNow,
                Usage: usage),
            new RuntimeEventPublishOptions("/tmp/ws", "s1", PersistToSessionStore: false),
            CancellationToken.None);

        publisher.GetRecentEventsSnapshot().Should().ContainSingle();
        usageTracker.TryGetCumulative("s1").Should().BeEquivalentTo(usage);
    }

    /// <summary>
    /// Ensures <see cref="UsageTracker" /> accumulates per session.
    /// </summary>
    [Fact]
    public void UsageTracker_should_accumulate_token_counts()
    {
        var tracker = new UsageTracker();
        tracker.ApplyUsage("s1", new UsageSnapshot(1, 1, 0, 2, null));
        tracker.ApplyUsage("s1", new UsageSnapshot(2, 0, 1, 3, 1m));

        var total = tracker.TryGetCumulative("s1");
        total.Should().NotBeNull();
        total!.InputTokens.Should().Be(3);
        total.OutputTokens.Should().Be(1);
        total.CachedInputTokens.Should().Be(1);
        total.TotalTokens.Should().Be(5);
        total.EstimatedCostUsd.Should().Be(1m);
    }

    /// <summary>
    /// Ensures JSON trace export includes polymorphic discriminator.
    /// </summary>
    [Fact]
    public void JsonTraceExporter_should_emit_polymorphic_event_type()
    {
        var exporter = new JsonTraceExporter();
        IReadOnlyList<RuntimeEvent> events =
        [
            new ToolStartedEvent(
                EventId: "e1",
                SessionId: "s1",
                TurnId: "t1",
                OccurredAtUtc: DateTimeOffset.Parse("2026-04-06T12:00:00Z"),
                Request: new ToolExecutionRequest(
                    "tr1",
                    "s1",
                    "t1",
                    "read_file",
                    "{}",
                    SharpClaw.Code.Protocol.Enums.ApprovalScope.ToolExecution,
                    "/w",
                    false,
                    false))
        ];

        var json = exporter.SerializeEvents(events, writeIndented: false);
        json.Should().Contain("\"$eventType\":\"toolStarted\"");
    }

    /// <summary>
    /// Ensures one failing external sink does not break telemetry publishing for the runtime.
    /// </summary>
    [Fact]
    public async Task RuntimeEventPublisher_should_isolate_sink_failures()
    {
        var usageTracker = new UsageTracker();
        var recordingSink = new RecordingSink();
        var publisher = new RuntimeEventPublisher(
            Options.Create(new TelemetryOptions { RuntimeEventRingBufferCapacity = 16 }),
            usageTracker,
            sinks: [new ThrowingSink(), recordingSink]);

        await publisher.PublishAsync(
            new UsageUpdatedEvent(
                EventId: "e2",
                SessionId: "s1",
                TurnId: "t1",
                OccurredAtUtc: DateTimeOffset.UtcNow,
                Usage: new UsageSnapshot(2, 3, 0, 5, 0.1m)),
            new RuntimeEventPublishOptions("/tmp/ws", "s1", PersistToSessionStore: false),
            CancellationToken.None);

        recordingSink.Envelopes.Should().ContainSingle();
        usageTracker.TryGetCumulative("s1")!.TotalTokens.Should().Be(5);
    }

    private sealed class ThrowingSink : SharpClaw.Code.Telemetry.Abstractions.IRuntimeEventSink
    {
        public Task PublishAsync(RuntimeEventEnvelope envelope, CancellationToken cancellationToken)
            => throw new InvalidOperationException("sink failed");
    }

    private sealed class RecordingSink : SharpClaw.Code.Telemetry.Abstractions.IRuntimeEventSink
    {
        public List<RuntimeEventEnvelope> Envelopes { get; } = [];

        public Task PublishAsync(RuntimeEventEnvelope envelope, CancellationToken cancellationToken)
        {
            Envelopes.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
