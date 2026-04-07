namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Emitted when an undo is requested.
/// </summary>
public sealed record UndoRequestedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string? TargetMutationSetId) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
