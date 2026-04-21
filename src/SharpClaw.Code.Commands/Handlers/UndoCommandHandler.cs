using System.CommandLine;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Reverts the last SharpClaw-tracked workspace mutation set.
/// </summary>
public sealed class UndoCommandHandler(IRuntimeCommandService runtimeCommandService, OutputRendererDispatcher outputRendererDispatcher)
    : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "undo";

    /// <inheritdoc />
    public string Description => "Undoes the last checkpoint-backed file mutation set for the session.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var cmd = new Command(Name, Description);
        var id = new Option<string?>("--id") { Description = "Session id; latest/attached when omitted." };
        cmd.Options.Add(id);
        cmd.SetAction(async (parseResult, cancellationToken) =>
        {
            var ctx = globalOptions.Resolve(parseResult);
            var sid = parseResult.GetValue(id);
            var result = await runtimeCommandService.UndoAsync(sid, ctx.ToRuntimeCommandContext(), cancellationToken).ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, ctx.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });
        return cmd;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var sid = command.Arguments.Length > 0 ? command.Arguments[0] : null;
        return ExecuteUndoAsync(sid, context, cancellationToken);
    }

    private async Task<int> ExecuteUndoAsync(string? sessionId, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService
            .UndoAsync(sessionId, context.ToRuntimeCommandContext(), cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }
}
