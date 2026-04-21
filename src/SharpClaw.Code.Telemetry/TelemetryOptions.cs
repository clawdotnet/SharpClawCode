namespace SharpClaw.Code.Telemetry;

/// <summary>
/// Configuration for in-process telemetry buffers and export defaults.
/// </summary>
public sealed class TelemetryOptions
{
    /// <summary>
    /// Maximum number of <see cref="SharpClaw.Code.Protocol.Events.RuntimeEvent" /> instances retained in the ring buffer.
    /// </summary>
    public int RuntimeEventRingBufferCapacity { get; set; } = 10_000;

    /// <summary>
    /// Optional webhook destinations that receive normalized runtime event envelopes.
    /// </summary>
    public List<string> EventWebhookUrls { get; } = [];

    /// <summary>
    /// Maximum number of webhook delivery attempts per event.
    /// </summary>
    public int WebhookMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Initial webhook retry delay in milliseconds.
    /// </summary>
    public int WebhookInitialBackoffMilliseconds { get; set; } = 200;
}
