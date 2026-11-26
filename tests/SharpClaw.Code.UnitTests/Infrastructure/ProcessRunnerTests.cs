using System.Diagnostics;
using FluentAssertions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Infrastructure.Services;

namespace SharpClaw.Code.UnitTests.Infrastructure;

/// <summary>
/// Verifies process execution does not hang on children that inherit the output pipes.
/// </summary>
public sealed class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_should_return_when_shell_exits_even_if_a_background_child_holds_the_pipes()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var runner = new ProcessRunner(new SystemClock());
        var stopwatch = Stopwatch.StartNew();

        var result = await runner.RunAsync(
            new ProcessRunRequest("/bin/sh", ["-c", "sleep 30 & echo hi"], null, null),
            CancellationToken.None);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(15));
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("hi");
    }
}
