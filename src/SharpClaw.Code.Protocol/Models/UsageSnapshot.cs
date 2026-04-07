namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents normalized token and cost usage captured during execution.
/// </summary>
/// <param name="InputTokens">The number of input tokens consumed.</param>
/// <param name="OutputTokens">The number of output tokens produced.</param>
/// <param name="CachedInputTokens">The number of cached input tokens reused.</param>
/// <param name="TotalTokens">The total token count for the captured interaction.</param>
/// <param name="EstimatedCostUsd">The estimated USD cost, if available.</param>
public sealed record UsageSnapshot(
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    long TotalTokens,
    decimal? EstimatedCostUsd);
