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

        var standardOutput = new BoundedOutputReader(process.StandardOutput);
        var standardError = new BoundedOutputReader(process.StandardError);
        using var readerCancellation = CancellationTokenSource.CreateLinkedTokenSource(processToken);
        var standardOutputTask = standardOutput.ReadToEndAsync(readerCancellation.Token);
        var standardErrorTask = standardError.ReadToEndAsync(readerCancellation.Token);
        try
        {
            await process.WaitForExitAsync(processToken).ConfigureAwait(false);
            try
            {
                // A background grandchild can inherit the pipes and keep them open after the shell exits.
                await Task.WhenAll(standardOutputTask, standardErrorTask)
                    .WaitAsync(PostExitDrainGrace, processToken)
                    .ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                readerCancellation.Cancel();
                await ObserveAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            }

            var completedAtUtc = systemClock.UtcNow;
            return new ProcessRunResult(process.ExitCode, standardOutput.GetText(), standardError.GetText(), startedAtUtc, completedAtUtc);
        }
        catch (Exception exception)
        {
            timeout.Cancel();
            TryKillProcessTree(process);
            await ObserveAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            if (!cancellationToken.IsCancellationRequested && exception is OperationCanceledException)
            {
                throw new TimeoutException("Process exceeded its execution deadline.", exception);
            }
            throw;
        }
    }

    /// <summary>Characters retained per output stream; output beyond this keeps its head and tail.</summary>
    public const int MaxRetainedCharacters = 1024 * 1024;

    private static readonly TimeSpan PostExitDrainGrace = TimeSpan.FromSeconds(2);

    private static async Task ObserveAsync(Task first, Task second)
    {
        try { await Task.WhenAll(first, second).ConfigureAwait(false); }
        catch (Exception) { /* Observe both reader failures before disposing the process. */ }
    }

    /// <summary>
    /// Drains a process stream with bounded memory, keeping the first and last halves of the retained budget.
    /// </summary>
    private sealed class BoundedOutputReader(StreamReader reader)
    {
        private const int HalfBudget = MaxRetainedCharacters / 2;
        private readonly System.Text.StringBuilder head = new();
        private readonly char[] tail = new char[HalfBudget];
        private int tailStart;
        private int tailLength;
        private long droppedCharacters;
        private readonly object gate = new();

        public async Task ReadToEndAsync(CancellationToken cancellationToken)
        {
            var buffer = new char[4096];
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (count == 0) return;
                lock (gate) Append(buffer.AsSpan(0, count));
            }
        }

        public string GetText()
        {
            lock (gate)
            {
                if (tailLength == 0) return head.ToString();
                var builder = new System.Text.StringBuilder(head.Length + tailLength + 96);
                builder.Append(head);
                if (droppedCharacters > 0)
                {
                    builder.Append($"\n[... {droppedCharacters} characters of output omitted ...]\n");
                }

                var firstSegment = Math.Min(tailLength, tail.Length - tailStart);
                builder.Append(tail, tailStart, firstSegment);
                builder.Append(tail, 0, tailLength - firstSegment);
                return builder.ToString();
            }
        }

        private void Append(ReadOnlySpan<char> chunk)
        {
            var headRoom = HalfBudget - head.Length;
            if (headRoom > 0)
            {
                var taken = Math.Min(headRoom, chunk.Length);
                head.Append(chunk[..taken]);
                chunk = chunk[taken..];
            }

            if (chunk.IsEmpty) return;
            if (chunk.Length >= tail.Length)
            {
                droppedCharacters += tailLength + (chunk.Length - tail.Length);
                chunk[^tail.Length..].CopyTo(tail);
                tailStart = 0;
                tailLength = tail.Length;
                return;
            }

            var overflow = Math.Max(0, tailLength + chunk.Length - tail.Length);
            droppedCharacters += overflow;
            tailStart = (tailStart + overflow) % tail.Length;
            tailLength -= overflow;
            var writeAt = (tailStart + tailLength) % tail.Length;
            var firstSegment = Math.Min(chunk.Length, tail.Length - writeAt);
            chunk[..firstSegment].CopyTo(tail.AsSpan(writeAt));
            chunk[firstSegment..].CopyTo(tail);
            tailLength += chunk.Length;
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
