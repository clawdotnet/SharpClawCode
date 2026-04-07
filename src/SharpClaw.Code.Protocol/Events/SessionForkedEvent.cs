namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when a session is forked from a parent session.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The new child session id.</param>
/// <param name="TurnId">The related turn id, if any.</param>
/// <param name="OccurredAtUtc">When the fork occurred.</param>
/// <param name="ParentSessionId">Source session id.</param>
/// <param name="ChildSessionId">Same as <paramref name="SessionId"/> for clarity in exports.</param>
/// <param name="ForkedFromCheckpointId">Optional checkpoint id on the parent.</param>
public sealed record SessionForkedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string ParentSessionId,
    string ChildSessionId,
    string? ForkedFromCheckpointId) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
