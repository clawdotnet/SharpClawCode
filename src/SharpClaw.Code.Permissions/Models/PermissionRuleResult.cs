namespace SharpClaw.Code.Permissions.Models;

/// <summary>
/// Captures the outcome of evaluating a single permission rule.
/// </summary>
/// <param name="Outcome">The rule outcome.</param>
/// <param name="Reason">A concise explanation for the outcome.</param>
/// <param name="CanRememberApproval">Indicates whether a positive approval can be remembered for the session.</param>
public sealed record PermissionRuleResult(
    PermissionRuleOutcome Outcome,
    string? Reason,
    bool CanRememberApproval = false)
{
    /// <summary>
    /// Creates an abstaining rule result.
    /// </summary>
    /// <returns>The abstaining result.</returns>
    public static PermissionRuleResult Abstain() => new(PermissionRuleOutcome.Abstain, null);

    /// <summary>
    /// Creates an allowing rule result.
    /// </summary>
    /// <param name="reason">The allow reason.</param>
    /// <returns>The allowing result.</returns>
    public static PermissionRuleResult Allow(string reason) => new(PermissionRuleOutcome.Allow, reason);

    /// <summary>
    /// Creates a denying rule result.
    /// </summary>
    /// <param name="reason">The denial reason.</param>
    /// <returns>The denying result.</returns>
    public static PermissionRuleResult Deny(string reason) => new(PermissionRuleOutcome.Deny, reason);

    /// <summary>
    /// Creates a rule result that requires explicit approval.
    /// </summary>
    /// <param name="reason">The approval reason.</param>
    /// <param name="canRememberApproval">Indicates whether a positive approval can be remembered for the session.</param>
    /// <returns>The approval-required result.</returns>
    public static PermissionRuleResult RequireApproval(string reason, bool canRememberApproval = false)
        => new(PermissionRuleOutcome.RequireApproval, reason, canRememberApproval);
}
