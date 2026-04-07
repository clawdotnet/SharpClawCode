using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Models;

/// <summary>
/// Stores a remembered approval decision for the duration of a session.
/// </summary>
/// <param name="Decision">The remembered approval decision.</param>
public sealed record ApprovalMemoryEntry(ApprovalDecision Decision);
