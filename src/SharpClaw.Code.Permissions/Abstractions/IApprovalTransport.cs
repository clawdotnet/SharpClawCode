using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Abstractions;

/// <summary>
/// Handles interactive approval requests for a specific caller transport.
/// </summary>
public interface IApprovalTransport
{
    /// <summary>
    /// Returns whether the transport can handle approvals for the supplied context.
    /// </summary>
    bool CanHandle(PermissionEvaluationContext context);

    /// <summary>
    /// Requests approval through the transport.
    /// </summary>
    Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken);
}
