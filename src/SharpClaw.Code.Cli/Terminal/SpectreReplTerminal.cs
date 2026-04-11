using SharpClaw.Code.Commands;
using Spectre.Console;

namespace SharpClaw.Code.Cli.Terminal;

/// <summary>
/// Implements REPL terminal interaction using Spectre.Console and the system console.
/// </summary>
public sealed class SpectreReplTerminal : IReplTerminal
{
    /// <inheritdoc />
    public event ConsoleCancelEventHandler? CancelKeyPress
    {
        add => Console.CancelKeyPress += value;
        remove => Console.CancelKeyPress -= value;
    }

    /// <inheritdoc />
    public async ValueTask<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken)
    {
        AnsiConsole.Markup($"[grey]{Markup.Escape(prompt)}[/]");
        var readTask = Task.Run(Console.ReadLine, cancellationToken);
        var tcs = new TaskCompletionSource<string?>();
        await using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        var completed = await Task.WhenAny(readTask, tcs.Task).ConfigureAwait(false);
        return await completed.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void WriteInfo(string message)
        => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]");

    /// <inheritdoc />
    public void WriteError(string message)
        => AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
}
