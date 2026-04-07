namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents a single prompt-response turn within a conversation session.
/// </summary>
/// <param name="Id">The unique turn identifier.</param>
/// <param name="SessionId">The parent session identifier.</param>
/// <param name="SequenceNumber">The zero- or one-based turn sequence number.</param>
/// <param name="Input">The user or system input for the turn.</param>
/// <param name="Output">The final output captured for the turn, if available.</param>
/// <param name="StartedAtUtc">The UTC timestamp when turn execution started.</param>
/// <param name="CompletedAtUtc">The UTC timestamp when turn execution completed, if available.</param>
/// <param name="AgentId">The primary agent identifier used for the turn, if known.</param>
/// <param name="SlashCommandName">The slash command invoked for the turn, if any.</param>
/// <param name="Usage">The usage snapshot captured for the turn, if any.</param>
/// <param name="Metadata">Additional machine-readable turn metadata.</param>
public sealed record ConversationTurn(
    string Id,
    string SessionId,
    int SequenceNumber,
    string Input,
    string? Output,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? CompletedAtUtc,
    string? AgentId,
    string? SlashCommandName,
    UsageSnapshot? Usage,
    Dictionary<string, string>? Metadata);
