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
    public string GetCurrentDirectory()
        => Environment.CurrentDirectory;

    /// <inheritdoc />
    public string? GetFileName(string path)
        => Path.GetFileName(path);

    /// <inheritdoc />
    public string GetTempPath()
        => Path.GetTempPath();
}
