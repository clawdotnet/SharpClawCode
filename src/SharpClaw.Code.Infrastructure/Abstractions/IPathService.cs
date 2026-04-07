namespace SharpClaw.Code.Infrastructure.Abstractions;

/// <summary>
/// Provides practical path operations used by runtime and storage code.
/// </summary>
public interface IPathService
{
    /// <summary>
    /// Combines path segments into a single path.
    /// </summary>
    /// <param name="parts">The path segments to combine.</param>
    /// <returns>The combined path.</returns>
    string Combine(params string[] parts);

    /// <summary>
    /// Gets a normalized full path.
    /// </summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized full path.</returns>
    string GetFullPath(string path);

    /// <summary>
    /// Gets the current working directory.
    /// </summary>
    /// <returns>The current working directory.</returns>
    string GetCurrentDirectory();

    /// <summary>
    /// Gets the file or directory name from a path.
    /// </summary>
    /// <param name="path">The path to inspect.</param>
    /// <returns>The final path segment.</returns>
    string? GetFileName(string path);

    /// <summary>
    /// Gets the path of the temporary folder for the current user.
    /// </summary>
    /// <returns>The temp directory path.</returns>
    string GetTempPath();
}
