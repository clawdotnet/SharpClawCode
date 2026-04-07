using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Adjusts or displays the REPL primary workflow mode.
/// </summary>
public sealed class ModeSlashCommandHandler(
    ReplInteractionState replState,
    OutputRendererDispatcher outputRendererDispatcher) : ISlashCommandHandler
{
    /// <inheritdoc />
    public string CommandName => "mode";

    /// <inheritdoc />
    public string Description => "Shows or sets build vs plan mode for the REPL session.";

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        if (command.Arguments.Length == 0)
        {
            var effective = replState.PrimaryModeOverride ?? context.PrimaryMode;
            var message = $"Primary mode: {effective} (override: {(replState.PrimaryModeOverride is null ? "none" : replState.PrimaryModeOverride.ToString())}).";
            return RenderAsync(message, context, cancellationToken);
        }

        var next = command.Arguments[0].Trim().ToLowerInvariant() switch
        {
            "plan" => PrimaryMode.Plan,
            "build" => PrimaryMode.Build,
            _ => (PrimaryMode?)null,
        };

        if (next is null)
        {
            return RenderAsync("Usage: /mode [build|plan]", context, cancellationToken, success: false);
        }

        replState.PrimaryModeOverride = next;
        return RenderAsync($"Primary mode set to {next}.", context, cancellationToken);
    }

    private async Task<int> RenderAsync(
        string message,
        CommandExecutionContext context,
        CancellationToken cancellationToken,
        bool success = true)
    {
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(success, success ? 0 : 1, context.OutputFormat, message, null),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return success ? 0 : 1;
    }
}
