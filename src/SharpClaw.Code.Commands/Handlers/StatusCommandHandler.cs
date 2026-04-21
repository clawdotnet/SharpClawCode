using System.CommandLine;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Implements the status command.
/// </summary>
public sealed class StatusCommandHandler(
    IRuntimeCommandService runtimeCommandService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "status";

    /// <inheritdoc />
    public string Description => "Displays the current runtime status.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var result = await runtimeCommandService.GetStatusAsync(context.ToRuntimeCommandContext(), cancellationToken);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken);
            return result.ExitCode;
        });

        return command;
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService.GetStatusAsync(context.ToRuntimeCommandContext(), cancellationToken);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken);
        return result.ExitCode;
    }
}
