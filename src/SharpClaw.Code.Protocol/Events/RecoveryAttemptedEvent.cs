namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Raised when the runtime attempts recovery from a checkpoint or failure.
/// </summary>
/// <param name="EventId">The unique runtime event identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="TurnId">The related turn identifier, if any.</param>
/// <param name="OccurredAtUtc">The UTC timestamp when the event occurred.</param>
/// <param name="CheckpointId">The checkpoint identifier used during recovery.</param>
/// <param name="AttemptNumber">The recovery attempt number.</param>
/// <param name="Succeeded">Indicates whether the recovery attempt succeeded.</param>
/// <param name="Summary">A concise recovery summary, if available.</param>
public sealed record RecoveryAttemptedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string CheckpointId,
    int AttemptNumber,
    bool Succeeded,
    string? Summary) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
