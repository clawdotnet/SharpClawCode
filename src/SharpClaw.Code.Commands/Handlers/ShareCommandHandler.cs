using System.CommandLine;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Creates or refreshes a self-hosted share snapshot for a session.
/// </summary>
public sealed class ShareCommandHandler(
    IRuntimeCommandService runtimeCommandService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "share";

    /// <inheritdoc />
    public string Description => "Creates or refreshes a self-hosted share link for a session.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        var idOption = new Option<string?>("--id") { Description = "Session id; latest when omitted." };
        command.Options.Add(idOption);
        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var context = globalOptions.Resolve(parseResult);
            var id = parseResult.GetValue(idOption);
            var result = await runtimeCommandService.ShareSessionAsync(id, ToRuntimeContext(context), cancellationToken).ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });
        return command;
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var id = command.Arguments.Length > 0 ? command.Arguments[0] : null;
        var result = await runtimeCommandService.ShareSessionAsync(id, ToRuntimeContext(context), cancellationToken).ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    private static RuntimeCommandContext ToRuntimeContext(CommandExecutionContext context)
        => new(
            context.WorkingDirectory,
            context.Model,
            context.PermissionMode,
            context.OutputFormat,
            context.PrimaryMode,
            context.SessionId,
            context.AgentId);
}
