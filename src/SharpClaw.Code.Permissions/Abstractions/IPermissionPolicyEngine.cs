using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Abstractions;

/// <summary>
/// Evaluates tool execution requests through the configured permission policy rules.
/// </summary>
public interface IPermissionPolicyEngine
{
    /// <summary>
    /// Evaluates a tool execution request against the active permission policy.
    /// </summary>
    /// <param name="request">The tool execution request to evaluate.</param>
    /// <param name="context">The permission evaluation context.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The resulting permission decision.</returns>
    Task<PermissionDecision> EvaluateAsync(
        ToolExecutionRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken);
}
