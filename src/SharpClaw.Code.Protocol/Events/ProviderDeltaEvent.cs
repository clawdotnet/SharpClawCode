namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when a provider stream yields a content delta.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="TurnId">The related turn identifier.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="ProviderName">The provider that emitted the delta.</param>
/// <param name="Model">The concrete model being streamed.</param>
/// <param name="ProviderEventId">The originating provider event identifier.</param>
/// <param name="Kind">The provider event kind.</param>
/// <param name="Content">The streamed content delta.</param>
public sealed record ProviderDeltaEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string ProviderName,
    string Model,
    string ProviderEventId,
    string Kind,
    string Content) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
