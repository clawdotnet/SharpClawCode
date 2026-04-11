using FluentAssertions;
using SharpClaw.Code.Infrastructure.Services;

namespace SharpClaw.Code.UnitTests.Infrastructure;

public sealed class LocalFileSystemExclusiveLockTests
{
    [Fact]
    public async Task AcquireExclusiveFileLockAsync_allows_second_acquire_after_dispose()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sharpclaw-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var lockPath = Path.Combine(tempDir, "test.lock");
        var fs = new LocalFileSystem();

        await using (await fs.AcquireExclusiveFileLockAsync(lockPath, CancellationToken.None))
        {
            File.Exists(lockPath).Should().BeTrue();
        }

        await using (await fs.AcquireExclusiveFileLockAsync(lockPath, CancellationToken.None))
        {
            File.Exists(lockPath).Should().BeTrue();
        }
    }

    [Fact]
    public async Task AcquireExclusiveFileLockAsync_second_holder_waits_until_first_releases()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "sharpclaw-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var lockPath = Path.Combine(tempDir, "contended.lock");
        var fs = new LocalFileSystem();

        await using var first = await fs.AcquireExclusiveFileLockAsync(lockPath, CancellationToken.None);
        var secondTask = Task.Run(async () => await fs.AcquireExclusiveFileLockAsync(lockPath, CancellationToken.None));

        // Give the second task time to start and attempt the lock.
        await Task.Delay(200);
        secondTask.IsCompleted.Should().BeFalse("the second lock holder should block while the first is held");

        await first.DisposeAsync();
        await using var second = await secondTask.WaitAsync(TimeSpan.FromSeconds(10));
        second.Should().NotBeNull();
    }
}
