using System.Text;
using SharpClaw.Code.Infrastructure.Abstractions;

namespace SharpClaw.Code.Infrastructure.Services;

/// <summary>
/// Uses the local machine file system for storage operations.
/// </summary>
public sealed class LocalFileSystem : IFileSystem
{
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(50);
    private const int MaxLockRetries = 200; // ~10 seconds total at 50ms intervals
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, SemaphoreSlim> AppendGates =
        new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<IAsyncDisposable> AcquireExclusiveFileLockAsync(string lockFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lockFilePath);
        var directory = Path.GetDirectoryName(lockFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    lockFilePath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 4096,
                    FileOptions.Asynchronous);
                return new ExclusiveFileLock(stream);
            }
            catch (IOException) when (attempt < MaxLockRetries)
            {
                await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < MaxLockRetries)
            {
                await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public void CreateDirectory(string path)
        => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public bool FileExists(string path)
        => File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path)
        => Directory.Exists(path);

    /// <inheritdoc />
    public IEnumerable<string> EnumerateDirectories(string path)
        => Directory.Exists(path) ? Directory.EnumerateDirectories(path) : [];

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern)
        => Directory.Exists(path) ? Directory.EnumerateFiles(path, searchPattern) : [];

    /// <inheritdoc />
    public async Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException) when (attempt < MaxLockRetries)
            {
                await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < MaxLockRetries)
            {
                await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public async Task<string[]> ReadAllLinesIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(path))
            {
                return [];
            }

            try
            {
                return await File.ReadAllLinesAsync(path, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException) when (attempt < MaxLockRetries)
            {
                await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException) when (attempt < MaxLockRetries)
            {
                await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return WriteAllTextAtomicallyAsync(path, content, cancellationToken);
    }

    /// <summary>
    /// Writes to a sibling temporary file and renames it over the target so readers never observe
    /// a truncated or partially written file, even if the process dies mid-write.
    /// Symlinked targets are written in place so the link itself is preserved.
    /// </summary>
    private static async Task WriteAllTextAtomicallyAsync(string path, string content, CancellationToken cancellationToken)
    {
        var existing = new FileInfo(path);
        if (existing.Exists && existing.LinkTarget is not null)
        {
            await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
            return;
        }

        // Short, independent sibling name: appending to the target name could exceed the 255-char component limit.
        var directory = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var tempPath = Path.Combine(directory, $".sc-{Guid.NewGuid():N}.tmp");
        try
        {
            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous,
                BufferSize = 4096,
            };
            if (existing.Exists && !OperatingSystem.IsWindows())
            {
                // Create with the target's own mode so restricted content is never briefly readable by others.
                options.UnixCreateMode = existing.UnixFileMode;
            }

            await using (var stream = new FileStream(tempPath, options))
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (existing.Exists && !OperatingSystem.IsWindows())
            {
                // The umask can only have narrowed the creation mode; restore the exact original mode.
                File.SetUnixFileMode(tempPath, existing.UnixFileMode);
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (existing.Exists && OperatingSystem.IsWindows())
                    {
                        // ReplaceFile keeps the target's security descriptor (file-specific DACL) and attributes.
                        File.Replace(tempPath, path, destinationBackupFileName: null, ignoreMetadataErrors: true);
                    }
                    else
                    {
                        File.Move(tempPath, path, overwrite: true);
                    }

                    return;
                }
                catch (Exception exception) when ((exception is IOException or UnauthorizedAccessException) && attempt < MaxLockRetries)
                {
                    // Windows can briefly refuse the replace while a reader holds the target open.
                    await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Ignore cleanup failures for the temporary file.
                }
            }
        }
    }

    /// <inheritdoc />
    public async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        var destDir = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrWhiteSpace(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        await using (var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await using var dest = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await source.CopyToAsync(dest, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task AppendLineAsync(string path, string line, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var gate = AppendGates.GetOrAdd(Path.GetFullPath(path), static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var payload = new StringBuilder(line.Length + 2);
            if (await EndsWithPartialLineAsync(path, cancellationToken).ConfigureAwait(false))
            {
                // A previous writer crashed mid-line: terminate that fragment so this record stays parseable.
                payload.Append('\n');
            }

            payload.Append(line).Append('\n');
            var bytes = Utf8NoBom.GetBytes(payload.ToString());

            // Only opening is retried (sharing violations on Windows). Once bytes may have reached the file, a
            // failure propagates instead of being retried, so a record is never written twice.
            await using var stream = await OpenForAppendAsync(path, cancellationToken).ConfigureAwait(false);

            // FileMode.Append opens with O_APPEND on Unix, so each write lands at the current end of file even
            // when other processes append concurrently; on Windows the write handle excludes other writers.
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private static async Task<FileStream> OpenForAppendAsync(string path, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.Asynchronous);
            }
            catch (IOException) when (attempt < MaxLockRetries)
            {
                await Task.Delay(LockRetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task<bool> EndsWithPartialLineAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous);
            if (reader.Length == 0)
            {
                return false;
            }

            reader.Seek(-1, SeekOrigin.End);
            var lastByte = new byte[1];
            return await reader.ReadAsync(lastByte, cancellationToken).ConfigureAwait(false) == 1 && lastByte[0] != (byte)'\n';
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public void DeleteDirectoryRecursive(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <inheritdoc />
    public void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }
}
