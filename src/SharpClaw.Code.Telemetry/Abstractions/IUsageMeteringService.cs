using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Telemetry.Abstractions;

/// <summary>
/// Exposes query surfaces for persisted usage metering.
/// </summary>
public interface IUsageMeteringService
{
    /// <summary>
    /// Builds an aggregated usage summary for the supplied workspace query.
    /// </summary>
    Task<UsageMeteringSummaryReport> GetSummaryAsync(string workspaceRoot, UsageMeteringQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Lists detailed metering records for the supplied workspace query.
    /// </summary>
    Task<UsageMeteringDetailReport> GetDetailAsync(
        string workspaceRoot,
        UsageMeteringQuery query,
        int limit,
        CancellationToken cancellationToken);
}
