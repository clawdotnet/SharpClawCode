namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Emitted when undo cannot be applied safely.
/// </summary>
public sealed record UndoFailedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string Reason) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
