using SharpClaw.Code.Infrastructure.Abstractions;

namespace SharpClaw.Code.Infrastructure.Services;

/// <summary>Validates local package IDs and prevents package operations from escaping their catalog.</summary>
public static class LocalPackagePath
{
    /// <summary>Rejects IDs containing path syntax, including Windows path syntax on Unix.</summary>
    public static void ValidateId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (id is "." or ".." || id.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.')))
            throw new ArgumentException("Package IDs must contain only letters, digits, dots, hyphens, and underscores.", nameof(id));
    }

    /// <summary>Resolves a package directory and rejects links outside the catalog.</summary>
    public static string Resolve(IPathService paths, string root, string id, string? workspaceRoot = null)
    {
        ValidateId(id);
        var canonicalRoot = paths.GetCanonicalFullPath(root);
        var directory = paths.GetCanonicalFullPath(paths.Combine(root, id));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (workspaceRoot is not null)
        {
            var lexicalWorkspace = Path.TrimEndingDirectorySeparator(paths.GetFullPath(workspaceRoot)) + Path.DirectorySeparatorChar;
            var canonicalWorkspace = Path.TrimEndingDirectorySeparator(paths.GetCanonicalFullPath(workspaceRoot)) + Path.DirectorySeparatorChar;
            // External host storage is explicit; workspace-local catalogs must remain in their workspace.
            if (paths.GetFullPath(root).StartsWith(lexicalWorkspace, comparison)
                && !canonicalRoot.StartsWith(canonicalWorkspace, comparison))
                throw new ArgumentException("Package catalog escapes the workspace.", nameof(root));
        }
        if (!directory.StartsWith(Path.TrimEndingDirectorySeparator(canonicalRoot) + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Package directory escapes the catalog.", nameof(id));
        return directory;
    }
}
