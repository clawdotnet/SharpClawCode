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
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(request.ExecutionTimeout ?? TimeSpan.FromMinutes(2));
        var processToken = timeout.Token;

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

        var standardOutputTask = ReadBoundedAsync(process.StandardOutput, timeout, processToken);
        var standardErrorTask = ReadBoundedAsync(process.StandardError, timeout, processToken);
        try
        {
            await process.WaitForExitAsync(processToken).ConfigureAwait(false);
            var standardOutput = await standardOutputTask.ConfigureAwait(false);
            var standardError = await standardErrorTask.ConfigureAwait(false);
            var completedAtUtc = systemClock.UtcNow;

            return new ProcessRunResult(process.ExitCode, standardOutput, standardError, startedAtUtc, completedAtUtc);
        }
        catch (Exception exception)
        {
            timeout.Cancel();
            TryKillProcessTree(process);
            try { await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false); }
            catch (Exception) { /* Observe both reader failures before disposing the process. */ }
            if (!cancellationToken.IsCancellationRequested && exception is OperationCanceledException)
            {
                var outputFailure = (standardOutputTask.Exception?.InnerExceptions.AsEnumerable() ?? [])
                    .Concat(standardErrorTask.Exception?.InnerExceptions.AsEnumerable() ?? []).OfType<IOException>().FirstOrDefault();
                if (outputFailure is not null) throw outputFailure;
                throw new TimeoutException("Process exceeded its execution deadline.", exception);
            }
            throw;
        }
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationTokenSource timeout, CancellationToken cancellationToken)
    {
        const int maxCharacters = 1024 * 1024;
        var builder = new System.Text.StringBuilder();
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0) return builder.ToString();
            if (builder.Length + count > maxCharacters)
            {
                timeout.Cancel();
                throw new IOException("Process output exceeded the 1 MiB character limit.");
            }
            builder.Append(buffer, 0, count);
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
