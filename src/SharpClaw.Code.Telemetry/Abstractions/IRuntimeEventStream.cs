using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Telemetry.Abstractions;

/// <summary>
/// Exposes an in-process stream of runtime event envelopes for embedded hosts and admin APIs.
/// </summary>
public interface IRuntimeEventStream
{
    /// <summary>
    /// Returns recent event envelopes retained in memory.
    /// </summary>
    IReadOnlyList<RuntimeEventEnvelope> GetRecentEnvelopesSnapshot();

    /// <summary>
    /// Streams event envelopes as they are published.
    /// </summary>
    IAsyncEnumerable<RuntimeEventEnvelope> StreamAsync(CancellationToken cancellationToken);
}
