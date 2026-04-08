using SharpClaw.Code.Infrastructure.Abstractions;

namespace SharpClaw.Code.Infrastructure.Services;

/// <summary>
/// Uses <see cref="Path"/> and environment APIs for common path operations.
/// </summary>
public sealed class PathService : IPathService
{
    /// <inheritdoc />
    public string Combine(params string[] parts)
        => Path.Combine(parts);

    /// <inheritdoc />
    public string GetFullPath(string path)
        => Path.GetFullPath(path);

    /// <inheritdoc />
    public string GetCanonicalFullPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(root))
        {
            return fullPath;
        }

        var current = root;
        var relativeSegments = fullPath[root.Length..]
            .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in relativeSegments)
        {
            current = Path.Combine(current, segment);
            if (TryResolveExistingLink(current) is { } resolved)
            {
                current = string.Equals(resolved, current, StringComparison.Ordinal)
                    ? resolved
                    : GetCanonicalFullPath(resolved);
            }
        }

        return Path.GetFullPath(current);
    }

    /// <inheritdoc />
    public string GetCurrentDirectory()
        => Environment.CurrentDirectory;

    /// <inheritdoc />
    public string? GetFileName(string path)
        => Path.GetFileName(path);

    /// <inheritdoc />
    public string GetTempPath()
        => Path.GetTempPath();

    private static string? TryResolveExistingLink(string path)
    {
        FileSystemInfo? info = null;
        if (Directory.Exists(path))
        {
            info = new DirectoryInfo(path);
        }
        else if (File.Exists(path))
        {
            info = new FileInfo(path);
        }

        if (info is null)
        {
            return null;
        }

        if (info.LinkTarget is null)
        {
            return info.FullName;
        }

        var target = info.ResolveLinkTarget(returnFinalTarget: true);
        return target is null
            ? info.FullName
            : Path.GetFullPath(target.FullName);
    }
}
