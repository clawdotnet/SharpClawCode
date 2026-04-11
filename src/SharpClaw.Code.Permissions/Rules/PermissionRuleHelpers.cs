using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Rules;

internal static class PermissionRuleHelpers
{
    public static bool IsTrusted(IReadOnlyCollection<string>? trustedNames, string? candidate)
        => !string.IsNullOrWhiteSpace(candidate)
           && trustedNames is not null
           && trustedNames.Contains(candidate, StringComparer.OrdinalIgnoreCase);

    public static string ResolvePath(IPathService pathService, PermissionEvaluationContext context, string? candidatePath)
    {
        ArgumentNullException.ThrowIfNull(pathService);

        var workspaceRoot = pathService.GetCanonicalFullPath(context.WorkspaceRoot);
        var value = string.IsNullOrWhiteSpace(candidatePath)
            ? context.WorkingDirectory
            : candidatePath;

        var fullPath = Path.IsPathRooted(value)
            ? pathService.GetCanonicalFullPath(value)
            : pathService.GetCanonicalFullPath(Path.Combine(workspaceRoot, value));

        return fullPath;
    }

    public static bool IsWithinWorkspace(IPathService pathService, PermissionEvaluationContext context, string fullPath)
    {
        ArgumentNullException.ThrowIfNull(pathService);

        var workspaceRoot = pathService.GetCanonicalFullPath(context.WorkspaceRoot);
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
        try
        {
            using var document = JsonDocument.Parse(request.ArgumentsJson);
            return document.RootElement.TryGetProperty(propertyName, out var property)
                && property.ValueKind is JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
