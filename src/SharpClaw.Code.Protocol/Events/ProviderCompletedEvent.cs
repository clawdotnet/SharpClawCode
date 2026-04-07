using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when a provider stream completes.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="TurnId">The related turn identifier.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="ProviderName">The provider that completed.</param>
/// <param name="Model">The concrete model that completed.</param>
/// <param name="ProviderEventId">The originating provider event identifier.</param>
/// <param name="Kind">The provider event kind.</param>
/// <param name="Usage">The terminal usage snapshot, if any.</param>
public sealed record ProviderCompletedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string ProviderName,
    string Model,
    string ProviderEventId,
    string Kind,
    UsageSnapshot? Usage) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
