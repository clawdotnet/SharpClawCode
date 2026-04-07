using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Abstractions;

/// <summary>
/// Evaluates a single permission rule within the policy engine.
/// </summary>
public interface IPermissionRule
{
    /// <summary>
    /// Evaluates the rule for the current request and context.
    /// </summary>
    /// <param name="request">The tool execution request being evaluated.</param>
    /// <param name="context">The permission evaluation context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The rule evaluation result.</returns>
    Task<PermissionRuleResult> EvaluateAsync(
        ToolExecutionRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken);
}
