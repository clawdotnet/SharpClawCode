namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Emitted when redo cannot be applied safely.
/// </summary>
public sealed record RedoFailedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string Reason) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
