using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Runtime.CompilerServices;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Providers.Resilience;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.UnitTests.Providers;

/// <summary>
/// Verifies retry, rate-limit, and circuit-breaker behavior of <see cref="ResilientProviderDecorator"/>.
/// </summary>
public sealed class ResilienceTests
{
    private static readonly ProviderRequest FakeRequest = new(
        Id: "req-001",
        SessionId: "session-1",
        TurnId: "turn-1",
        ProviderName: "test",
        Model: "test-model",
        Prompt: "hello",
        SystemPrompt: null,
        OutputFormat: OutputFormat.Text,
        Temperature: null,
        Metadata: null);

    private static ResilientProviderDecorator BuildDecorator(
        IModelProvider inner,
        ProviderResilienceOptions? options = null)
    {
        var opts = options ?? new ProviderResilienceOptions
        {
            MaxRetries = 2,
            InitialRetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero,
            RequestTimeout = TimeSpan.FromSeconds(30),
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerBreakDuration = TimeSpan.FromSeconds(30),
        };

        return new ResilientProviderDecorator(inner, opts, NullLogger.Instance);
    }

    [Fact]
    public async Task Retries_on_transient_failure_then_succeeds()
    {
        // Arrange: fail twice with HttpRequestException, succeed on third attempt
        var fakeHandle = new ProviderStreamHandle(FakeRequest, AsyncEnumerable.Empty<ProviderEvent>());
        var mock = new CountingMockProvider();
        mock.Behaviors.Enqueue(() => throw new HttpRequestException("transient 1"));
        mock.Behaviors.Enqueue(() => throw new HttpRequestException("transient 2"));
        mock.Behaviors.Enqueue(() => Task.FromResult(fakeHandle));

        var decorator = BuildDecorator(mock);

        // Act
        var result = await decorator.StartStreamAsync(FakeRequest, CancellationToken.None);
        await DrainAsync(result.Events);

        // Assert
        result.Request.Should().Be(FakeRequest);
        mock.CallCount.Should().Be(3);
    }

