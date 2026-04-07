namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Emitted when redo applied successfully.
/// </summary>
public sealed record RedoCompletedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string MutationSetId) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
