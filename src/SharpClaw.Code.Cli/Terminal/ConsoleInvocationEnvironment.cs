using SharpClaw.Code.Commands;

namespace SharpClaw.Code.Cli.Terminal;

/// <summary>
/// Reads CLI invocation characteristics from the current process console streams.
/// </summary>
public sealed class ConsoleInvocationEnvironment : ICliInvocationEnvironment
{
    /// <inheritdoc />
    public bool IsInputRedirected => Console.IsInputRedirected;

    /// <inheritdoc />
    public bool IsOutputRedirected => Console.IsOutputRedirected;

    /// <inheritdoc />
    public async Task<string> ReadStandardInputToEndAsync(CancellationToken cancellationToken)
    {
        var readTask = Console.In.ReadToEndAsync();
        var completed = await Task.WhenAny(readTask, Task.Delay(Timeout.Infinite, cancellationToken)).ConfigureAwait(false);
        if (completed != readTask)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return await readTask.ConfigureAwait(false);
    }
}
