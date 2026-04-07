using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Rules;

/// <summary>
/// Restricts execution to an explicit allowed-tool list when one is provided.
/// </summary>
public sealed class AllowedToolRule : IPermissionRule
{
    /// <inheritdoc />
    public Task<PermissionRuleResult> EvaluateAsync(
        ToolExecutionRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        if (context.AllowedTools is null || context.AllowedTools.Count == 0)
        {
            return Task.FromResult(PermissionRuleResult.Abstain());
        }

        return context.AllowedTools.Contains(request.ToolName, StringComparer.OrdinalIgnoreCase)
            ? Task.FromResult(PermissionRuleResult.Abstain())
            : Task.FromResult(PermissionRuleResult.Deny($"Tool '{request.ToolName}' is not in the explicit allow list."));
    }
}
