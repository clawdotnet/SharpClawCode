namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Emitted when a session share snapshot is removed.
/// </summary>
/// <param name="EventId">Runtime event id.</param>
/// <param name="SessionId">Session id.</param>
/// <param name="TurnId">Related turn id, if any.</param>
/// <param name="OccurredAtUtc">Timestamp.</param>
/// <param name="ShareId">Removed share id.</param>
public sealed record ShareRemovedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string ShareId) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
