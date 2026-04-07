using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Abstractions;

/// <summary>
/// Resolves approval requests that require user or automation input.
/// </summary>
public interface IApprovalService
{
    /// <summary>
    /// Requests approval for a permission-sensitive action.
    /// </summary>
    /// <param name="request">The approval request details.</param>
    /// <param name="context">The evaluation context for the request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resolved approval decision.</returns>
    Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken);
}
