using System.Text.Json;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Rules;

internal static class PermissionRuleHelpers
{
    public static bool IsTrusted(IReadOnlyCollection<string>? trustedNames, string? candidate)
        => !string.IsNullOrWhiteSpace(candidate)
           && trustedNames is not null
           && trustedNames.Contains(candidate, StringComparer.OrdinalIgnoreCase);

    public static string ResolvePath(PermissionEvaluationContext context, string? candidatePath)
    {
        var workspaceRoot = Path.GetFullPath(context.WorkspaceRoot);
        var value = string.IsNullOrWhiteSpace(candidatePath)
            ? context.WorkingDirectory
            : candidatePath;

        var fullPath = Path.IsPathRooted(value)
            ? Path.GetFullPath(value)
            : Path.GetFullPath(Path.Combine(workspaceRoot, value));

        return fullPath;
    }

    public static bool IsWithinWorkspace(PermissionEvaluationContext context, string fullPath)
    {
        var workspaceRoot = Path.GetFullPath(context.WorkspaceRoot);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(workspaceRoot, fullPath, comparison))
        {
            return true;
        }

        var prefix = workspaceRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), comparison)
            ? workspaceRoot
            : workspaceRoot + Path.DirectorySeparatorChar;

        return fullPath.StartsWith(prefix, comparison);
    }

    public static string? TryReadJsonString(ToolExecutionRequest request, string propertyName)
    {
        using var document = JsonDocument.Parse(request.ArgumentsJson);
        return document.RootElement.TryGetProperty(propertyName, out var property)
            && property.ValueKind is JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
