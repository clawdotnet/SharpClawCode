namespace SharpClaw.Code.Providers.Configuration;

/// <summary>
/// Configures resilience behavior for provider requests.
/// </summary>
public sealed class ProviderResilienceOptions
{
    /// <summary>Maximum number of retry attempts for transient failures.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Initial delay before the first retry.</summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Maximum delay between retries.</summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Timeout for a single provider request.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Number of consecutive failures before the circuit opens.</summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>Duration the circuit stays open before allowing a probe request.</summary>
    public TimeSpan CircuitBreakerBreakDuration { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Whether resilience is enabled. When false, no retry/circuit-breaker wrapping is applied.</summary>
    public bool Enabled { get; set; } = true;
}
