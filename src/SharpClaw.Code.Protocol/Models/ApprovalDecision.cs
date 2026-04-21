using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents a user or policy approval response.
/// </summary>
/// <param name="Scope">The scope covered by the approval.</param>
/// <param name="Approved">Indicates whether the request was approved.</param>
/// <param name="RequestedBy">The actor or system that requested approval.</param>
/// <param name="ResolvedBy">The actor that resolved the approval, if known.</param>
/// <param name="Reason">A concise explanation for the approval outcome, if available.</param>
/// <param name="ResolvedAtUtc">The UTC timestamp when the approval was resolved.</param>
/// <param name="ExpiresAtUtc">An optional UTC expiration timestamp for the approval.</param>
/// <param name="RememberForSession">Whether the approval should be remembered for the current session when allowed.</param>
public sealed record ApprovalDecision(
    ApprovalScope Scope,
    bool Approved,
    string? RequestedBy,
    string? ResolvedBy,
    string? Reason,
    DateTimeOffset ResolvedAtUtc,
    DateTimeOffset? ExpiresAtUtc,
    bool RememberForSession = false,
    ApprovalPrincipal? Principal = null);
