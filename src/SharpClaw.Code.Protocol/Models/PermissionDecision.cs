using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents the evaluated permission outcome for a capability request.
/// </summary>
/// <param name="Scope">The approval scope being evaluated.</param>
/// <param name="Mode">The permission mode used during evaluation.</param>
/// <param name="IsAllowed">Indicates whether the requested action is allowed.</param>
/// <param name="Reason">A concise explanation for the decision, if available.</param>
/// <param name="EvaluatedAtUtc">The UTC timestamp when the decision was produced.</param>
public sealed record PermissionDecision(
    ApprovalScope Scope,
    PermissionMode Mode,
    bool IsAllowed,
    string? Reason,
    DateTimeOffset EvaluatedAtUtc);
