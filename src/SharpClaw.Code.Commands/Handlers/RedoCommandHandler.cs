using System.CommandLine;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Re-applies the last undone SharpClaw-tracked mutation set.
/// </summary>
public sealed class RedoCommandHandler(IRuntimeCommandService runtimeCommandService, OutputRendererDispatcher outputRendererDispatcher)
    : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "redo";

    /// <inheritdoc />
    public string Description => "Redoes the last undone checkpoint-backed mutation set.";

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
            var result = await runtimeCommandService.RedoAsync(sid, ctx.ToRuntimeCommandContext(), cancellationToken).ConfigureAwait(false);
            await outputRendererDispatcher.RenderCommandResultAsync(result, ctx.OutputFormat, cancellationToken).ConfigureAwait(false);
            return result.ExitCode;
        });
        return cmd;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var sid = command.Arguments.Length > 0 ? command.Arguments[0] : null;
        return ExecuteRedoAsync(sid, context, cancellationToken);
    }

    private async Task<int> ExecuteRedoAsync(string? sessionId, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService
            .RedoAsync(sessionId, context.ToRuntimeCommandContext(), cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }
}
