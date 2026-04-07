using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when a conversation turn completes execution.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="TurnId">The related turn identifier.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="Turn">The completed turn snapshot.</param>
/// <param name="Succeeded">Indicates whether the turn completed successfully.</param>
/// <param name="Summary">A concise execution summary, if available.</param>
public sealed record TurnCompletedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    ConversationTurn Turn,
    bool Succeeded,
    string? Summary) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
