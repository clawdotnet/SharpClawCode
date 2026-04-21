using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Surfaces durable usage totals for the current workspace.
/// </summary>
public sealed class UsageCommandHandler(
    IWorkspaceInsightsService workspaceInsightsService,
    IUsageMeteringService usageMeteringService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "usage";

    /// <inheritdoc />
    public string Description => "Shows session and workspace token usage totals.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        command.Subcommands.Add(BuildSummaryCommand(globalOptions));
        command.Subcommands.Add(BuildDetailCommand(globalOptions));
        command.SetAction((parseResult, cancellationToken) => ExecuteAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
        => command.Arguments.Length switch
        {
            0 => ExecuteAsync(context, cancellationToken),
            _ when string.Equals(command.Arguments[0], "summary", StringComparison.OrdinalIgnoreCase)
                => ExecuteSummaryAsync(context, null, null, cancellationToken),
            _ when string.Equals(command.Arguments[0], "detail", StringComparison.OrdinalIgnoreCase)
                => ExecuteDetailAsync(
                    context,
                    null,
                    null,
                    command.Arguments.Length > 1 && int.TryParse(command.Arguments[1], out var limit) ? limit : null,
                    cancellationToken),
            _ => ExecuteAsync(context, cancellationToken)
        };

    private Command BuildSummaryCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("summary", "Shows tenant-aware metering totals for the current workspace.");
        var fromUtcOption = new Option<string?>("--from-utc") { Description = "Inclusive lower-bound timestamp in UTC (ISO-8601)." };
        var toUtcOption = new Option<string?>("--to-utc") { Description = "Exclusive upper-bound timestamp in UTC (ISO-8601)." };
        command.Options.Add(fromUtcOption);
        command.Options.Add(toUtcOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteSummaryAsync(
            globalOptions.Resolve(parseResult),
            ParseTimestamp(parseResult.GetValue(fromUtcOption), "--from-utc"),
            ParseTimestamp(parseResult.GetValue(toUtcOption), "--to-utc"),
            cancellationToken));
        return command;
    }

    private Command BuildDetailCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("detail", "Lists normalized usage metering records for the current workspace.");
        var fromUtcOption = new Option<string?>("--from-utc") { Description = "Inclusive lower-bound timestamp in UTC (ISO-8601)." };
        var toUtcOption = new Option<string?>("--to-utc") { Description = "Exclusive upper-bound timestamp in UTC (ISO-8601)." };
        var limitOption = new Option<int?>("--limit") { Description = "Maximum number of records to return." };
        command.Options.Add(fromUtcOption);
        command.Options.Add(toUtcOption);
        command.Options.Add(limitOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteDetailAsync(
            globalOptions.Resolve(parseResult),
            ParseTimestamp(parseResult.GetValue(fromUtcOption), "--from-utc"),
            ParseTimestamp(parseResult.GetValue(toUtcOption), "--to-utc"),
            parseResult.GetValue(limitOption),
            cancellationToken));
        return command;
    }

    private async Task<int> ExecuteAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var report = await workspaceInsightsService
            .BuildUsageReportAsync(context.WorkingDirectory, context.SessionId, cancellationToken)
            .ConfigureAwait(false);
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            $"Workspace total: {report.WorkspaceTotal.TotalTokens} tokens across {report.Sessions.Count} session(s).",
            JsonSerializer.Serialize(report, ProtocolJsonContext.Default.WorkspaceUsageReport));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteSummaryAsync(
        CommandExecutionContext context,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken)
    {
        var report = await usageMeteringService
            .GetSummaryAsync(context.WorkingDirectory, CreateQuery(context, fromUtc, toUtc), cancellationToken)
            .ConfigureAwait(false);
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            $"Usage summary: {report.TotalUsage.TotalTokens} tokens, {report.ProviderRequestCount} provider request(s), {report.ToolExecutionCount} tool execution(s), {report.TurnCount} turn(s).",
            JsonSerializer.Serialize(report, ProtocolJsonContext.Default.UsageMeteringSummaryReport));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteDetailAsync(
        CommandExecutionContext context,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        int? limit,
        CancellationToken cancellationToken)
    {
        var report = await usageMeteringService
            .GetDetailAsync(
                context.WorkingDirectory,
                CreateQuery(context, fromUtc, toUtc),
                Math.Clamp(limit.GetValueOrDefault(50), 1, 500),
                cancellationToken)
            .ConfigureAwait(false);
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            $"Usage detail: {report.Records.Count} record(s).",
            JsonSerializer.Serialize(report, ProtocolJsonContext.Default.UsageMeteringDetailReport));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static UsageMeteringQuery CreateQuery(
        CommandExecutionContext context,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc)
        => new(
            FromUtc: fromUtc,
            ToUtc: toUtc,
            TenantId: context.HostContext?.TenantId,
            HostId: context.HostContext?.HostId,
            WorkspaceRoot: context.WorkingDirectory,
            SessionId: context.SessionId);

    private static DateTimeOffset? ParseTimestamp(string? value, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, out var parsed))
        {
            return parsed;
        }

        throw new InvalidOperationException($"Option '{optionName}' must be a valid UTC timestamp.");
    }
}
