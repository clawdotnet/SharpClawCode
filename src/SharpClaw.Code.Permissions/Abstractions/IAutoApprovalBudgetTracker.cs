using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Permissions.Abstractions;

/// <summary>
/// Tracks session-scoped auto-approval budget consumption for bounded autonomy.
/// </summary>
public interface IAutoApprovalBudgetTracker
{
    /// <summary>
    /// Attempts to consume a single auto-approval slot from the configured session budget.
    /// </summary>
    bool TryConsume(PermissionEvaluationContext context, ApprovalScope scope, int budget, out int remainingBudget);
}
