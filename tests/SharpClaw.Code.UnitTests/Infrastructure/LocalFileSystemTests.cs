using FluentAssertions;
using SharpClaw.Code.Infrastructure.Services;

namespace SharpClaw.Code.UnitTests.Infrastructure;

/// <summary>
/// Verifies atomic writes keep file permissions and append handling stays line-safe.
/// </summary>
public sealed class LocalFileSystemTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"sharpclaw-lfs-{Guid.NewGuid():N}");
    private readonly LocalFileSystem fileSystem = new();

    public LocalFileSystemTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task WriteAllTextAsync_should_keep_a_restrictive_unix_mode_and_leave_no_temp_files()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(root, new string('n', 230) + ".json");
        await File.WriteAllTextAsync(path, "old");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        await fileSystem.WriteAllTextAsync(path, "new", CancellationToken.None);

        (await File.ReadAllTextAsync(path)).Should().Be("new");
        File.GetUnixFileMode(path).Should().Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
        Directory.GetFiles(root).Should().ContainSingle("the temporary sibling must be renamed over the target");
    }

    [Fact]
    public async Task AppendLineAsync_should_terminate_a_torn_line_before_appending()
    {
        var path = Path.Combine(root, "events.ndjson");
        await File.WriteAllTextAsync(path, "{\"complete\":1}\n{\"torn\":");

        await fileSystem.AppendLineAsync(path, "{\"next\":2}", CancellationToken.None);

        (await File.ReadAllLinesAsync(path)).Should().Equal("{\"complete\":1}", "{\"torn\":", "{\"next\":2}");
    }

    [Fact]
    public async Task AppendLineAsync_should_wait_for_a_writer_in_another_process()
    {
        var path = Path.Combine(root, "events.ndjson");
        await File.WriteAllTextAsync(path, "{\"first\":1}\n");

        // Simulate another process holding the cross-process append lock.
        var lockPath = Path.Combine(root, ".events.ndjson.lock");
        var otherProcess = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var append = fileSystem.AppendLineAsync(path, "{\"second\":2}", CancellationToken.None);

        await Task.Delay(300);
        append.IsCompleted.Should().BeFalse("the append must not interleave with another process's write");
        (await File.ReadAllLinesAsync(path)).Should().Equal("{\"first\":1}");

        await otherProcess.DisposeAsync();
        await append.WaitAsync(TimeSpan.FromSeconds(10));
        (await File.ReadAllLinesAsync(path)).Should().Equal("{\"first\":1}", "{\"second\":2}");
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
