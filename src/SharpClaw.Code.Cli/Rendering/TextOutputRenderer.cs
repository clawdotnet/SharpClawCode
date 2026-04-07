using SharpClaw.Code.Commands;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using Spectre.Console;

namespace SharpClaw.Code.Cli.Rendering;

/// <summary>
/// Renders command and prompt results as human-readable text.
/// </summary>
public sealed class TextOutputRenderer : IOutputRenderer
{
    /// <inheritdoc />
    public OutputFormat Format => OutputFormat.Text;

    /// <inheritdoc />
    public Task RenderCommandResultAsync(CommandResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (result.Succeeded)
        {
            AnsiConsole.MarkupLine($"[green]{Markup.Escape(result.Message)}[/]");
        }
        else
        {
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(result.Message)}[/]");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RenderTurnExecutionResultAsync(TurnExecutionResult result, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(result.FinalOutput))
        {
            AnsiConsole.WriteLine(result.FinalOutput);
        }
        else if (!string.IsNullOrWhiteSpace(result.Turn.Output))
        {
            AnsiConsole.WriteLine(result.Turn.Output);
        }
        else
        {
            AnsiConsole.MarkupLine("[yellow]Prompt completed with no output.[/]");
        }

        return Task.CompletedTask;
    }
}
