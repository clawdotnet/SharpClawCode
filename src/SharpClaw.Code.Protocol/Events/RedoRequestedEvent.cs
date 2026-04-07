namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Emitted when redo is requested.
/// </summary>
public sealed record RedoRequestedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string? TargetMutationSetId) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
