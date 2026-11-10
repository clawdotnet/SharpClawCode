using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Manages durable scheduled prompts for the current workspace.
/// </summary>
public sealed class ScheduleCommandHandler(
    IScheduledPromptService scheduledPromptService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "schedule";

    /// <inheritdoc />
    public string Description => "Lists, persists, and runs workspace scheduled prompts.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);

        var list = new Command("list", "Lists schedules for the workspace.");
        list.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        command.Subcommands.Add(list);

        var add = CreateUpsertCommand("add", "Adds a schedule.", globalOptions, isUpdate: false);
        command.Subcommands.Add(add);

        var update = CreateUpsertCommand("update", "Updates a schedule.", globalOptions, isUpdate: true);
        command.Subcommands.Add(update);

        command.Subcommands.Add(CreateIdCommand("remove", "Removes a schedule.", globalOptions, ExecuteRemoveAsync));
        command.Subcommands.Add(CreateIdCommand("pause", "Pauses a schedule.", globalOptions, (id, context, ct) => ExecuteSetEnabledAsync(id, context, enabled: false, ct)));
        command.Subcommands.Add(CreateIdCommand("resume", "Resumes a schedule.", globalOptions, (id, context, ct) => ExecuteSetEnabledAsync(id, context, enabled: true, ct)));
        command.Subcommands.Add(CreateIdCommand("run", "Runs a schedule immediately.", globalOptions, ExecuteRunAsync));

        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        if (command.Arguments.Length == 0 || string.Equals(command.Arguments[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteListAsync(context, cancellationToken);
        }

        if (command.Arguments.Length >= 2 && string.Equals(command.Arguments[0], "run", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteRunAsync(command.Arguments[1], context, cancellationToken);
        }

        if (command.Arguments.Length >= 2 && string.Equals(command.Arguments[0], "remove", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteRemoveAsync(command.Arguments[1], context, cancellationToken);
        }

        if (command.Arguments.Length >= 2 && string.Equals(command.Arguments[0], "pause", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteSetEnabledAsync(command.Arguments[1], context, enabled: false, cancellationToken);
        }

        if (command.Arguments.Length >= 2 && string.Equals(command.Arguments[0], "resume", StringComparison.OrdinalIgnoreCase))
        {
            return ExecuteSetEnabledAsync(command.Arguments[1], context, enabled: true, cancellationToken);
        }

        if (string.Equals(command.Arguments[0], "add", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 4)
        {
            var sessionTarget = command.Arguments.Length >= 7 ? command.Arguments[6] : "attached";
            return ExecuteSaveAsync(
                scheduleId: null,
                name: command.Arguments[1],
                prompt: command.Arguments[3],
                cron: command.Arguments[2],
                primaryMode: command.Arguments.Length >= 5 ? ParsePrimaryMode(command.Arguments[4]) : PrimaryMode.Build,
                modelOverride: null,
                permissionMode: command.Arguments.Length >= 6 ? ParsePermissionMode(command.Arguments[5]) : PermissionMode.WorkspaceWrite,
                approvalSettings: null,
                sessionTarget: ParseSessionTarget(sessionTarget),
                context: context,
                cancellationToken: cancellationToken).AsTask();
        }

        if (string.Equals(command.Arguments[0], "update", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 5)
        {
            var sessionTarget = command.Arguments.Length >= 8 ? command.Arguments[7] : "attached";
            return ExecuteSaveAsync(
                scheduleId: command.Arguments[1],
                name: command.Arguments[2],
                prompt: command.Arguments[4],
                cron: command.Arguments[3],
                primaryMode: command.Arguments.Length >= 6 ? ParsePrimaryMode(command.Arguments[5]) : PrimaryMode.Build,
                modelOverride: null,
                permissionMode: command.Arguments.Length >= 7 ? ParsePermissionMode(command.Arguments[6]) : PermissionMode.WorkspaceWrite,
                approvalSettings: null,
                sessionTarget: ParseSessionTarget(sessionTarget),
                context: context,
                cancellationToken: cancellationToken).AsTask();
        }

        return RenderAsync("Usage: /schedule [list|add <name> <cron> <prompt> [mode] [permissionMode] [sessionTarget]|update <id> <name> <cron> <prompt> [mode] [permissionMode] [sessionTarget]|run|pause|resume|remove <id>]", context, false, cancellationToken);
    }

    private Command CreateUpsertCommand(string name, string description, GlobalCliOptions globalOptions, bool isUpdate)
    {
        var command = new Command(name, description);
        var idOption = new Option<string?>("--id") { Description = "Schedule id." };
        var nameOption = new Option<string>("--name") { Required = true, Description = "Schedule name." };
        var promptOption = new Option<string>("--prompt") { Required = true, Description = "Prompt text to execute." };
        var cronOption = new Option<string>("--cron") { Required = true, Description = "Cron expression or @hourly/@daily/@weekly." };
        var primaryModeOption = new Option<string>("--primary-mode") { DefaultValueFactory = _ => "build", Description = "build, plan, spec, or research." };
        var modelOption = new Option<string?>("--model") { Description = "Optional model override." };
        var permissionModeOption = new Option<string>("--permission-mode") { DefaultValueFactory = _ => "workspaceWrite", Description = "readOnly, workspaceWrite, or dangerFullAccess." };
        var autoApproveOption = new Option<string?>("--auto-approve") { Description = "Optional durable auto-approval scopes." };
        var autoApproveBudgetOption = new Option<int?>("--auto-approve-budget") { Description = "Optional durable auto-approval budget." };
        var sessionTargetOption = new Option<string>("--session-target") { DefaultValueFactory = _ => "attached", Description = "new, attached, or an explicit session id." };

        if (isUpdate)
        {
            idOption.Required = true;
            command.Options.Add(idOption);
        }

        command.Options.Add(nameOption);
        command.Options.Add(promptOption);
        command.Options.Add(cronOption);
        command.Options.Add(primaryModeOption);
        command.Options.Add(modelOption);
        command.Options.Add(permissionModeOption);
        command.Options.Add(autoApproveOption);
        command.Options.Add(autoApproveBudgetOption);
        command.Options.Add(sessionTargetOption);

        command.SetAction((parseResult, cancellationToken) => ExecuteSaveAsync(
            scheduleId: isUpdate ? parseResult.GetValue(idOption) : null,
            name: parseResult.GetValue(nameOption) ?? throw new InvalidOperationException("--name is required."),
            prompt: parseResult.GetValue(promptOption) ?? throw new InvalidOperationException("--prompt is required."),
            cron: parseResult.GetValue(cronOption) ?? throw new InvalidOperationException("--cron is required."),
            primaryMode: ParsePrimaryMode(parseResult.GetValue(primaryModeOption) ?? "build"),
            modelOverride: parseResult.GetValue(modelOption),
            permissionMode: ParsePermissionMode(parseResult.GetValue(permissionModeOption) ?? "workspaceWrite"),
            approvalSettings: ApprovalSettingsText.Parse(parseResult.GetValue(autoApproveOption), parseResult.GetValue(autoApproveBudgetOption)),
            sessionTarget: ParseSessionTarget(parseResult.GetValue(sessionTargetOption) ?? "attached"),
            context: globalOptions.Resolve(parseResult),
            cancellationToken: cancellationToken).AsTask());
        return command;
    }

    private Command CreateIdCommand(
        string name,
        string description,
        GlobalCliOptions globalOptions,
        Func<string, CommandExecutionContext, CancellationToken, Task<int>> action)
    {
        var command = new Command(name, description);
        var idOption = new Option<string>("--id") { Required = true, Description = "Schedule id." };
        command.Options.Add(idOption);
        command.SetAction((parseResult, cancellationToken) => action(
            parseResult.GetValue(idOption) ?? throw new InvalidOperationException("--id is required."),
            globalOptions.Resolve(parseResult),
            cancellationToken));
        return command;
    }

    private async Task<int> ExecuteListAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var schedules = await scheduledPromptService.ListAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"{schedules.Count} scheduled prompt(s).", JsonSerializer.Serialize(schedules.ToList(), ProtocolJsonContext.Default.ListScheduledPromptDefinition)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async ValueTask<int> ExecuteSaveAsync(
        string? scheduleId,
        string name,
        string prompt,
        string cron,
        PrimaryMode primaryMode,
        string? modelOverride,
        PermissionMode permissionMode,
        ApprovalSettings? approvalSettings,
        ScheduledPromptSessionTarget sessionTarget,
        CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        ScheduledPromptDefinition definition;
        if (string.IsNullOrWhiteSpace(scheduleId))
        {
            definition = new ScheduledPromptDefinition(
                Id: CreateScheduleId(),
                WorkspaceRoot: context.WorkingDirectory,
                Name: name,
                Prompt: prompt,
                Cron: cron,
                PrimaryMode: primaryMode,
                ModelOverride: modelOverride,
                PermissionMode: permissionMode,
                ApprovalSettings: approvalSettings,
                SessionTarget: sessionTarget,
                Enabled: true,
                LastRunUtc: null,
                NextRunUtc: null,
                LastOutcome: null);
        }
        else
        {
            var existing = await scheduledPromptService.GetAsync(context.WorkingDirectory, scheduleId, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"Scheduled prompt '{scheduleId}' was not found.");
            definition = existing with
            {
                Name = name,
                Prompt = prompt,
                Cron = cron,
                PrimaryMode = primaryMode,
                ModelOverride = modelOverride,
                PermissionMode = permissionMode,
                ApprovalSettings = approvalSettings,
                SessionTarget = sessionTarget,
            };
        }

        var saved = await scheduledPromptService.SaveAsync(context.WorkingDirectory, definition, cancellationToken).ConfigureAwait(false);
        var message = string.IsNullOrWhiteSpace(scheduleId)
            ? $"Added scheduled prompt '{saved.Name}' ({saved.Id})."
            : $"Updated scheduled prompt '{saved.Name}' ({saved.Id}).";
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"{message} Next run: {saved.NextRunUtc?.ToString("O") ?? "paused"}.", JsonSerializer.Serialize(saved, ProtocolJsonContext.Default.ScheduledPromptDefinition)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteRemoveAsync(string scheduleId, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var removed = await scheduledPromptService.RemoveAsync(context.WorkingDirectory, scheduleId, cancellationToken).ConfigureAwait(false);
        return await RenderAsync(
            removed ? $"Removed scheduled prompt '{scheduleId}'." : $"Scheduled prompt '{scheduleId}' was not found.",
            context,
            removed,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ExecuteSetEnabledAsync(string scheduleId, CommandExecutionContext context, bool enabled, CancellationToken cancellationToken)
    {
        var updated = await scheduledPromptService.SetEnabledAsync(context.WorkingDirectory, scheduleId, enabled, cancellationToken).ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"{(enabled ? "Resumed" : "Paused")} scheduled prompt '{updated.Name}'.", JsonSerializer.Serialize(updated, ProtocolJsonContext.Default.ScheduledPromptDefinition)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteRunAsync(string scheduleId, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var report = await scheduledPromptService.RunAsync(context.WorkingDirectory, scheduleId, context.ToRuntimeCommandContext(isInteractive: false), cancellationToken).ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(report.Succeeded, report.Succeeded ? 0 : 1, context.OutputFormat, report.Message, JsonSerializer.Serialize(report, ProtocolJsonContext.Default.ScheduledPromptRunReport)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return report.Succeeded ? 0 : 1;
    }

    private async Task<int> RenderAsync(string message, CommandExecutionContext context, bool success, CancellationToken cancellationToken)
    {
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(success, success ? 0 : 1, context.OutputFormat, message, null),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return success ? 0 : 1;
    }

    private static PrimaryMode ParsePrimaryMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "plan" => PrimaryMode.Plan,
            "spec" => PrimaryMode.Spec,
            "research" => PrimaryMode.Research,
            _ => PrimaryMode.Build,
        };

    private static PermissionMode ParsePermissionMode(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "readonly" or "read-only" => PermissionMode.ReadOnly,
            "workspacewrite" or "workspace-write" or "prompt" or "autoapprovesafe" or "auto-approve-safe" => PermissionMode.WorkspaceWrite,
            "dangerfullaccess" or "danger-full-access" or "fulltrust" or "full-trust" => PermissionMode.DangerFullAccess,
            _ => PermissionMode.WorkspaceWrite,
        };

    private static ScheduledPromptSessionTarget ParseSessionTarget(string value)
        => value.Trim().ToLowerInvariant() switch
        {
            "new" => new ScheduledPromptSessionTarget(ScheduledPromptSessionTargetKind.New),
            "attached" => new ScheduledPromptSessionTarget(ScheduledPromptSessionTargetKind.Attached),
            _ => new ScheduledPromptSessionTarget(ScheduledPromptSessionTargetKind.Explicit, value.Trim()),
        };

    private static string CreateScheduleId()
    {
        var value = $"schedule-{Guid.NewGuid():N}";
        return value[..Math.Min(value.Length, 21)];
    }
}
