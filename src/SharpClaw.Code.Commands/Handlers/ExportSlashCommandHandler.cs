using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Exports the latest or named session from the REPL.
/// </summary>
public sealed class ExportSlashCommandHandler(IRuntimeCommandService runtimeCommandService, OutputRendererDispatcher outputRendererDispatcher)
    : ISlashCommandHandler
{
    /// <inheritdoc />
    public string CommandName => "export";

    /// <inheritdoc />
    public string Description => "Exports a session as Markdown or JSON (/export md|json [sessionId]).";

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        if (command.Arguments.Length == 0)
        {
            return RenderAsync("Usage: /export md|json [sessionId]", context, false, cancellationToken);
        }

        var format = command.Arguments[0].Trim().ToLowerInvariant() switch
        {
            "md" or "markdown" => SessionExportFormat.Markdown,
            "json" => SessionExportFormat.Json,
            _ => (SessionExportFormat?)null,
        };
        if (format is null)
        {
            return RenderAsync("Usage: /export md|json [sessionId]", context, false, cancellationToken);
        }

        string? sessionId = command.Arguments.Length > 1 ? command.Arguments[1] : null;
        return ExportAsync(sessionId, format.Value, context, cancellationToken);
    }

    private async Task<int> ExportAsync(
        string? sessionId,
        SessionExportFormat format,
        CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var result = await runtimeCommandService
            .ExportSessionAsync(sessionId, format, null, context.ToRuntimeCommandContext(), cancellationToken)
            .ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    private async Task<int> RenderAsync(string message, CommandExecutionContext context, bool success, CancellationToken cancellationToken)
    {
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(success, success ? 0 : 1, context.OutputFormat, message, null),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return success ? 0 : 1;
    }
}
