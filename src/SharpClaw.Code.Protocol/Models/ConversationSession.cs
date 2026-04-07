using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents durable metadata for a conversation session.
/// </summary>
/// <param name="Id">The unique session identifier.</param>
/// <param name="Title">A human-friendly session title.</param>
/// <param name="State">The current session lifecycle state.</param>
/// <param name="PermissionMode">The permission mode applied to the session.</param>
/// <param name="OutputFormat">The preferred output format for session results.</param>
/// <param name="WorkingDirectory">The active working directory for the session.</param>
/// <param name="RepositoryRoot">The repository root associated with the session.</param>
/// <param name="CreatedAtUtc">The UTC timestamp when the session was created.</param>
/// <param name="UpdatedAtUtc">The UTC timestamp when the session was last updated.</param>
/// <param name="ActiveTurnId">The current active turn identifier, if any.</param>
/// <param name="LastCheckpointId">The most recent runtime checkpoint identifier, if any.</param>
/// <param name="Metadata">Additional machine-readable session metadata.</param>
public sealed record ConversationSession(
    string Id,
    string Title,
    SessionLifecycleState State,
    PermissionMode PermissionMode,
    OutputFormat OutputFormat,
    string? WorkingDirectory,
    string? RepositoryRoot,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    string? ActiveTurnId,
    string? LastCheckpointId,
    Dictionary<string, string>? Metadata);
