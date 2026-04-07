using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when a conversation session changes lifecycle state.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="TurnId">The related turn identifier, if any.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="PreviousState">The prior session state.</param>
/// <param name="CurrentState">The new session state.</param>
/// <param name="Reason">A concise reason for the state transition, if available.</param>
public sealed record SessionStateChangedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    SessionLifecycleState PreviousState,
    SessionLifecycleState CurrentState,
    string? Reason) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
