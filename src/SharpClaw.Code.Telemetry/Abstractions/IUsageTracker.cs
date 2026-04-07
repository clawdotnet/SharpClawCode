using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Telemetry.Abstractions;

/// <summary>
/// Aggregates per-session <see cref="UsageSnapshot" /> totals in memory (telemetry-owned; not session storage).
/// </summary>
public interface IUsageTracker
{
    /// <summary>
    /// Merges a usage delta into cumulative totals for the session.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <param name="delta">Token and cost deltas to add.</param>
    void ApplyUsage(string sessionId, UsageSnapshot delta);

    /// <summary>
    /// Gets cumulative usage for the session, if any has been recorded.
    /// </summary>
    /// <param name="sessionId">The session identifier.</param>
    /// <returns>The snapshot or <see langword="null" />.</returns>
    UsageSnapshot? TryGetCumulative(string sessionId);

    /// <summary>
    /// Gets a copy of all cumulative usage keyed by session id.
    /// </summary>
    IReadOnlyDictionary<string, UsageSnapshot> GetCumulativeSnapshot();
}
