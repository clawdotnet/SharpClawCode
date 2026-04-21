using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Abstractions;

/// <summary>
/// Exposes the current approval principal and auth status for the active async flow.
/// </summary>
public interface IApprovalPrincipalAccessor
{
    /// <summary>
    /// Gets the current approval principal, when available.
    /// </summary>
    ApprovalPrincipal? CurrentPrincipal { get; }

    /// <summary>
    /// Gets the current approval-auth status for the active request.
    /// </summary>
    ApprovalAuthStatus? CurrentStatus { get; }

    /// <summary>
    /// Sets the current approval principal and auth status for the active async flow.
    /// </summary>
    IDisposable BeginScope(ApprovalPrincipal? principal, ApprovalAuthStatus? status);
}
