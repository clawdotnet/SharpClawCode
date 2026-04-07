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
}
