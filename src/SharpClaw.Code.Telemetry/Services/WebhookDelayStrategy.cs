using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Telemetry.Services;

/// <summary>
/// Uses the platform task scheduler for webhook retry delays.
/// </summary>
public sealed class WebhookDelayStrategy : IWebhookDelayStrategy
{
    /// <inheritdoc />
    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        => Task.Delay(delay, cancellationToken);
}
