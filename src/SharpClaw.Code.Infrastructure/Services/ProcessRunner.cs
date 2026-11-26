using System.Diagnostics;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;

namespace SharpClaw.Code.Infrastructure.Services;

/// <summary>
/// Runs external processes using the local machine process APIs.
/// </summary>
public sealed class ProcessRunner(ISystemClock systemClock) : IProcessRunner
{
    /// <inheritdoc />
    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);

        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        if (request.EnvironmentVariables is not null)
        {
            foreach (var pair in request.EnvironmentVariables)
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var startedAtUtc = systemClock.UtcNow;
        process.Start();

        var standardOutput = new System.Text.StringBuilder();
        var standardError = new System.Text.StringBuilder();
        using var readerCts = new CancellationTokenSource();
        var standardOutputTask = PumpAsync(process.StandardOutput, standardOutput, readerCts.Token);
        var standardErrorTask = PumpAsync(process.StandardError, standardError, readerCts.Token);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            // A backgrounded child (e.g. `server &`) inherits the pipes and keeps them open after the shell exits.
            // Drain what is available, but do not block the caller until that child exits.
            try
            {
                await Task.WhenAll(standardOutputTask, standardErrorTask)
                    .WaitAsync(OutputDrainGracePeriod, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                readerCts.Cancel();
            }

            var completedAtUtc = systemClock.UtcNow;
            return new ProcessRunResult(process.ExitCode, Snapshot(standardOutput), Snapshot(standardError), startedAtUtc, completedAtUtc);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            readerCts.Cancel();
            throw;
        }
    }

    private static readonly TimeSpan OutputDrainGracePeriod = TimeSpan.FromSeconds(2);

    private static async Task PumpAsync(StreamReader reader, System.Text.StringBuilder sink, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                lock (sink)
                {
                    sink.Append(buffer, 0, read);
                }
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or ObjectDisposedException or IOException)
        {
            // Stopped draining: the caller takes whatever was captured so far.
        }
    }

    private static string Snapshot(System.Text.StringBuilder sink)
    {
        lock (sink)
        {
            return sink.ToString();
        }
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
    }
}
