using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Protocol.Operational;

namespace SharpClaw.Code.Runtime.Diagnostics.Checks;

/// <summary>
/// Reports approval-auth configuration and health for embedded server workflows.
/// </summary>
public sealed class ApprovalAuthCheck(IApprovalIdentityService approvalIdentityService) : IOperationalCheck
{
    /// <inheritdoc />
    public string Id => "approval.auth";

    /// <inheritdoc />
    public async Task<OperationalCheckItem> ExecuteAsync(OperationalDiagnosticsContext context, CancellationToken cancellationToken)
    {
        var status = await approvalIdentityService.GetStatusAsync(context.NormalizedWorkspacePath, cancellationToken).ConfigureAwait(false);
        var severity = status.Mode switch
        {
            Protocol.Models.ApprovalAuthMode.Disabled => OperationalCheckStatus.Ok,
            _ when status.IsHealthy => OperationalCheckStatus.Ok,
            _ when status.IsConfigured => OperationalCheckStatus.Warn,
            _ => OperationalCheckStatus.Ok,
        };

        return new OperationalCheckItem(
            Id,
            severity,
            $"Approval auth mode: {status.Mode}.",
            status.Detail);
    }
}
