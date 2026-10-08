using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Services;

/// <summary>Resolves deterministic, canonical .NET targets without evaluating MSBuild.</summary>
public sealed class DotNetWorkspaceTargetResolver(IFileSystem fileSystem, IPathService pathService) : IDotNetWorkspaceTargetResolver
{
    /// <inheritdoc />
    public Task<DotNetWorkspaceTarget> ResolveAsync(DotNetWorkspaceRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WorkspaceRoot);
        var root = pathService.GetCanonicalFullPath(request.WorkspaceRoot);
        if (!fileSystem.DirectoryExists(root))
        {
            return Task.FromResult(new DotNetWorkspaceTarget(DotNetWorkspaceStatus.NoTarget, root, null, [], "Workspace directory does not exist."));
        }

        if (!string.IsNullOrWhiteSpace(request.Target))
        {
            var target = pathService.GetCanonicalFullPath(Path.IsPathRooted(request.Target) ? request.Target : Path.Combine(root, request.Target));
            var valid = IsWithin(root, target) && IsSupported(target) && fileSystem.FileExists(target);
            return Task.FromResult(new DotNetWorkspaceTarget(valid ? DotNetWorkspaceStatus.Ready : DotNetWorkspaceStatus.InvalidTarget, root, valid ? target : null, [], valid ? null : "Target must be an existing .sln, .slnx, or .csproj within the workspace."));
        }

        var targets = fileSystem.EnumerateFiles(root, "*").Where(IsSupported)
            .Select(pathService.GetCanonicalFullPath).Where(path => IsWithin(root, path)).Order(StringComparer.Ordinal).ToArray();
        var solutions = targets.Where(path => !path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)).ToArray();
        var candidates = solutions.Length > 0 ? solutions : targets;
        return Task.FromResult(candidates.Length switch
        {
            1 => new DotNetWorkspaceTarget(DotNetWorkspaceStatus.Ready, root, candidates[0], []),
            0 => new DotNetWorkspaceTarget(DotNetWorkspaceStatus.NoTarget, root, null, [], "No top-level .NET solution or project was found."),
            _ => new DotNetWorkspaceTarget(DotNetWorkspaceStatus.AmbiguousTarget, root, null, candidates.Select(path => Relative(root, path)).ToArray(), "Specify a target; multiple plausible .NET targets were found.")
        });
    }

    internal static bool IsWithin(string root, string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return string.Equals(root, path, comparison) || path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison);
    }

    internal static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool IsSupported(string path) => Path.GetExtension(path).ToLowerInvariant() is ".sln" or ".slnx" or ".csproj";
}
