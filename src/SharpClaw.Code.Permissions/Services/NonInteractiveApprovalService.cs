using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Services;

/// <summary>
/// Denies approval requests when the caller is non-interactive.
/// </summary>
public sealed class NonInteractiveApprovalService : IApprovalService
{
    /// <inheritdoc />
    public Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
        => Task.FromResult(new ApprovalDecision(
            Scope: request.Scope,
            Approved: false,
            RequestedBy: request.RequestedBy,
            ResolvedBy: "non-interactive",
            Reason: "Approval was required but the caller is non-interactive.",
            ResolvedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: null,
            RememberForSession: false));
}
