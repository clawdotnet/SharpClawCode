using FluentAssertions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Infrastructure.Services;

namespace SharpClaw.Code.UnitTests.Infrastructure;

/// <summary>
/// Verifies process cancellation tears down the spawned process tree.
/// </summary>
public sealed class ProcessRunnerCancellationTests
{
    /// <summary>
    /// Ensures a canceled process does not continue long enough to produce delayed output.
    /// </summary>
    [Fact]
    public async Task RunAsync_should_kill_process_when_canceled()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "sharpclaw-process-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var markerPath = Path.Combine(tempRoot, "marker.txt");
        var runner = new ProcessRunner(new SystemClock());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var request = OperatingSystem.IsWindows()
            ? new ProcessRunRequest(
                "cmd.exe",
                ["/c", $@"ping -n 4 127.0.0.1 >NUL && echo ran>""{markerPath}"""],
                tempRoot,
                null)
            : new ProcessRunRequest(
                "/bin/sh",
                ["-lc", $"sleep 2; echo ran > '{markerPath}'"],
                tempRoot,
                null);

        var act = async () => await runner.RunAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await Task.Delay(TimeSpan.FromSeconds(3));
        File.Exists(markerPath).Should().BeFalse();
    }
}
