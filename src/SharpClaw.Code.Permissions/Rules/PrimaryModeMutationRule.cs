using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Rules;

/// <summary>
/// Blocks mutating tool executions while the session is in <see cref="PrimaryMode.Plan"/>.
/// </summary>
public sealed class PrimaryModeMutationRule : IPermissionRule
{
    /// <inheritdoc />
    public Task<PermissionRuleResult> EvaluateAsync(
        ToolExecutionRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        if (context.PrimaryMode != PrimaryMode.Plan)
        {
            return Task.FromResult(PermissionRuleResult.Abstain());
        }

        if (request.IsDestructive)
        {
            return Task.FromResult(PermissionRuleResult.Deny("Plan mode blocks mutating tools."));
        }

        if (request.ApprovalScope is ApprovalScope.FileSystemWrite or ApprovalScope.ShellExecution)
        {
            return Task.FromResult(PermissionRuleResult.Deny($"Plan mode blocks {request.ApprovalScope}."));
        }

        return Task.FromResult(PermissionRuleResult.Abstain());
    }
}
