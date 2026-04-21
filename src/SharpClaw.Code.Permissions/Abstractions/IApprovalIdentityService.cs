using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Abstractions;

/// <summary>
/// Resolves and validates approval identities for embedded host requests.
/// </summary>
public interface IApprovalIdentityService
{
    /// <summary>
    /// Resolves the approval-auth configuration and health for the workspace.
    /// </summary>
    Task<ApprovalAuthStatus> GetStatusAsync(string workspaceRoot, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the current approval principal for the supplied request, if any.
    /// </summary>
    Task<ApprovalPrincipal?> ResolveAsync(
        string workspaceRoot,
        ApprovalIdentityRequest request,
        RuntimeHostContext? hostContext,
        CancellationToken cancellationToken);
}
