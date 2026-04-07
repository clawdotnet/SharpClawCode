using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Rules;

/// <summary>
/// Requires explicit approval for dangerous shell command patterns unless bypass is explicitly enabled.
/// </summary>
public sealed partial class DangerousShellPatternRule : IPermissionRule
{
    /// <inheritdoc />
    public Task<PermissionRuleResult> EvaluateAsync(
        ToolExecutionRequest request,
        PermissionEvaluationContext context,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.ToolName, "bash", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(PermissionRuleResult.Abstain());
        }

        var command = PermissionRuleHelpers.TryReadJsonString(request, "command");
        if (string.IsNullOrWhiteSpace(command) || context.AllowDangerousBypass)
        {
            return Task.FromResult(PermissionRuleResult.Abstain());
        }

        return IsDangerous(command)
            ? Task.FromResult(PermissionRuleResult.RequireApproval($"The shell command '{command}' matches a dangerous command pattern.", canRememberApproval: false))
            : Task.FromResult(PermissionRuleResult.Abstain());
    }

    private static bool IsDangerous(string command)
    {
        var normalized = command.Trim().ToLowerInvariant();
        return normalized.Contains("rm -rf /", StringComparison.Ordinal)
               || normalized.Contains("rm -rf .", StringComparison.Ordinal)
               || normalized.Contains("rm -rf ..", StringComparison.Ordinal)
               || normalized.Contains("sudo ", StringComparison.Ordinal)
               || normalized.Contains("mkfs", StringComparison.Ordinal)
               || normalized.Contains("dd if=", StringComparison.Ordinal)
               || normalized.StartsWith("shutdown", StringComparison.Ordinal)
               || normalized.StartsWith("reboot", StringComparison.Ordinal);
    }
}
