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
            if (!OperatingSystem.IsWindows())
            {
                // Stage with owner-only access: the temporary file's group can differ from the target's, so
                // applying group/other bits before the content is in place could expose it to the wrong group.
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            await using (var stream = OperatingSystem.IsWindows() && existing.Exists
                ? CreateWithTargetAcl(tempPath, path)
                : new FileStream(tempPath, options))
            {
                await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                await writer.WriteAsync(content.AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (existing.Exists && !OperatingSystem.IsWindows())
            {
                // Restore the target's exact mode once the content is complete, just before the atomic rename.
                File.SetUnixFileMode(tempPath, existing.UnixFileMode);
            }
            else if (!OperatingSystem.IsWindows())
            {
                // New files get the conventional umask-filtered default instead of staying owner-only.
                File.SetUnixFileMode(tempPath, DefaultNewFileMode & ~CurrentUmask.Value);
            }

            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    // Re-check every attempt: a failed ReplaceFile can leave the target missing.
                    if (OperatingSystem.IsWindows() && File.Exists(path))
                    {
                        ReplaceWithRecovery(tempPath, path);
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
                    if (File.Exists(path))
                    {
                        File.Delete(tempPath);
                    }
                    else
                    {
                        // A failed replace (or a cancellation during recovery) left no target: the staged file is
                        // the only complete copy of the new content, so put it in place instead of deleting it.
                        File.Move(tempPath, path);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Leave the staged file for manual recovery rather than losing it.
                }
            }
        }
    }

    private const UnixFileMode DefaultNewFileMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.GroupWrite
        | UnixFileMode.OtherRead | UnixFileMode.OtherWrite;

    private static readonly Lazy<UnixFileMode> CurrentUmask = new(
        static () => OperatingSystem.IsWindows() ? UnixFileMode.None : ProbeUmask());

    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    private static UnixFileMode ProbeUmask()
    {
        // .NET exposes no umask API; create a probe file with mode 0666 and read back what the umask left.
        var probe = Path.Combine(Path.GetTempPath(), $".sc-umask-{Guid.NewGuid():N}");
        try
        {
            using (new FileStream(probe, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, UnixCreateMode = DefaultNewFileMode }))
            {
            }

            return DefaultNewFileMode & ~File.GetUnixFileMode(probe);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return UnixFileMode.GroupWrite | UnixFileMode.OtherWrite; // conventional 022
        }
        finally
        {
            try
            {
                File.Delete(probe);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>
    /// Windows replace that keeps the target's security descriptor and recovers ReplaceFile's documented
    /// partial-failure states: if the target ends up missing, the backup (original) or the staged replacement
    /// is moved back into place before the error propagates, so content is never lost.
    /// </summary>
    private static void ReplaceWithRecovery(string tempPath, string path)
    {
        var backupPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".", $".sc-{Guid.NewGuid():N}.bak");
        try
        {
            // Fail rather than silently replacing the target's security descriptor with a broader one.
            File.Replace(tempPath, path, backupPath, ignoreMetadataErrors: false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!File.Exists(path))
            {
                if (File.Exists(backupPath))
                {
                    File.Move(backupPath, path); // restore the original; the staged file stays for a retry
                }
                else if (File.Exists(tempPath))
                {
                    File.Move(tempPath, path); // the replacement is the only surviving copy
                    return;
                }
            }

            throw;
        }
        finally
        {
            if (File.Exists(backupPath) && File.Exists(path))
            {
                try
                {
                    File.Delete(backupPath);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                }
            }
        }
    }

    /// <summary>
    /// Creates the Windows staging file with the target's effective DACL (inherited entries made explicit) so
    /// staged content is never more widely readable than the file it replaces.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static FileStream CreateWithTargetAcl(string tempPath, string targetPath)
    {
        var security = new FileInfo(targetPath).GetAccessControl(System.Security.AccessControl.AccessControlSections.Access);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
        return new FileInfo(tempPath).Create(
            FileMode.CreateNew,
            System.Security.AccessControl.FileSystemRights.Write | System.Security.AccessControl.FileSystemRights.ReadAttributes
                | System.Security.AccessControl.FileSystemRights.Delete | System.Security.AccessControl.FileSystemRights.Synchronize,
            FileShare.None,
            4096,
            FileOptions.Asynchronous,
            security);
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

        // .NET's FileMode.Append does not use O_APPEND and FileShare is only advisory on Unix, so writers in other
        // processes are excluded with an exclusive sidecar lock around the torn-line check and the append.
        IAsyncDisposable? crossProcessLock = null;
        try
        {
            crossProcessLock = await AcquireExclusiveFileLockAsync(GetAppendLockPath(path), cancellationToken).ConfigureAwait(false);
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
            stream.Seek(0, SeekOrigin.End);

            // FileMode.Append opens with O_APPEND on Unix, so each write lands at the current end of file even
            // when other processes append concurrently; on Windows the write handle excludes other writers.
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (crossProcessLock is not null)
            {
                await crossProcessLock.DisposeAsync().ConfigureAwait(false);
            }

            gate.Release();
        }
    }

    private static string GetAppendLockPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return Path.Combine(Path.GetDirectoryName(fullPath) ?? ".", $".{Path.GetFileName(fullPath)}.lock");
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
