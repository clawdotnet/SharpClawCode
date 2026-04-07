using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.Utilities;

/// <summary>
/// Resolves tool paths relative to the workspace root and enforces workspace containment.
/// </summary>
/// <param name="pathService">Path normalization service.</param>
public sealed class WorkspacePathResolver(IPathService pathService)
{
    private readonly StringComparison pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    /// <summary>Absolute, normalized workspace root from the tool context.</summary>
    public string ResolveWorkspaceRoot(ToolExecutionContext context)
        => pathService.GetFullPath(context.WorkspaceRoot);

    /// <summary>
    /// Resolves a user-relative or absolute path to a full path under (or equal to) the workspace root.
    /// </summary>
    public string ResolvePath(ToolExecutionContext context, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var workspaceRoot = ResolveWorkspaceRoot(context);
        var fullPath = Path.IsPathRooted(path)
            ? pathService.GetFullPath(path)
            : pathService.GetFullPath(pathService.Combine(workspaceRoot, path));

        EnsureWithinWorkspace(workspaceRoot, fullPath);
        return fullPath;
    }

    /// <summary>
    /// Resolves the effective working directory for a tool invocation, defaulting to the context working directory.
    /// </summary>
    public string ResolveWorkingDirectory(ToolExecutionContext context, string? workingDirectory)
    {
        var candidate = string.IsNullOrWhiteSpace(workingDirectory)
            ? context.WorkingDirectory
            : workingDirectory;

        return ResolvePath(context, candidate);
    }

    /// <summary>Produces a forward-slash relative path from the workspace root.</summary>
    public string ToRelativePath(ToolExecutionContext context, string fullPath)
    {
        var workspaceRoot = ResolveWorkspaceRoot(context);
        var relativePath = Path.GetRelativePath(workspaceRoot, fullPath);
        return relativePath.Replace(Path.DirectorySeparatorChar, '/');
    }

    private void EnsureWithinWorkspace(string workspaceRoot, string fullPath)
    {
        if (string.Equals(workspaceRoot, fullPath, pathComparison))
        {
            return;
        }

        var prefix = workspaceRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), pathComparison)
            ? workspaceRoot
            : workspaceRoot + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(prefix, pathComparison))
        {
            throw new InvalidOperationException($"Resolved path '{fullPath}' escapes the workspace boundary '{workspaceRoot}'.");
        }
    }
}
