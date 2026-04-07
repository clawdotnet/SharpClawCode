using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Implements the REPL help slash command.
/// </summary>
public sealed class HelpSlashCommandHandler(ICommandRegistry commandRegistry, OutputRendererDispatcher outputRendererDispatcher) : ISlashCommandHandler
{
    /// <inheritdoc />
    public string CommandName => "help";

    /// <inheritdoc />
    public string Description => "Lists available REPL slash commands.";

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var message = string.Join(Environment.NewLine, commandRegistry
            .GetSlashCommandHandlers()
            .Select(handler => $"/{handler.CommandName} - {handler.Description}"));

        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(
                Succeeded: true,
                ExitCode: 0,
                OutputFormat: OutputFormat.Text,
                Message: message,
                DataJson: null),
            context.OutputFormat,
            cancellationToken);

        return 0;
    }
}
