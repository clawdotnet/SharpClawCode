using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Services;

/// <summary>
/// Resolves approval requests through an authenticated embedded-host principal.
/// </summary>
public sealed class AuthenticatedApprovalTransport(IApprovalPrincipalAccessor principalAccessor) : IApprovalTransport
{
    /// <inheritdoc />
    public bool CanHandle(PermissionEvaluationContext context)
        => principalAccessor.CurrentPrincipal is not null
            && principalAccessor.CurrentStatus is { Mode: not ApprovalAuthMode.Disabled }
            && !string.Equals(context.SourceName, "acp", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var principal = principalAccessor.CurrentPrincipal;
        if (principal is null)
        {
            return Task.FromResult(new ApprovalDecision(
                request.Scope,
                Approved: false,
                RequestedBy: request.RequestedBy,
                ResolvedBy: "approval-auth",
                Reason: "Approval identity was not available for the current request.",
                ResolvedAtUtc: DateTimeOffset.UtcNow,
                ExpiresAtUtc: null,
                RememberForSession: false));
        }

        if (!string.IsNullOrWhiteSpace(context.TenantId)
            && (string.IsNullOrWhiteSpace(principal.TenantId)
                || !string.Equals(context.TenantId, principal.TenantId, StringComparison.Ordinal)))
        {
            return Task.FromResult(new ApprovalDecision(
                request.Scope,
                Approved: false,
                RequestedBy: request.RequestedBy,
                ResolvedBy: principal.SubjectId,
                Reason: $"Approver tenant '{principal.TenantId ?? "<none>"}' does not match runtime tenant '{context.TenantId}'.",
                ResolvedAtUtc: DateTimeOffset.UtcNow,
                ExpiresAtUtc: null,
                RememberForSession: false,
                Principal: principal));
        }

        return Task.FromResult(new ApprovalDecision(
            request.Scope,
            Approved: true,
            RequestedBy: request.RequestedBy,
            ResolvedBy: principal.SubjectId,
            Reason: "Approved by authenticated embedded-host principal.",
            ResolvedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: null,
            RememberForSession: request.CanRememberDecision,
            Principal: principal));
    }
}
