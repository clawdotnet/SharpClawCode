using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Protocol.Commands;

/// <summary>
/// Represents the context used to attempt runtime recovery.
/// </summary>
/// <param name="SessionId">The affected session identifier.</param>
/// <param name="Checkpoint">The checkpoint selected for recovery.</param>
/// <param name="FailureReason">The reason recovery was requested.</param>
/// <param name="AttemptNumber">The current recovery attempt number.</param>
/// <param name="RequestedAtUtc">The UTC timestamp when recovery was requested.</param>
public sealed record RecoveryContext(
    string SessionId,
    RuntimeCheckpoint Checkpoint,
    string FailureReason,
    int AttemptNumber,
    DateTimeOffset RequestedAtUtc);
