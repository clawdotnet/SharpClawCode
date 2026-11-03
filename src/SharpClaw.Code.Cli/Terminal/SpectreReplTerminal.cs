using SharpClaw.Code.Commands;
using Spectre.Console;

namespace SharpClaw.Code.Cli.Terminal;

/// <summary>
/// Implements REPL terminal interaction using Spectre.Console and the system console.
/// </summary>
public sealed class SpectreReplTerminal : IReplTerminal
{
    private readonly SemaphoreSlim readGate = new(1, 1);
    private Task<string?>? pendingRead;
    /// <inheritdoc />
    public event ConsoleCancelEventHandler? CancelKeyPress
    {
        add => Console.CancelKeyPress += value;
        remove => Console.CancelKeyPress -= value;
    }

    /// <inheritdoc />
    public async ValueTask<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken)
    {
        await readGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AnsiConsole.Markup($"[grey]{Markup.Escape(prompt)}[/]");
            // Console.ReadLine cannot be cancelled. Retain the same pending read across
            // cancelled waits so a later REPL run never starts a competing reader.
            pendingRead ??= Task.Run(Console.ReadLine);
            var line = await pendingRead.WaitAsync(cancellationToken).ConfigureAwait(false);
            pendingRead = null;
            return line;
        }
        finally { readGate.Release(); }
    }

    /// <inheritdoc />
    public void WriteInfo(string message)
        => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]");

    /// <inheritdoc />
    public void WriteError(string message)
        => AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");

    /// <inheritdoc />
    public void ClearScreen()
        => AnsiConsole.Clear();
}
