using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Emitted when a session share snapshot is created.
/// </summary>
/// <param name="EventId">Runtime event id.</param>
/// <param name="SessionId">Session id.</param>
/// <param name="TurnId">Related turn id, if any.</param>
/// <param name="OccurredAtUtc">Timestamp.</param>
/// <param name="Share">Created share metadata.</param>
public sealed record ShareCreatedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    ShareSessionRecord Share) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
