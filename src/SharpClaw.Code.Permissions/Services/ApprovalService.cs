using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Services;

/// <summary>
/// Selects interactive or non-interactive approval behavior based on the current context.
/// </summary>
public sealed class ApprovalService(
    ConsoleApprovalService consoleApprovalService,
    NonInteractiveApprovalService nonInteractiveApprovalService,
    IEnumerable<IApprovalTransport> approvalTransports,
    IApprovalPrincipalAccessor approvalPrincipalAccessor) : IApprovalService
{
    private readonly IApprovalTransport[] approvalTransports = approvalTransports.ToArray();

    /// <inheritdoc />
    public Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        var transport = approvalTransports.FirstOrDefault(candidate => candidate.CanHandle(context));
        if (transport is not null)
        {
            return transport.RequestApprovalAsync(request, context, cancellationToken);
        }

        if (approvalPrincipalAccessor.CurrentStatus is { RequireAuthenticatedApprovals: true, Mode: not ApprovalAuthMode.Disabled }
            && approvalPrincipalAccessor.CurrentPrincipal is null)
        {
            return Task.FromResult(new ApprovalDecision(
                request.Scope,
                Approved: false,
                RequestedBy: request.RequestedBy,
                ResolvedBy: "approval-auth",
                Reason: "Authenticated approval is required for this host, but no valid approval identity was supplied.",
                ResolvedAtUtc: DateTimeOffset.UtcNow,
                ExpiresAtUtc: null,
                RememberForSession: false));
        }

        return context.IsInteractive
            ? consoleApprovalService.RequestApprovalAsync(request, context, cancellationToken)
            : nonInteractiveApprovalService.RequestApprovalAsync(request, context, cancellationToken);
    }
}
