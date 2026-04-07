using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when the runtime starts a provider request.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="TurnId">The related turn identifier.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="ProviderName">The provider selected for the request.</param>
/// <param name="Model">The concrete model selected for the request.</param>
/// <param name="Request">The provider request snapshot.</param>
public sealed record ProviderStartedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string ProviderName,
    string Model,
    ProviderRequest Request) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
