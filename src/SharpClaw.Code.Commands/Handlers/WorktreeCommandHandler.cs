using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Git.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Lists, creates, and prunes git worktrees for the current repository.
/// </summary>
public sealed class WorktreeCommandHandler(
    IGitWorkspaceService gitWorkspaceService,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public string Name => "worktree";

    /// <inheritdoc />
    public string Description => "Lists, creates, and prunes git worktrees.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        command.Subcommands.Add(BuildListCommand(globalOptions));
        command.Subcommands.Add(BuildAddCommand(globalOptions));
        command.Subcommands.Add(BuildPruneCommand(globalOptions));
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

        if (string.Equals(command.Arguments[0], "add", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 3)
        {
            var startPoint = command.Arguments.Length >= 4 ? command.Arguments[3] : null;
            return ExecuteAddAsync(command.Arguments[1], command.Arguments[2], startPoint, useExistingBranch: false, context, cancellationToken);
        }

        if (string.Equals(command.Arguments[0], "attach", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 3)
        {
            return ExecuteAddAsync(command.Arguments[1], command.Arguments[2], null, useExistingBranch: true, context, cancellationToken);
        }

        if (string.Equals(command.Arguments[0], "prune", StringComparison.OrdinalIgnoreCase))
        {
            return ExecutePruneAsync(context, cancellationToken);
        }

        return RenderAsync(
            "Usage: /worktree [list|add <path> <branch> [startPoint]|attach <path> <branch>|prune]",
            null,
            context,
            success: false,
            cancellationToken: cancellationToken);
    }

    private Command BuildListCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("list", "Lists git worktrees.");
        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private Command BuildAddCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("add", "Creates a git worktree.");
        var pathOption = new Option<string>("--path") { Required = true, Description = "Path for the new worktree." };
        var branchOption = new Option<string>("--branch") { Required = true, Description = "Branch name to create or attach." };
        var startPointOption = new Option<string?>("--start-point") { Description = "Optional starting ref for a new branch." };
        var useExistingBranchOption = new Option<bool>("--use-existing-branch")
        {
            Description = "Attach an existing branch instead of creating a new one.",
            DefaultValueFactory = _ => false,
        };
        command.Options.Add(pathOption);
        command.Options.Add(branchOption);
        command.Options.Add(startPointOption);
        command.Options.Add(useExistingBranchOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteAddAsync(
            parseResult.GetValue(pathOption)!,
            parseResult.GetValue(branchOption)!,
            parseResult.GetValue(startPointOption),
            parseResult.GetValue(useExistingBranchOption),
            globalOptions.Resolve(parseResult),
            cancellationToken));
        return command;
    }

    private Command BuildPruneCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("prune", "Prunes stale git worktree administrative state.");
        command.SetAction((parseResult, cancellationToken) => ExecutePruneAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private async Task<int> ExecuteListAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            var payload = await gitWorkspaceService.ListWorktreesAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
            return await RenderAsync(
                $"{payload.Worktrees.Count} worktree(s).",
                JsonSerializer.Serialize(payload, JsonOptions),
                context,
                success: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return await RenderAsync(exception.Message, null, context, success: false, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> ExecuteAddAsync(
        string path,
        string branch,
        string? startPoint,
        bool useExistingBranch,
        CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.PermissionMode == PermissionMode.ReadOnly)
        {
            return await RenderAsync("Read-only mode blocks git worktree creation.", null, context, success: false, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var payload = await gitWorkspaceService
                .CreateWorktreeAsync(context.WorkingDirectory, path, branch, startPoint, useExistingBranch, cancellationToken)
                .ConfigureAwait(false);
            var message = useExistingBranch
                ? $"Attached worktree '{payload.Worktree.Path}' to existing branch '{payload.Worktree.Branch ?? branch}'."
                : $"Created worktree '{payload.Worktree.Path}' on branch '{payload.Worktree.Branch ?? branch}'.";
            return await RenderAsync(message, JsonSerializer.Serialize(payload, JsonOptions), context, success: true, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return await RenderAsync(exception.Message, null, context, success: false, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> ExecutePruneAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        if (context.PermissionMode == PermissionMode.ReadOnly)
        {
            return await RenderAsync("Read-only mode blocks git worktree pruning.", null, context, success: false, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var payload = await gitWorkspaceService.PruneWorktreesAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
            return await RenderAsync(
                $"Pruned {payload.PrunedCount} stale worktree record(s).",
                JsonSerializer.Serialize(payload, JsonOptions),
                context,
                success: true,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            return await RenderAsync(exception.Message, null, context, success: false, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<int> RenderAsync(
        string message,
        string? dataJson,
        CommandExecutionContext context,
        bool success,
        CancellationToken cancellationToken)
    {
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(success, success ? 0 : 1, context.OutputFormat, message, dataJson),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return success ? 0 : 1;
    }
}
