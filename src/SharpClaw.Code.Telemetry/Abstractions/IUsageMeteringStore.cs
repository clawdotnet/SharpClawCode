using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Telemetry.Abstractions;

/// <summary>
/// Persists and queries normalized usage metering records.
/// </summary>
public interface IUsageMeteringStore
{
    /// <summary>
    /// Persists one usage metering record for a workspace.
    /// </summary>
    Task AppendAsync(string workspaceRoot, UsageMeteringRecord record, CancellationToken cancellationToken);

    /// <summary>
    /// Builds an aggregated usage summary for the supplied workspace query.
    /// </summary>
    Task<UsageMeteringSummaryReport> GetSummaryAsync(string workspaceRoot, UsageMeteringQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Lists detailed usage metering records for the supplied workspace query.
    /// </summary>
    Task<UsageMeteringDetailReport> GetDetailAsync(
        string workspaceRoot,
        UsageMeteringQuery query,
        int limit,
        CancellationToken cancellationToken);
}
