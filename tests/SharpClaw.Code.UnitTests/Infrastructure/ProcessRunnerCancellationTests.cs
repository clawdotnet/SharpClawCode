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
        // Cancel quickly; the shell command sleeps much longer so if the
        // process survived cancellation, the marker file would appear.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));

        var request = OperatingSystem.IsWindows()
            ? new ProcessRunRequest(
                "cmd.exe",
                ["/c", $@"ping -n 10 127.0.0.1 >NUL && echo ran>""{markerPath}"""],
                tempRoot,
                null)
            : new ProcessRunRequest(
                "/bin/sh",
                ["-lc", $"sleep 10; echo ran > '{markerPath}'"],
                tempRoot,
                null);

        var act = async () => await runner.RunAsync(request, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // The shell command sleeps 10s; wait just 1s — if the process was killed,
        // the marker cannot exist. This avoids the prior 3s wait.
        await Task.Delay(TimeSpan.FromSeconds(1));
        File.Exists(markerPath).Should().BeFalse();
    }
}
