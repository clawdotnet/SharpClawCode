using Microsoft.Extensions.AI;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Internal;

/// <summary>
/// Maps Microsoft.Extensions.AI streaming updates and usage content into SharpClaw <see cref="ProviderEvent"/> instances.
/// </summary>
internal static class ProviderStreamEventFactory
{
    /// <summary>
    /// Creates a non-terminal delta event.
    /// </summary>
    public static ProviderEvent Delta(string requestId, ISystemClock clock, string? text)
        => new(
            Id: $"provider-event-{Guid.NewGuid():N}",
            RequestId: requestId,
            Kind: "delta",
            CreatedAtUtc: clock.UtcNow,
            Content: text,
            IsTerminal: false,
            Usage: null);

    /// <summary>
    /// Creates a terminal completed event with optional usage.
    /// </summary>
    public static ProviderEvent Completed(string requestId, ISystemClock clock, UsageSnapshot? usage)
        => new(
            Id: $"provider-event-{Guid.NewGuid():N}",
            RequestId: requestId,
            Kind: "completed",
            CreatedAtUtc: clock.UtcNow,
            Content: null,
            IsTerminal: true,
            Usage: usage);

    /// <summary>
    /// Maps MEAI <see cref="UsageDetails"/> to a protocol <see cref="UsageSnapshot"/>.
    /// </summary>
    public static UsageSnapshot? FromUsageDetails(UsageDetails? details)
    {
        if (details is null)
        {
            return null;
        }

        var input = details.InputTokenCount ?? 0L;
        var output = details.OutputTokenCount ?? 0L;
        var cached = details.CachedInputTokenCount ?? 0L;
        var total = details.TotalTokenCount ?? input + output;

        if (input == 0 && output == 0 && cached == 0 && total == 0)
        {
            return null;
        }

        return new UsageSnapshot(
            InputTokens: input,
            OutputTokens: output,
            CachedInputTokens: cached,
            TotalTokens: total,
            EstimatedCostUsd: null);
    }

    /// <summary>
    /// Extracts usage from a streamed update's message contents, if present.
    /// </summary>
    public static UsageSnapshot? TryUsageFromUpdate(ChatResponseUpdate update)
    {
        if (update.Contents is null)
        {
            return null;
        }

        foreach (var content in update.Contents)
        {
            if (content is UsageContent usageContent)
            {
                return FromUsageDetails(usageContent.Details);
            }
        }

        return null;
    }
}
