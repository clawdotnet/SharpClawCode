namespace SharpClaw.Code.Protocol.Events;

/// <summary>
/// Emitted when a durable mutation set is recorded for a checkpoint.
/// </summary>
public sealed record MutationSetRecordedEvent(
    string EventId,
    string SessionId,
    string? TurnId,
    DateTimeOffset OccurredAtUtc,
    string MutationSetId,
    string CheckpointId,
    int OperationCount) : RuntimeEvent(EventId, SessionId, TurnId, OccurredAtUtc);
