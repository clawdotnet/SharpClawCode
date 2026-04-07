namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents a durable checkpoint captured for runtime recovery.
/// </summary>
/// <param name="Id">The unique checkpoint identifier.</param>
/// <param name="SessionId">The parent session identifier.</param>
/// <param name="TurnId">The related turn identifier, if any.</param>
/// <param name="CreatedAtUtc">The UTC timestamp when the checkpoint was created.</param>
/// <param name="Summary">A concise summary of the captured recovery point.</param>
/// <param name="StateLocation">The durable location or key for the checkpoint payload.</param>
/// <param name="RecoveryHint">A recovery hint or label, if available.</param>
/// <param name="Metadata">Additional machine-readable checkpoint metadata.</param>
public sealed record RuntimeCheckpoint(
    string Id,
    string SessionId,
    string? TurnId,
    DateTimeOffset CreatedAtUtc,
    string Summary,
    string StateLocation,
    string? RecoveryHint,
    Dictionary<string, string>? Metadata);
