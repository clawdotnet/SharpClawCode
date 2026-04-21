using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Opens the configured external editor to compose a prompt blob.
/// </summary>
public sealed class EditorSlashCommandHandler(
    IExternalEditorService externalEditorService,
    OutputRendererDispatcher outputRendererDispatcher,
    IRuntimeCommandService runtimeCommandService) : ISlashCommandHandler
{
    /// <inheritdoc />
    public string CommandName => "editor";

    /// <inheritdoc />
    public string Description => "Opens $VISUAL/$EDITOR to compose prompt text, then runs it.";

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        string? initial = command.Arguments.Length > 0 ? string.Join(' ', command.Arguments) : null;
        var composed = await externalEditorService.ComposeAsync(initial, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(composed))
        {
            await outputRendererDispatcher.RenderCommandResultAsync(
                new CommandResult(true, 0, context.OutputFormat, "Editor compose canceled.", null),
                context.OutputFormat,
                cancellationToken).ConfigureAwait(false);
            return 0;
        }

        try
        {
            var result = await runtimeCommandService
                .ExecutePromptAsync(composed.Trim(), context.ToRuntimeCommandContext(), cancellationToken)
                .ConfigureAwait(false);
            await outputRendererDispatcher.RenderTurnExecutionResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (SharpClaw.Code.Providers.Models.ProviderExecutionException exception)
        {
            await outputRendererDispatcher.RenderCommandResultAsync(
                new CommandResult(false, 1, context.OutputFormat, $"Provider failure ({exception.Kind}): {exception.Message}", null),
                context.OutputFormat,
                cancellationToken).ConfigureAwait(false);
            return 1;
        }
    }
}