    [Fact]
    public async Task Does_not_retry_non_transient_failures()
    {
        // Arrange: throw ArgumentException immediately
        var mock = new CountingMockProvider();
        mock.Behaviors.Enqueue(() => throw new ArgumentException("bad argument"));

        var decorator = BuildDecorator(mock);

        // Act
        Func<Task> act = async () =>
        {
            var stream = await decorator.StartStreamAsync(FakeRequest, CancellationToken.None);
            await DrainAsync(stream.Events);
        };

        // Assert: propagates immediately after a single call
        await act.Should().ThrowAsync<ArgumentException>();
        mock.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Circuit_breaker_opens_after_threshold()
    {
        // Arrange: always fail; threshold = 3, maxRetries = 2 (so each outer call = 1 attempt since no retries remain after threshold)
        var opts = new ProviderResilienceOptions
        {
            MaxRetries = 0,           // No retries — each call is a single attempt
            InitialRetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero,
            RequestTimeout = TimeSpan.FromSeconds(30),
            CircuitBreakerFailureThreshold = 3,
            CircuitBreakerBreakDuration = TimeSpan.FromHours(1), // Will not auto-reset
        };

        var mock = new CountingMockProvider();
        // Queue more than threshold failures
        for (var i = 0; i < 10; i++)
        {
            mock.Behaviors.Enqueue(() => throw new HttpRequestException("always fails"));
        }

        var decorator = BuildDecorator(mock, opts);

        // Exhaust threshold with 3 calls (each fails)
        for (var i = 0; i < 3; i++)
        {
            await FluentActions
                .Awaiting(async () =>
                {
                    var stream = await decorator.StartStreamAsync(FakeRequest, CancellationToken.None);
                    await DrainAsync(stream.Events);
                })
                .Should().ThrowAsync<ProviderExecutionException>();
        }

        var callsBeforeCircuitOpen = mock.CallCount;

        // Next call should be rejected by the open circuit without reaching inner provider
        await FluentActions
            .Awaiting(() => decorator.StartStreamAsync(FakeRequest, CancellationToken.None))
            .Should().ThrowAsync<ProviderExecutionException>()
            .WithMessage("*Circuit breaker*");

        mock.CallCount.Should().Be(callsBeforeCircuitOpen, "circuit breaker must not forward the call to the inner provider");
    }

    [Fact]
    public async Task Circuit_breaker_allows_probe_after_break_duration()
    {
        // Arrange: circuit opens immediately after threshold, then break duration is 0 so probe is allowed right away
        var opts = new ProviderResilienceOptions
        {
            MaxRetries = 0,
            InitialRetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero,
            RequestTimeout = TimeSpan.FromSeconds(30),
            CircuitBreakerFailureThreshold = 1,  // Opens after 1 failure
            CircuitBreakerBreakDuration = TimeSpan.Zero,  // Immediately allow probe
        };

        var fakeHandle = new ProviderStreamHandle(FakeRequest, AsyncEnumerable.Empty<ProviderEvent>());
        var mock = new CountingMockProvider();

        // First call fails — opens circuit
        mock.Behaviors.Enqueue(() => throw new HttpRequestException("initial failure"));
        // Probe call succeeds
        mock.Behaviors.Enqueue(() => Task.FromResult(fakeHandle));

        var decorator = BuildDecorator(mock, opts);

        // First call: should fail and open the circuit
        await FluentActions.Awaiting(async () =>
        {
            var stream = await decorator.StartStreamAsync(FakeRequest, CancellationToken.None);
            await DrainAsync(stream.Events);
        }).Should().ThrowAsync<ProviderExecutionException>();

        mock.CallCount.Should().Be(1);

        // Second call: break duration has elapsed (it's zero), so probe should reach inner provider
        var result = await decorator.StartStreamAsync(FakeRequest, CancellationToken.None);
        await DrainAsync(result.Events);
        result.Request.Should().Be(FakeRequest);
        mock.CallCount.Should().Be(2, "probe attempt must reach the inner provider");
    }

    [Fact]
    public async Task Retries_when_stream_fails_before_first_event()
    {
        var mock = new CountingMockProvider();
        mock.Behaviors.Enqueue(() => Task.FromResult(new ProviderStreamHandle(FakeRequest, FailBeforeFirstEvent())));
        mock.Behaviors.Enqueue(() => Task.FromResult(new ProviderStreamHandle(FakeRequest, SingleEvent())));
        var decorator = BuildDecorator(mock);

        var stream = await decorator.StartStreamAsync(FakeRequest, CancellationToken.None);
        var events = await CollectAsync(stream.Events);

        events.Should().ContainSingle();
        mock.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Does_not_retry_after_stream_has_emitted_an_event()
    {
        var mock = new CountingMockProvider();
        mock.Behaviors.Enqueue(() => Task.FromResult(new ProviderStreamHandle(FakeRequest, FailAfterFirstEvent())));
        var decorator = BuildDecorator(mock);

        var stream = await decorator.StartStreamAsync(FakeRequest, CancellationToken.None);
        Func<Task> act = () => DrainAsync(stream.Events);

        await act.Should().ThrowAsync<ProviderExecutionException>();
        mock.CallCount.Should().Be(1, "replaying a partial stream would duplicate output");
    }

    [Fact]
    public async Task Request_timeout_covers_async_enumeration()
    {
        var options = new ProviderResilienceOptions
        {
            MaxRetries = 0,
            InitialRetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero,
            RequestTimeout = TimeSpan.FromMilliseconds(25),
            CircuitBreakerFailureThreshold = 5,
            CircuitBreakerBreakDuration = TimeSpan.FromSeconds(30),
        };
        var mock = new CountingMockProvider();
        mock.Behaviors.Enqueue(() => Task.FromResult(new ProviderStreamHandle(FakeRequest, NeverCompletes())));
        var decorator = BuildDecorator(mock, options);

        var stream = await decorator.StartStreamAsync(FakeRequest, CancellationToken.None);
        Func<Task> act = () => DrainAsync(stream.Events);

        await act.Should().ThrowAsync<ProviderExecutionException>();
        mock.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task Retries_operation_canceled_failure_when_caller_is_not_canceled()
    {
        var fakeHandle = new ProviderStreamHandle(FakeRequest, AsyncEnumerable.Empty<ProviderEvent>());
        var mock = new CountingMockProvider();
        mock.Behaviors.Enqueue(() => throw new OperationCanceledException("provider timeout"));
        mock.Behaviors.Enqueue(() => Task.FromResult(fakeHandle));
        var decorator = BuildDecorator(mock);

        var stream = await decorator.StartStreamAsync(FakeRequest, CancellationToken.None);
        await DrainAsync(stream.Events);

        mock.CallCount.Should().Be(2);
    }

    [Fact]
    public async Task Circuit_breaker_stops_retries_in_current_request()
    {
        var options = new ProviderResilienceOptions
        {
            MaxRetries = 3,
            InitialRetryDelay = TimeSpan.Zero,
            MaxRetryDelay = TimeSpan.Zero,
            RequestTimeout = TimeSpan.FromSeconds(30),
            CircuitBreakerFailureThreshold = 2,
            CircuitBreakerBreakDuration = TimeSpan.FromHours(1),
        };
        var mock = new CountingMockProvider();
        for (var i = 0; i < 4; i++)
        {
            mock.Behaviors.Enqueue(() => throw new IOException("provider unavailable"));
        }
        var decorator = BuildDecorator(mock, options);

        var act = async () =>
        {
            var stream = await decorator.StartStreamAsync(FakeRequest, CancellationToken.None);
            await DrainAsync(stream.Events);
        };

        var exception = await act.Should().ThrowAsync<ProviderExecutionException>();
        exception.Which.Message.Should().Contain("after 2 attempt(s)");
        mock.CallCount.Should().Be(2);
    }

    private static async Task DrainAsync(IAsyncEnumerable<ProviderEvent> events)
        => _ = await CollectAsync(events);

    private static async Task<IReadOnlyList<ProviderEvent>> CollectAsync(IAsyncEnumerable<ProviderEvent> events)
    {
        var result = new List<ProviderEvent>();
        await foreach (var providerEvent in events)
        {
            result.Add(providerEvent);
        }

        return result;
    }

    private static async IAsyncEnumerable<ProviderEvent> FailBeforeFirstEvent()
    {
        await Task.Yield();
        throw new IOException("stream failed");
#pragma warning disable CS0162
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<ProviderEvent> FailAfterFirstEvent()
    {
        yield return CreateEvent("delta");
        await Task.Yield();
        throw new IOException("stream failed after output");
    }

    private static async IAsyncEnumerable<ProviderEvent> SingleEvent()
    {
        await Task.Yield();
        yield return CreateEvent("completed");
    }

    private static async IAsyncEnumerable<ProviderEvent> NeverCompletes(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        yield break;
    }

    private static ProviderEvent CreateEvent(string kind)
        => new(
            Id: Guid.NewGuid().ToString("N"),
            RequestId: FakeRequest.Id,
            Kind: kind,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            Content: null,
            IsTerminal: string.Equals(kind, "completed", StringComparison.Ordinal),
            Usage: null);

    // -----------------------------------------------------------------------
    // Test double
    // -----------------------------------------------------------------------

    private sealed class CountingMockProvider : IModelProvider
    {
        public int CallCount { get; private set; }
        public Queue<Func<Task<ProviderStreamHandle>>> Behaviors { get; } = new();

        public string ProviderName => "test";

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken ct)
            => Task.FromResult(new AuthStatus(null, false, "test", null, null, null));

        public async Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken ct)
        {
            CallCount++;
            return await Behaviors.Dequeue()();
        }
    }
}
