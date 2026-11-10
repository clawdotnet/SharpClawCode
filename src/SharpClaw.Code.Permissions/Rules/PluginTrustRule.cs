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

        if (context.PermissionMode == PermissionMode.ReadOnly || context.PrimaryMode is PrimaryMode.Plan or PrimaryMode.Research)
        {
            return Task.FromResult(PermissionRuleResult.Deny("Plugin processes are unavailable in read-only workflows."));
        }

        // Workspace session files, like manifests, are not an authority for executing arbitrary processes.
        return Task.FromResult(PermissionRuleResult.RequireApproval(
                $"Plugin tool '{request.ToolName}' from '{context.ToolOriginatingPluginId}' (manifest trust: {manifestTrust}) requires explicit approval.",
                canRememberApproval: false));
    }
}
