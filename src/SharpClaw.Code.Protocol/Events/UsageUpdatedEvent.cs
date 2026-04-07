using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when usage totals are updated.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="TurnId">The related turn identifier, if any.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="Usage">The latest usage snapshot.</param>
public sealed record UsageUpdatedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    UsageSnapshot Usage) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
