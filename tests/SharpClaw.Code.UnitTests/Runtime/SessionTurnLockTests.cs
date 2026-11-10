using FluentAssertions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Runtime.Orchestration;
using SharpClaw.Code.UnitTests.Support;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>
/// Verifies the per-session lock shared by prompt turns and session-mutating commands.
/// </summary>
public sealed class SessionTurnLockTests
{
    [Fact]
    public async Task Second_holder_waits_until_the_first_releases()
    {
        var workspace = CreateWorkspace();
        var turnLock = new SessionTurnLock(new LocalFileSystem(), TestRuntimeStorageResolver.Create(workspace));
        var first = await turnLock.AcquireAsync(workspace, "session-a", CancellationToken.None);

        var second = turnLock.AcquireAsync(workspace, "session-a", CancellationToken.None);
        await Task.Delay(200);
        second.IsCompleted.Should().BeFalse();

        await first.DisposeAsync();
        await using var acquired = await second.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Different_sessions_do_not_block_each_other()
    {
        var workspace = CreateWorkspace();
        var turnLock = new SessionTurnLock(new LocalFileSystem(), TestRuntimeStorageResolver.Create(workspace));
        await using var first = await turnLock.AcquireAsync(workspace, "session-a", CancellationToken.None);

        await using var other = await turnLock.AcquireAsync(workspace, "session-b", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Cancelled_wait_does_not_leak_the_mutex()
    {
        var workspace = CreateWorkspace();
        var turnLock = new SessionTurnLock(new LocalFileSystem(), TestRuntimeStorageResolver.Create(workspace));
        var first = await turnLock.AcquireAsync(workspace, "session-a", CancellationToken.None);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await FluentActions.Awaiting(() => turnLock.AcquireAsync(workspace, "session-a", cancel.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        await first.DisposeAsync();
        await first.DisposeAsync(); // Idempotent: a double dispose must not over-release the mutex.
        await using var again = await turnLock.AcquireAsync(workspace, "session-a", CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        var blocked = turnLock.AcquireAsync(workspace, "session-a", CancellationToken.None);
        await Task.Delay(200);
        blocked.IsCompleted.Should().BeFalse();
        await again.DisposeAsync();
        await using var last = await blocked.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static string CreateWorkspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), "sharpclaw-turn-lock-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspace);
        return workspace;
    }
}
