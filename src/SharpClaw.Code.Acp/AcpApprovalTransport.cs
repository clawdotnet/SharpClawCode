using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Acp;

/// <summary>Routes interactive ACP approvals to the connected editor under the host's identity policy.</summary>
internal sealed class AcpApprovalTransport(AcpApprovalCoordinator coordinator, IApprovalPrincipalAccessor principals) : IApprovalTransport
{
    public bool CanHandle(PermissionEvaluationContext context)
        => context.IsInteractive && string.Equals(context.SourceName, "acp", StringComparison.OrdinalIgnoreCase);

    public async Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, PermissionEvaluationContext context, CancellationToken cancellationToken)
    {
        var requiresIdentity = principals.CurrentStatus is { RequireAuthenticatedApprovals: true, Mode: not ApprovalAuthMode.Disabled };
        var principal = principals.CurrentPrincipal;
        if (requiresIdentity && (principal is null ||
            (!string.IsNullOrWhiteSpace(context.TenantId) && !string.Equals(context.TenantId, principal.TenantId, StringComparison.Ordinal))))
        {
            return new(request.Scope, false, request.RequestedBy, "approval-auth", "A valid approval identity for this host and tenant is required.",
                DateTimeOffset.UtcNow, null, RememberForSession: false);
        }
        var decision = await coordinator.RequestApprovalAsync(request, context, cancellationToken).ConfigureAwait(false);
        return requiresIdentity ? decision with { Principal = principal, ResolvedBy = principal!.SubjectId } : decision;
    }
}
