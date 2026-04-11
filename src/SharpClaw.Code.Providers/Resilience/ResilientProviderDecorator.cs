using System.Net;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Resilience;

/// <summary>
/// Decorates an <see cref="IModelProvider"/> with retry, rate-limit handling, and circuit-breaker resilience.
/// </summary>
internal sealed class ResilientProviderDecorator : IModelProvider
{
    private readonly IModelProvider _inner;
    private readonly ProviderResilienceOptions _options;
    private readonly ILogger _logger;

    // Circuit breaker state
    private int _consecutiveFailures;
    private DateTimeOffset _circuitOpenedAt;
    private bool _circuitOpen;
    private readonly object _lock = new();

    public ResilientProviderDecorator(
        IModelProvider inner,
        ProviderResilienceOptions options,
        ILogger logger)
    {
        _inner = inner;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProviderName => _inner.ProviderName;

    /// <inheritdoc />
    public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
        => _inner.GetAuthStatusAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken ct)
    {
        // 1. Check circuit breaker
        lock (_lock)
        {
            if (_circuitOpen)
            {
                var elapsed = DateTimeOffset.UtcNow - _circuitOpenedAt;
                if (elapsed < _options.CircuitBreakerBreakDuration)
                {
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

                // Break duration elapsed — allow a probe attempt (half-open)
                _circuitOpen = false;
                _logger.LogInformation(
                    "Circuit breaker entering half-open state for provider {Provider}. Allowing probe request.",
                    ProviderName);
            }
        }

        // 2. Retry loop
        Exception? lastException = null;

        for (var attempt = 0; attempt <= _options.MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            using var timeoutCts = new CancellationTokenSource(_options.RequestTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                var result = await _inner.StartStreamAsync(request, linkedCts.Token).ConfigureAwait(false);

                // Success — reset circuit breaker
                ResetCircuit();
                return result;
            }
            catch (Exception ex) when (!IsTransient(ex))
            {
                // Non-transient: fail immediately without retrying
                _logger.LogError(
                    ex,
                    "Non-transient failure from provider {Provider} on attempt {Attempt}. Not retrying.",
                    ProviderName,
                    attempt + 1);
                RecordFailure();
                throw;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                lastException = ex;
                RecordFailure();

                if (attempt >= _options.MaxRetries)
                {
                    // All retries exhausted
                    break;
                }

                // Determine delay — respect Retry-After for 429 responses
                var delay = ComputeDelay(attempt, ex);

                _logger.LogWarning(
                    ex,
                    "Transient failure from provider {Provider} on attempt {Attempt}/{MaxAttempts}. Retrying in {Delay}ms.",
                    ProviderName,
                    attempt + 1,
                    _options.MaxRetries + 1,
                    delay.TotalMilliseconds);

                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }

        // All retries exhausted — open circuit if threshold reached
        OpenCircuitIfThresholdReached();

        throw new ProviderExecutionException(
            ProviderName,
            request.Model,
            ProviderFailureKind.StreamFailed,
            $"Provider '{ProviderName}' failed after {_options.MaxRetries + 1} attempt(s).",
            lastException);
    }

    private static bool IsTransient(Exception ex)
    {
        // Non-transient exceptions we should NOT retry
        if (ex is ProviderExecutionException pee &&
            (pee.Kind is ProviderFailureKind.AuthenticationUnavailable or ProviderFailureKind.MissingProvider))
        {
            return false;
        }

        if (ex is ArgumentException)
        {
            return false;
        }

        // Transient: HTTP errors (5xx), timeouts, IO failures
        return ex is HttpRequestException or TaskCanceledException or IOException;
    }

    private TimeSpan ComputeDelay(int attempt, Exception ex)
    {
        // Check for 429 with Retry-After
        if (ex is HttpRequestException httpEx && httpEx.StatusCode == HttpStatusCode.TooManyRequests)
        {
            // Attempt to extract Retry-After from the inner exception message or data
            // HttpRequestException does not carry headers directly; providers may embed seconds in Data
            if (httpEx.Data.Contains("Retry-After") &&
                httpEx.Data["Retry-After"] is int retryAfterSeconds and > 0)
            {
                var retryAfterDelay = TimeSpan.FromSeconds(retryAfterSeconds);
                if (retryAfterDelay <= _options.MaxRetryDelay)
                {
                    return retryAfterDelay;
                }
            }
        }

        // Exponential backoff: InitialDelay * 2^attempt + jitter
        var exponential = _options.InitialRetryDelay.TotalMilliseconds * Math.Pow(2, attempt);
        var jitter = Random.Shared.Next(0, 100);
        var total = exponential + jitter;
        var capped = Math.Min(total, _options.MaxRetryDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(capped);
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

    private void RecordFailure()
    {
        lock (_lock)
        {
            _consecutiveFailures++;
        }
    }

    private void OpenCircuitIfThresholdReached()
    {
        lock (_lock)
        {
            if (_consecutiveFailures >= _options.CircuitBreakerFailureThreshold)
            {
                _circuitOpen = true;
                _circuitOpenedAt = DateTimeOffset.UtcNow;
                _logger.LogError(
                    "Circuit breaker opened for provider {Provider} after {Failures} consecutive failures.",
                    ProviderName,
                    _consecutiveFailures);
            }
        }
    }
}
