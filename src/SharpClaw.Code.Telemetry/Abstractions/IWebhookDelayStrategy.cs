namespace SharpClaw.Code.Telemetry.Abstractions;

/// <summary>
/// Delays webhook retry attempts without hard-coding <see cref="Task.Delay(TimeSpan, CancellationToken)" /> into the sink.
/// </summary>
public interface IWebhookDelayStrategy
{
    /// <summary>
    /// Waits for the supplied delay interval before the next delivery attempt.
    /// </summary>
    /// <param name="delay">The backoff interval to wait.</param>
    /// <param name="cancellationToken">Cancels the delay.</param>
    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}
