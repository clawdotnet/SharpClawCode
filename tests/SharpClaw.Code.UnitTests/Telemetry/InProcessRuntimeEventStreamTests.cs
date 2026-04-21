using FluentAssertions;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Services;

namespace SharpClaw.Code.UnitTests.Telemetry;

/// <summary>
/// Verifies the in-process runtime event stream keeps a bounded recent-event buffer.
/// </summary>
public sealed class InProcessRuntimeEventStreamTests
{
    [Fact]
    public async Task PublishAsync_should_trim_recent_envelopes_to_capacity()
    {
        var stream = new InProcessRuntimeEventStream(Options.Create(new TelemetryOptions
        {
            RuntimeEventRingBufferCapacity = 64,
        }));

        for (var i = 0; i < 80; i++)
        {
            await stream.PublishAsync(CreateEnvelope($"evt-{i}"), CancellationToken.None);
        }

        var snapshot = stream.GetRecentEnvelopesSnapshot();
        snapshot.Should().HaveCount(64);
        snapshot.Select(item => item.Event.EventId).Should().Equal(Enumerable.Range(16, 64).Select(index => $"evt-{index}"));
    }

    private static RuntimeEventEnvelope CreateEnvelope(string eventId)
        => new(
            EventType: nameof(UsageUpdatedEvent),
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Event: new UsageUpdatedEvent(
                EventId: eventId,
                SessionId: "session-1",
                TurnId: "turn-1",
                OccurredAtUtc: DateTimeOffset.UtcNow,
                Usage: new UsageSnapshot(1, 1, 0, 2, null)));
}
