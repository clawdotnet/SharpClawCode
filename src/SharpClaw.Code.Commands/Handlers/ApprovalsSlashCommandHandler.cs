using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Shows or adjusts bounded auto-approval settings for REPL-driven prompts.
/// </summary>
public sealed class ApprovalsSlashCommandHandler(
    ReplInteractionState replState,
    OutputRendererDispatcher outputRendererDispatcher) : ISlashCommandHandler
{
    /// <inheritdoc />
    public string CommandName => "approvals";

    /// <inheritdoc />
    public string Description => "Shows or sets auto-approval scopes and budget for REPL prompts.";

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        if (command.Arguments.Length == 0)
        {
            var effective = replState.ApprovalSettingsOverride ?? context.ApprovalSettings;
            return RenderAsync(
                $"Auto-approvals: {ApprovalSettingsText.RenderSummary(effective)} (override: {(replState.ApprovalSettingsOverride is null ? "none" : ApprovalSettingsText.RenderSummary(replState.ApprovalSettingsOverride))}).",
                context,
                cancellationToken);
        }

        if (string.Equals(command.Arguments[0], "reset", StringComparison.OrdinalIgnoreCase)
            || string.Equals(command.Arguments[0], "clear", StringComparison.OrdinalIgnoreCase))
        {
            replState.ApprovalSettingsOverride = null;
            return RenderAsync("Auto-approval reset for the next prompt.", context, cancellationToken);
        }

        if (!string.Equals(command.Arguments[0], "set", StringComparison.OrdinalIgnoreCase) || command.Arguments.Length < 2)
        {
            return RenderAsync("Usage: /approvals [set <scopes> [budget]|reset]", context, cancellationToken, success: false);
        }

        var budget = command.Arguments.Length >= 3
            ? ParseBudget(command.Arguments[2])
            : null;
        var settings = ApprovalSettingsText.Parse(command.Arguments[1], budget) ?? ApprovalSettings.Empty;
        replState.ApprovalSettingsOverride = settings;
        return RenderAsync(
            $"Auto-approval override set to {ApprovalSettingsText.RenderSummary(settings)}.",
            context,
            cancellationToken);
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

    private static int? ParseBudget(string value)
        => int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : throw new InvalidOperationException($"Invalid auto-approve budget '{value}'.");
}
