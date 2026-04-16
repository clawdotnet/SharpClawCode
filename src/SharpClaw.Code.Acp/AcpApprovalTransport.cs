using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Acp;

internal sealed class AcpApprovalTransport(AcpApprovalCoordinator coordinator) : IApprovalTransport
{
    public bool CanHandle(PermissionEvaluationContext context)
        => context.IsInteractive
            && coordinator.SupportsApprovals
            && string.Equals(context.SourceName, "acp", StringComparison.OrdinalIgnoreCase);

    public Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
        => coordinator.RequestApprovalAsync(request, context, cancellationToken);
}
