using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;

namespace SharpClaw.Code.IntegrationTests.Fixtures;

internal sealed class FaultingFileSystem : IFileSystem
{
    private readonly LocalFileSystem inner = new();
    internal Action<int, string, string>? BeforeWrite { get; set; }
    internal Action<string>? BeforeRead { get; set; }
    private int writes;
    public Task<IAsyncDisposable> AcquireExclusiveFileLockAsync(string path, CancellationToken cancellationToken = default) => inner.AcquireExclusiveFileLockAsync(path, cancellationToken);
    public void CreateDirectory(string path) => inner.CreateDirectory(path);
    public bool FileExists(string path) => inner.FileExists(path);
    public bool DirectoryExists(string path) => inner.DirectoryExists(path);
    public IEnumerable<string> EnumerateDirectories(string path) => inner.EnumerateDirectories(path);
    public IEnumerable<string> EnumerateFiles(string path, string pattern) => inner.EnumerateFiles(path, pattern);
    public Task<string?> ReadAllTextIfExistsAsync(string path, CancellationToken token) { BeforeRead?.Invoke(path); return inner.ReadAllTextIfExistsAsync(path, token); }
    public Task<string[]> ReadAllLinesIfExistsAsync(string path, CancellationToken token) => inner.ReadAllLinesIfExistsAsync(path, token);
    public Task WriteAllTextAsync(string path, string content, CancellationToken token)
    {
        BeforeWrite?.Invoke(++writes, path, content);
        return inner.WriteAllTextAsync(path, content, token);
    }
    public Task CopyFileAsync(string source, string destination, CancellationToken token) => inner.CopyFileAsync(source, destination, token);
    public Task AppendLineAsync(string path, string line, CancellationToken token) => inner.AppendLineAsync(path, line, token);
    public void DeleteDirectoryRecursive(string path) => inner.DeleteDirectoryRecursive(path);
    public void TryDeleteFile(string path) => inner.TryDeleteFile(path);
}
