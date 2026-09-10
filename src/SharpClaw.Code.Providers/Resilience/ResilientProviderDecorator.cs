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

                        emittedEvent = true;
                        yield return enumerator.Current;
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

            var elapsed = DateTimeOffset.UtcNow - _circuitOpenedAt;
            if (elapsed >= _options.CircuitBreakerBreakDuration)
            {
                _circuitOpen = false;
                _logger.LogInformation(
                    "Circuit breaker entering half-open state for provider {Provider}. Allowing probe request.",
                    ProviderName);
                return;
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
        if (exception is ProviderExecutionException providerException
            && providerException.Kind is ProviderFailureKind.AuthenticationUnavailable or ProviderFailureKind.MissingProvider)
        {
            return false;
        }

        return exception is not ArgumentException
            && exception is HttpRequestException or OperationCanceledException or TimeoutException or IOException;
    }

    private TimeSpan ComputeDelay(int attempt, Exception exception)
    {
        if (exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } httpException
            && httpException.Data.Contains("Retry-After")
            && httpException.Data["Retry-After"] is int retryAfterSeconds and > 0)
        {
            var retryAfter = TimeSpan.FromSeconds(retryAfterSeconds);
            return retryAfter <= _options.MaxRetryDelay ? retryAfter : _options.MaxRetryDelay;
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
            _logger.LogError(
                "Circuit breaker opened for provider {Provider} after {Failures} consecutive failures.",
                ProviderName,
                _consecutiveFailures);
        }
    }
}
