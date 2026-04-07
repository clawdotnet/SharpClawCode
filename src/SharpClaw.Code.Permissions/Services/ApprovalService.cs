using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Services;

/// <summary>
/// Selects interactive or non-interactive approval behavior based on the current context.
/// </summary>
public sealed class ApprovalService(
    ConsoleApprovalService consoleApprovalService,
    NonInteractiveApprovalService nonInteractiveApprovalService) : IApprovalService
{
    /// <inheritdoc />
    public Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
        => context.IsInteractive
            ? consoleApprovalService.RequestApprovalAsync(request, context, cancellationToken)
            : nonInteractiveApprovalService.RequestApprovalAsync(request, context, cancellationToken);
}
