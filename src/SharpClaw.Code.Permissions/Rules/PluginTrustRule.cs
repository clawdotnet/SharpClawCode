using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Rules;

/// <summary>
/// Applies trust policy for plugin-initiated requests and for tools surfaced from plugin manifests.
/// </summary>
public sealed class PluginTrustRule : IPermissionRule
{
    /// <inheritdoc />
    public Task<PermissionRuleResult> EvaluateAsync(
        ToolExecutionRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        if (context.SourceKind is PermissionRequestSourceKind.Plugin)
        {
            return PermissionRuleHelpers.IsTrusted(context.TrustedPluginNames, context.SourceName)
                ? Task.FromResult(PermissionRuleResult.Abstain())
                : Task.FromResult(PermissionRuleResult.RequireApproval(
                    $"Plugin '{context.SourceName ?? "unknown"}' is not trusted to execute tools.",
                    canRememberApproval: true));
        }

        if (string.IsNullOrWhiteSpace(context.ToolOriginatingPluginId) || context.ToolOriginatingPluginTrust is not { } manifestTrust)
        {
            return Task.FromResult(PermissionRuleResult.Abstain());
        }

        if (manifestTrust is PluginTrustLevel.DeveloperTrusted or PluginTrustLevel.WorkspaceTrusted)
        {
            return Task.FromResult(PermissionRuleResult.Abstain());
        }

        return PermissionRuleHelpers.IsTrusted(context.TrustedPluginNames, context.ToolOriginatingPluginId)
            ? Task.FromResult(PermissionRuleResult.Abstain())
            : Task.FromResult(PermissionRuleResult.RequireApproval(
                $"Plugin tool '{request.ToolName}' from '{context.ToolOriginatingPluginId}' (manifest trust: Untrusted) requires approval or session trust.",
                canRememberApproval: true));
    }
}
