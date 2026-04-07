namespace SharpClaw.Code.Infrastructure.Abstractions;

/// <summary>
/// Provides file-system operations used by storage components.
/// </summary>
public interface IFileSystem
{
    /// <summary>
    /// Acquires an exclusive lock on a lock file for cross-process coordination.
    /// The caller must dispose the returned handle (sync or async) to release the lock.
    /// </summary>
    /// <param name="lockFilePath">Path to the lock file (parent directories are created).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A disposable that releases the lock.</returns>
    Task<IAsyncDisposable> AcquireExclusiveFileLockAsync(string lockFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a directory if it does not already exist.
    /// </summary>
    /// <param name="path">The directory path.</param>
    void CreateDirectory(string path);

    /// <summary>
    /// Determines whether a file exists.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns><see langword="true"/> when the file exists; otherwise <see langword="false"/>.</returns>
    bool FileExists(string path);

    /// <summary>
    /// Determines whether a directory exists.
    /// </summary>
    /// <param name="path">The directory path.</param>
    /// <returns><see langword="true"/> when the directory exists; otherwise <see langword="false"/>.</returns>
    bool DirectoryExists(string path);

    /// <summary>
    /// Enumerates child directories.
    /// </summary>
    /// <param name="path">The parent path.</param>
    /// <returns>The child directories.</returns>
    IEnumerable<string> EnumerateDirectories(string path);

    /// <summary>
    /// Enumerates child files matching a pattern.
    /// </summary>
    /// <param name="path">The parent path.</param>
    /// <param name="searchPattern">The search pattern.</param>
    /// <returns>The matching file paths.</returns>
    IEnumerable<string> EnumerateFiles(string path, string searchPattern);

    /// <summary>
    /// Reads all text from a file if it exists.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The file content, or <see langword="null"/> when the file does not exist.</returns>
    Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Reads all lines from a file if it exists.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The file lines, or an empty array when the file does not exist.</returns>
    Task<string[]> ReadAllLinesIfExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Writes all text to a file, replacing any existing content.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="content">The content to write.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Copies a file to a destination path, creating parent directories as needed. Replaces the destination if present.
    /// </summary>
    /// <param name="sourcePath">Source file path.</param>
    /// <param name="destinationPath">Destination file path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken);

    /// <summary>
    /// Appends a line of text to a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="line">The line to append.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    Task AppendLineAsync(string path, string line, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a directory and all of its contents if the directory exists.
    /// </summary>
    /// <param name="path">The directory path.</param>
    void DeleteDirectoryRecursive(string path);

    /// <summary>
    /// Deletes a file if it exists.
    /// </summary>
    /// <param name="path">The file path.</param>
    void TryDeleteFile(string path);
}
