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
    IEnumerable<IApprovalTransport> approvalTransports) : IApprovalService
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

        return context.IsInteractive
            ? consoleApprovalService.RequestApprovalAsync(request, context, cancellationToken)
            : nonInteractiveApprovalService.RequestApprovalAsync(request, context, cancellationToken);
    }
}
