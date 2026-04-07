namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Emitted when undo applied successfully.
/// </summary>
public sealed record UndoCompletedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string MutationSetId) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
