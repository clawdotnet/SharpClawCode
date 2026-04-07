using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Telemetry.Services;

/// <summary>
/// Thread-safe in-memory aggregation of <see cref="UsageSnapshot" /> per session.
/// </summary>
public sealed class UsageTracker : IUsageTracker
{
    private readonly object gate = new();
    private readonly Dictionary<string, UsageSnapshot> cumulative = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void ApplyUsage(string sessionId, UsageSnapshot delta)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(delta);

        lock (gate)
        {
            cumulative[sessionId] = cumulative.TryGetValue(sessionId, out var current)
                ? Merge(current, delta)
                : delta;
        }
    }

    /// <inheritdoc />
    public UsageSnapshot? TryGetCumulative(string sessionId)
    {
        lock (gate)
        {
            return cumulative.TryGetValue(sessionId, out var snap) ? snap : null;
        }
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, UsageSnapshot> GetCumulativeSnapshot()
    {
        lock (gate)
        {
            return new Dictionary<string, UsageSnapshot>(cumulative, StringComparer.Ordinal);
        }
    }

    private static UsageSnapshot Merge(UsageSnapshot previous, UsageSnapshot delta)
    {
        decimal? cost = (previous.EstimatedCostUsd, delta.EstimatedCostUsd) switch
        {
            (decimal a, decimal b) => a + b,
            (decimal a, null) => a,
            (null, decimal b) => b,
            _ => null
        };

        return new UsageSnapshot(
            previous.InputTokens + delta.InputTokens,
            previous.OutputTokens + delta.OutputTokens,
            previous.CachedInputTokens + delta.CachedInputTokens,
            previous.TotalTokens + delta.TotalTokens,
            cost);
    }
}
