using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// A single row for multi-session listing UIs and JSON output.
/// </summary>
public sealed record SessionSummaryRow(
    string SessionId,
    string Title,
    DateTimeOffset UpdatedAtUtc,
    SessionLifecycleState State,
    string? ParentSessionId,
    string? ActiveSessionMarker);
