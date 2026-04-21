using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Telemetry.Abstractions;

/// <summary>
/// Receives normalized runtime event envelopes for external streaming or integration.
/// </summary>
public interface IRuntimeEventSink
{
    /// <summary>
    /// Publishes one runtime event envelope to the sink.
    /// </summary>
    Task PublishAsync(RuntimeEventEnvelope envelope, CancellationToken cancellationToken);
}
