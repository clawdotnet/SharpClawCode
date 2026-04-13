using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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

        // Assert
        result.Should().BeSameAs(fakeHandle);
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
        Func<Task> act = () => decorator.StartStreamAsync(FakeRequest, CancellationToken.None);

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
                .Awaiting(() => decorator.StartStreamAsync(FakeRequest, CancellationToken.None))
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
        await FluentActions
            .Awaiting(() => decorator.StartStreamAsync(FakeRequest, CancellationToken.None))
            .Should().ThrowAsync<ProviderExecutionException>();

        mock.CallCount.Should().Be(1);

        // Second call: break duration has elapsed (it's zero), so probe should reach inner provider
        var result = await decorator.StartStreamAsync(FakeRequest, CancellationToken.None);
        result.Should().BeSameAs(fakeHandle);
        mock.CallCount.Should().Be(2, "probe attempt must reach the inner provider");
    }

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
