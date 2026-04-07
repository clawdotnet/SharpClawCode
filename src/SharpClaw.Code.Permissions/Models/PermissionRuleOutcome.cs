namespace SharpClaw.Code.Permissions.Models;

/// <summary>
/// Describes the outcome produced by a permission rule.
/// </summary>
public enum PermissionRuleOutcome
{
    /// <summary>
    /// The rule did not make a decision.
    /// </summary>
    Abstain,

    /// <summary>
    /// The rule allowed the request immediately.
    /// </summary>
    Allow,

    /// <summary>
    /// The rule denied the request immediately.
    /// </summary>
    Deny,

    /// <summary>
    /// The rule requires explicit approval to proceed.
    /// </summary>
    RequireApproval,
}
