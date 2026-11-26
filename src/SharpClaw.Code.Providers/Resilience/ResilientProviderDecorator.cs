using System.Net;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Resilience;

/// <summary>
/// Decorates an <see cref="IModelProvider"/> with full-stream timeouts, retry handling, and a circuit breaker.
/// </summary>
internal sealed class ResilientProviderDecorator : IModelProvider
{
    private readonly IModelProvider _inner;
    private readonly ProviderResilienceOptions _options;
    private readonly ILogger _logger;
    private readonly object _lock = new();
    private int _consecutiveFailures;
    private DateTimeOffset _circuitOpenedAt;
    private bool _circuitOpen;
    private DateTimeOffset? _halfOpenProbeStartedAt;

    public ResilientProviderDecorator(IModelProvider inner, ProviderResilienceOptions options, ILogger logger)
    {
        _inner = inner;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderName => _inner.ProviderName;

    /// <inheritdoc />
    public bool SupportsImageInput => _inner.SupportsImageInput;

    /// <inheritdoc />
    public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        => _inner.GetAuthStatusAsync(cancellationToken);

    /// <inheritdoc />
    public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
    {
        ThrowIfCircuitOpen(request);
        return Task.FromResult(new ProviderStreamHandle(request, ExecuteWithResilienceAsync(request, cancellationToken)));
    }

    private async IAsyncEnumerable<ProviderEvent> ExecuteWithResilienceAsync(
        ProviderRequest request,
        [EnumeratorCancellation] CancellationToken callerCancellationToken)
    {
        Exception? lastException = null;
        var attemptsMade = 0;

        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            attemptsMade = attempt + 1;
            callerCancellationToken.ThrowIfCancellationRequested();
            using var timeoutCts = new CancellationTokenSource(_options.RequestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerCancellationToken, timeoutCts.Token);
            var emittedEvent = false;
            Exception? attemptException = null;
            ProviderEvent? failedEvent = null;
            IAsyncEnumerator<ProviderEvent>? enumerator = null;

            try
            {
                var handle = await _inner.StartStreamAsync(request, linkedCts.Token).ConfigureAwait(false);
                enumerator = handle.Events.GetAsyncEnumerator(linkedCts.Token);
            }
            catch (Exception exception)
            {
                attemptException = exception;
            }

            if (enumerator is not null)
            {
                var streamCompleted = false;
                try
                {
                    while (true)
                    {
                        bool hasNext;
                        try
                        {
                            hasNext = await enumerator.MoveNextAsync().AsTask().WaitAsync(linkedCts.Token).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            attemptException = exception;
                            break;
                        }

                        if (!hasNext)
                        {
                            streamCompleted = true;
                            break;
                        }

                        var current = enumerator.Current;
                        if (current.IsTerminal && string.Equals(current.Kind, "failed", StringComparison.OrdinalIgnoreCase))
                        {
                            // SDK adapters convert HTTP errors into a terminal failed event rather than throwing,
                            // so a failed event is a failed attempt: it must count toward the circuit and be retried.
                            failedEvent = current;
                            attemptException = new ProviderStreamFailedEventException(current);
                            break;
                        }

                        emittedEvent = true;
                        yield return current;
                    }
                }
                finally
                {
                    try
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception) when (attemptException is not null)
                    {
                        _logger.LogDebug(exception, "Provider stream disposal failed after a stream error.");
                    }
                }

                if (streamCompleted)
                {
                    ResetCircuit();
                    yield break;
                }
            }

            if (attemptException is OperationCanceledException && callerCancellationToken.IsCancellationRequested)
            {
                throw attemptException;
            }

            if (attemptException is null)
            {
                throw new InvalidOperationException("Provider execution failed without an exception.");
            }

            if (failedEvent is not null && (emittedEvent || !ProviderStreamFailureClassifier.IsTransientFailedEvent(failedEvent)))
            {
                // Partial output was already streamed (cannot replay) or the failure is not retryable:
                // surface the original failed event so callers can classify it (auth, context overflow, ...).
                RecordFailureAndOpenCircuitIfNeeded();
                yield return failedEvent;
                yield break;
            }

            if (!IsTransient(attemptException))
            {
                RecordFailureAndOpenCircuitIfNeeded();
                _logger.LogError(
                    attemptException,
                    "Non-transient failure from provider {Provider} on attempt {Attempt}. Not retrying.",
                    ProviderName,
                    attempt + 1);
                throw attemptException;
            }

            lastException = attemptException;
            RecordFailureAndOpenCircuitIfNeeded();
            if (emittedEvent || attempt >= _options.MaxRetries || IsCircuitOpen())
            {
                if (failedEvent is not null)
                {
                    yield return failedEvent;
                    yield break;
                }

                break;
            }

            var delay = ComputeDelay(attempt, attemptException);
            _logger.LogWarning(
                attemptException,
                "Transient failure from provider {Provider} on attempt {Attempt}/{MaxAttempts}. Retrying in {Delay}ms.",
                ProviderName,
                attempt + 1,
                _options.MaxRetries + 1,
                delay.TotalMilliseconds);
            await Task.Delay(delay, callerCancellationToken).ConfigureAwait(false);
        }

