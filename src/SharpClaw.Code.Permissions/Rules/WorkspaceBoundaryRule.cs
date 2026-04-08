using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Rules;

/// <summary>
/// Denies requests that attempt to operate outside the active workspace boundary.
/// </summary>
public sealed class WorkspaceBoundaryRule(IPathService pathService) : IPermissionRule
{
    /// <inheritdoc />
    public Task<PermissionRuleResult> EvaluateAsync(
        ToolExecutionRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        var pathsToCheck = request.ToolName switch
        {
            "read_file" or "write_file" or "edit_file"
                => new[] { PermissionRuleHelpers.TryReadJsonString(request, "path") },
            "bash"
                => new[] { PermissionRuleHelpers.TryReadJsonString(request, "workingDirectory") ?? request.WorkingDirectory ?? context.WorkingDirectory },
            _ => Array.Empty<string?>()
        };

        foreach (var path in pathsToCheck)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var resolvedPath = PermissionRuleHelpers.ResolvePath(pathService, context, path);
            if (!PermissionRuleHelpers.IsWithinWorkspace(pathService, context, resolvedPath))
            {
                return Task.FromResult(PermissionRuleResult.Deny($"Request for '{request.ToolName}' escapes the workspace boundary."));
            }
        }

        return Task.FromResult(PermissionRuleResult.Abstain());
    }
}
