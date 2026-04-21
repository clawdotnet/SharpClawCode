using System.Net;
using System.Net.Http;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Telemetry.Services;

namespace SharpClaw.Code.UnitTests.Telemetry;

/// <summary>
/// Verifies webhook runtime event delivery and retry behavior.
/// </summary>
public sealed class WebhookRuntimeEventSinkTests
{
    [Fact]
    public async Task PublishAsync_should_skip_delivery_when_no_webhooks_are_configured()
    {
        var handler = new SequenceMessageHandler(HttpStatusCode.OK);
        var sink = new WebhookRuntimeEventSink(
            Options.Create(new TelemetryOptions()),
            new HttpClient(handler),
            new RecordingDelayStrategy());

        await sink.PublishAsync(CreateEnvelope(), CancellationToken.None);

        handler.AttemptCount.Should().Be(0);
    }

    [Fact]
    public async Task PublishAsync_should_retry_after_a_failure_and_eventually_succeed()
    {
        var options = new TelemetryOptions
        {
            WebhookMaxAttempts = 3,
            WebhookInitialBackoffMilliseconds = 25,
        };
        options.EventWebhookUrls.Add("https://example.com/runtime-events");
        var handler = new SequenceMessageHandler(HttpStatusCode.InternalServerError, HttpStatusCode.OK);
        var delayStrategy = new RecordingDelayStrategy();
        var sink = new WebhookRuntimeEventSink(
            Options.Create(options),
            new HttpClient(handler),
            delayStrategy);

        await sink.PublishAsync(CreateEnvelope(), CancellationToken.None);

        handler.AttemptCount.Should().Be(2);
        delayStrategy.Delays.Should().Equal(TimeSpan.FromMilliseconds(25));
    }

    [Fact]
    public async Task PublishAsync_should_stop_after_the_configured_attempt_limit()
    {
        var options = new TelemetryOptions
        {
            WebhookMaxAttempts = 3,
            WebhookInitialBackoffMilliseconds = 10,
        };
        options.EventWebhookUrls.Add("https://example.com/runtime-events");
        var handler = new SequenceMessageHandler(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError);
        var delayStrategy = new RecordingDelayStrategy();
        var sink = new WebhookRuntimeEventSink(
            Options.Create(options),
            new HttpClient(handler),
            delayStrategy);

        await sink.PublishAsync(CreateEnvelope(), CancellationToken.None);

        handler.AttemptCount.Should().Be(3);
        delayStrategy.Delays.Should().Equal(
            TimeSpan.FromMilliseconds(10),
            TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task PublishAsync_should_apply_exponential_backoff_between_attempts()
    {
        var options = new TelemetryOptions
        {
            WebhookMaxAttempts = 4,
            WebhookInitialBackoffMilliseconds = 50,
        };
        options.EventWebhookUrls.Add("https://example.com/runtime-events");
        var handler = new SequenceMessageHandler(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK);
        var delayStrategy = new RecordingDelayStrategy();
        var sink = new WebhookRuntimeEventSink(
            Options.Create(options),
            new HttpClient(handler),
            delayStrategy);

        await sink.PublishAsync(CreateEnvelope(), CancellationToken.None);

        delayStrategy.Delays.Should().Equal(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(200));
    }

    [Fact]
    public async Task PublishAsync_should_propagate_cancellation_without_retrying()
    {
        var options = new TelemetryOptions
        {
            WebhookMaxAttempts = 3,
            WebhookInitialBackoffMilliseconds = 50,
        };
        options.EventWebhookUrls.Add("https://example.com/runtime-events");
        using var cancellationTokenSource = new CancellationTokenSource();
        await cancellationTokenSource.CancelAsync();
        var handler = new CancelingMessageHandler(cancellationTokenSource.Token);
        var delayStrategy = new RecordingDelayStrategy();
        var sink = new WebhookRuntimeEventSink(
            Options.Create(options),
            new HttpClient(handler),
            delayStrategy);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sink.PublishAsync(CreateEnvelope(), cancellationTokenSource.Token));

        handler.AttemptCount.Should().Be(1);
        delayStrategy.Delays.Should().BeEmpty();
    }

    private static RuntimeEventEnvelope CreateEnvelope()
        => new(
            EventType: nameof(UsageUpdatedEvent),
            OccurredAtUtc: DateTimeOffset.UtcNow,
            Event: new UsageUpdatedEvent(
                EventId: "evt-usage",
                SessionId: "session-1",
                TurnId: "turn-1",
                OccurredAtUtc: DateTimeOffset.UtcNow,
                Usage: new UsageSnapshot(1, 2, 0, 3, 0.01m)),
            WorkspacePath: "/workspace",
            SessionId: "session-1",
            TenantId: "tenant-a",
            HostId: "host-a");

    private sealed class RecordingDelayStrategy : IWebhookDelayStrategy
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }

    private sealed class SequenceMessageHandler(params HttpStatusCode[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> responses = new(responses);

        public int AttemptCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AttemptCount++;
            var statusCode = responses.Count == 0 ? HttpStatusCode.OK : responses.Dequeue();
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }
    }

    private sealed class CancelingMessageHandler(CancellationToken token) : HttpMessageHandler
    {
        public int AttemptCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AttemptCount++;
            return Task.FromCanceled<HttpResponseMessage>(token);
        }
    }
}