        throw new ProviderExecutionException(
            ProviderName,
            request.Model,
            ProviderFailureKind.StreamFailed,
            $"Provider '{ProviderName}' failed while streaming after {attemptsMade} attempt(s).",
            lastException);
    }

    private void ThrowIfCircuitOpen(ProviderRequest request)
    {
        lock (_lock)
        {
            if (!_circuitOpen)
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var elapsed = now - _circuitOpenedAt;
            if (elapsed >= _options.CircuitBreakerBreakDuration)
            {
                // Half-open: admit exactly one probe at a time. A probe that never reports back (stream never
                // enumerated) expires after the request timeout so the breaker cannot wedge.
                var probeInFlight = _halfOpenProbeStartedAt is { } probeStartedAt
                    && now - probeStartedAt < _options.RequestTimeout;
                if (!probeInFlight)
                {
                    _halfOpenProbeStartedAt = now;
                    _logger.LogInformation(
                        "Circuit breaker entering half-open state for provider {Provider}. Allowing probe request.",
                        ProviderName);
                    return;
                }

                throw new ProviderExecutionException(
                    ProviderName,
                    request.Model,
                    ProviderFailureKind.StreamFailed,
                    $"Circuit breaker for provider '{ProviderName}' is half-open and a probe request is already in flight.");
            }

            var remaining = _options.CircuitBreakerBreakDuration - elapsed;
            _logger.LogWarning(
                "Circuit breaker is open for provider {Provider}. Rejecting request. Circuit resets in {Remaining}.",
                ProviderName,
                remaining);
            throw new ProviderExecutionException(
                ProviderName,
                request.Model,
                ProviderFailureKind.StreamFailed,
                $"Circuit breaker is open for provider '{ProviderName}'. Try again in {remaining.TotalSeconds:F1}s.");
        }
    }

    private static bool IsTransient(Exception exception)
    {
        if (exception is ProviderStreamFailedEventException failedEventException)
        {
            return ProviderStreamFailureClassifier.IsTransientFailedEvent(failedEventException.FailedEvent);
        }

        if (exception is ProviderExecutionException providerException
            && providerException.Kind is ProviderFailureKind.AuthenticationUnavailable or ProviderFailureKind.MissingProvider or ProviderFailureKind.ContextWindowExceeded)
        {
            return false;
        }

        return exception is not ArgumentException
            && (exception is OperationCanceledException || ProviderStreamFailureClassifier.IsTransient(exception));
    }

    private TimeSpan ComputeDelay(int attempt, Exception exception)
    {
        var retryAfter = exception is ProviderStreamFailedEventException failedEventException
            ? ProviderStreamFailureClassifier.TryGetRetryAfter(failedEventException.FailedEvent)
            : ProviderStreamFailureClassifier.TryGetRetryAfter(exception);
        if (retryAfter is { } hinted && hinted > TimeSpan.Zero)
        {
            return hinted <= _options.MaxRetryDelay ? hinted : _options.MaxRetryDelay;
        }

        var exponential = _options.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var jitter = _options.InitialRetryDelay == TimeSpan.Zero ? 0 : Random.Shared.Next(0, 100);
        return TimeSpan.FromMilliseconds(Math.Min(exponential + jitter, _options.MaxRetryDelay.TotalMilliseconds));
    }

    private void ResetCircuit()
    {
        lock (_lock)
        {
            if (_consecutiveFailures > 0 || _circuitOpen)
            {
                _logger.LogInformation("Circuit breaker reset for provider {Provider}.", ProviderName);
            }

            _consecutiveFailures = 0;
            _circuitOpen = false;
            _halfOpenProbeStartedAt = null;
        }
    }

    private bool IsCircuitOpen()
    {
        lock (_lock)
        {
            return _circuitOpen;
        }
    }

    private void RecordFailureAndOpenCircuitIfNeeded()
    {
        lock (_lock)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures < _options.CircuitBreakerFailureThreshold)
            {
                return;
            }

            _circuitOpen = true;
            _circuitOpenedAt = DateTimeOffset.UtcNow;
            _halfOpenProbeStartedAt = null;
            _logger.LogError(
                "Circuit breaker opened for provider {Provider} after {Failures} consecutive failures.",
                ProviderName,
                _consecutiveFailures);
        }
    }

    private sealed class ProviderStreamFailedEventException(ProviderEvent failedEvent)
        : Exception(failedEvent.Content ?? "Provider stream reported a failure.")
    {
        public ProviderEvent FailedEvent { get; } = failedEvent;
    }
}
