using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Rules;

/// <summary>
/// Requires approval for tool requests initiated by untrusted MCP servers.
/// </summary>
public sealed class McpTrustRule : IPermissionRule
{
    /// <inheritdoc />
    public Task<PermissionRuleResult> EvaluateAsync(
        ToolExecutionRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        if (context.SourceKind is not PermissionRequestSourceKind.Mcp)
        {
            return Task.FromResult(PermissionRuleResult.Abstain());
        }

        return PermissionRuleHelpers.IsTrusted(context.TrustedMcpServerNames, context.SourceName)
            ? Task.FromResult(PermissionRuleResult.Abstain())
            : Task.FromResult(PermissionRuleResult.RequireApproval($"MCP server '{context.SourceName ?? "unknown"}' is not trusted to execute tools.", canRememberApproval: true));
    }
}
