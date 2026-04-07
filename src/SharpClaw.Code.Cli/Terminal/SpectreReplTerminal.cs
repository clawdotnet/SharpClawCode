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
    public ValueTask<string?> ReadLineAsync(string prompt, CancellationToken cancellationToken)
    {
        AnsiConsole.Markup($"[grey]{Markup.Escape(prompt)}[/]");
        return ValueTask.FromResult(Console.ReadLine());
    }

    /// <inheritdoc />
    public void WriteInfo(string message)
        => AnsiConsole.MarkupLine($"[grey]{Markup.Escape(message)}[/]");

    /// <inheritdoc />
    public void WriteError(string message)
        => AnsiConsole.MarkupLine($"[red]{Markup.Escape(message)}[/]");
}
