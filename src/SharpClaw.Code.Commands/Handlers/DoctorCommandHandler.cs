using System.CommandLine;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Implements the doctor command.
/// </summary>
public sealed class DoctorCommandHandler(
    IRuntimeCommandService runtimeCommandService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "doctor";

    /// <inheritdoc />
    public string Description => "Runs basic CLI and runtime diagnostics.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var result = await runtimeCommandService.RunDoctorAsync(ToRuntimeContext(context), cancellationToken);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken);
            return result.ExitCode;
        });

        return command;
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService.RunDoctorAsync(ToRuntimeContext(context), cancellationToken);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken);
        return result.ExitCode;
    }

    private static RuntimeCommandContext ToRuntimeContext(CommandExecutionContext context)
        => new(
            context.WorkingDirectory,
            context.Model,
            context.PermissionMode,
            context.OutputFormat,
            context.PrimaryMode,
            context.SessionId);
}
