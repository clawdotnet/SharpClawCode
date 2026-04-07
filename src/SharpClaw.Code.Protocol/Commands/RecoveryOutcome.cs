using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Commands;

/// <summary>
/// Represents the result of a recovery attempt.
/// </summary>
/// <param name="SessionId">The affected session identifier.</param>
/// <param name="Recovered">Indicates whether recovery succeeded.</param>
/// <param name="SessionState">The resulting session state.</param>
/// <param name="Message">A concise recovery summary.</param>
/// <param name="CheckpointId">The checkpoint identifier used during recovery, if any.</param>
/// <param name="CompletedAtUtc">The UTC timestamp when recovery completed.</param>
public sealed record RecoveryOutcome(
    string SessionId,
    bool Recovered,
    SessionLifecycleState SessionState,
    string Message,
    string? CheckpointId,
    DateTimeOffset CompletedAtUtc);
