using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Configures session-scoped auto-approval behavior for elevated operations.
/// </summary>
/// <param name="AutoApproveScopes">Approval scopes that may be auto-approved without an interactive prompt.</param>
/// <param name="AutoApproveBudget">
/// Optional cap on the number of auto-approved elevated operations for the current session.
/// When null, matching scopes may be auto-approved without a numeric limit.
/// </param>
public sealed record ApprovalSettings(
    IReadOnlyList<ApprovalScope> AutoApproveScopes,
    int? AutoApproveBudget)
{
    /// <summary>
    /// Gets an empty approval-settings instance that disables auto-approval.
    /// </summary>
    public static ApprovalSettings Empty { get; } = new([], null);

    /// <summary>
    /// Gets a value indicating whether the configuration enables any auto-approval behavior.
    /// </summary>
    public bool HasAutoApproval => AutoApproveScopes.Count > 0;
}
