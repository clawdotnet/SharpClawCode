using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Manages durable session and workspace todo items.
/// </summary>
public sealed class TodoCommandHandler(
    ITodoService todoService,
    ISessionCoordinator sessionCoordinator,
    ISessionStore sessionStore,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "todo";

    /// <inheritdoc />
    public string Description => "Lists and mutates session or workspace todo items.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        command.Subcommands.Add(BuildListCommand(globalOptions));
        command.Subcommands.Add(BuildAddCommand(globalOptions));
        command.Subcommands.Add(BuildUpdateCommand(globalOptions));
        command.Subcommands.Add(BuildDoneCommand(globalOptions));
        command.Subcommands.Add(BuildRemoveCommand(globalOptions));
        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(null, null, globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    /// <inheritdoc />
    public async Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        if (command.Arguments.Length == 0 || string.Equals(command.Arguments[0], "list", StringComparison.OrdinalIgnoreCase))
        {
            var scopeText = command.Arguments.Length >= 2 ? command.Arguments[1] : null;
            var sessionId = command.Arguments.Length >= 3 ? command.Arguments[2] : null;
            return await ExecuteListAsync(scopeText, sessionId, context, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(command.Arguments[0], "add", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 3)
        {
            var scope = ParseScope(command.Arguments[1]);
            var title = string.Join(' ', command.Arguments.Skip(2));
            return await ExecuteAddAsync(scope, title, null, null, context, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(command.Arguments[0], "update", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 4)
        {
            var scope = ParseScope(command.Arguments[1]);
            var id = command.Arguments[2];
            var status = TryParseStatus(command.Arguments[3]);
            var title = command.Arguments.Length > 4 ? string.Join(' ', command.Arguments.Skip(4)) : null;
            return await ExecuteUpdateAsync(scope, id, title, status, null, null, context, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(command.Arguments[0], "done", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 3)
        {
            var scope = ParseScope(command.Arguments[1]);
            return await ExecuteUpdateAsync(scope, command.Arguments[2], null, TodoStatus.Done, null, null, context, cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(command.Arguments[0], "remove", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 3)
        {
            var scope = ParseScope(command.Arguments[1]);
            return await ExecuteRemoveAsync(scope, command.Arguments[2], null, context, cancellationToken).ConfigureAwait(false);
        }

        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(false, 1, context.OutputFormat, "Unsupported todo syntax.", null),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 1;
    }

    private Command BuildListCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("list", "Lists workspace and session todos.");
        var scopeOption = new Option<string?>("--scope") { Description = "Scope: session, workspace, or all." };
        var sessionOption = new Option<string?>("--session") { Description = "Session id when listing session todos." };
        command.Options.Add(scopeOption);
        command.Options.Add(sessionOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(parseResult.GetValue(scopeOption), parseResult.GetValue(sessionOption), globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private Command BuildAddCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("add", "Adds a todo item.");
        var scopeOption = new Option<string>("--scope") { Required = true, Description = "Scope: session or workspace." };
        var titleOption = new Option<string>("--title") { Required = true, Description = "Todo title." };
        var sessionOption = new Option<string?>("--session") { Description = "Session id for session-scoped todos." };
        var ownerOption = new Option<string?>("--owner-agent") { Description = "Optional owner agent id." };
        command.Options.Add(scopeOption);
        command.Options.Add(titleOption);
        command.Options.Add(sessionOption);
        command.Options.Add(ownerOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteAddAsync(ParseScope(parseResult.GetValue(scopeOption)!), parseResult.GetValue(titleOption)!, parseResult.GetValue(sessionOption), parseResult.GetValue(ownerOption), globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private Command BuildUpdateCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("update", "Updates a todo item.");
        var scopeOption = new Option<string>("--scope") { Required = true, Description = "Scope: session or workspace." };
        var idOption = new Option<string>("--id") { Required = true, Description = "Todo id." };
        var titleOption = new Option<string?>("--title") { Description = "Optional replacement title." };
        var statusOption = new Option<string?>("--status") { Description = "Optional status: open, inProgress, blocked, done." };
        var sessionOption = new Option<string?>("--session") { Description = "Session id for session-scoped todos." };
        var ownerOption = new Option<string?>("--owner-agent") { Description = "Optional owner agent id." };
        command.Options.Add(scopeOption);
        command.Options.Add(idOption);
        command.Options.Add(titleOption);
        command.Options.Add(statusOption);
        command.Options.Add(sessionOption);
        command.Options.Add(ownerOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteUpdateAsync(
            ParseScope(parseResult.GetValue(scopeOption)!),
            parseResult.GetValue(idOption)!,
            parseResult.GetValue(titleOption),
            TryParseStatus(parseResult.GetValue(statusOption)),
            parseResult.GetValue(sessionOption),
            parseResult.GetValue(ownerOption),
            globalOptions.Resolve(parseResult),
            cancellationToken));
        return command;
    }

    private Command BuildDoneCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("done", "Marks a todo as done.");
        var scopeOption = new Option<string>("--scope") { Required = true, Description = "Scope: session or workspace." };
        var idOption = new Option<string>("--id") { Required = true, Description = "Todo id." };
        var sessionOption = new Option<string?>("--session") { Description = "Session id for session-scoped todos." };
        command.Options.Add(scopeOption);
        command.Options.Add(idOption);
        command.Options.Add(sessionOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteUpdateAsync(
            ParseScope(parseResult.GetValue(scopeOption)!),
            parseResult.GetValue(idOption)!,
            null,
            TodoStatus.Done,
            parseResult.GetValue(sessionOption),
            null,
            globalOptions.Resolve(parseResult),
            cancellationToken));
        return command;
    }

    private Command BuildRemoveCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("remove", "Removes a todo item.");
        var scopeOption = new Option<string>("--scope") { Required = true, Description = "Scope: session or workspace." };
        var idOption = new Option<string>("--id") { Required = true, Description = "Todo id." };
        var sessionOption = new Option<string?>("--session") { Description = "Session id for session-scoped todos." };
        command.Options.Add(scopeOption);
        command.Options.Add(idOption);
        command.Options.Add(sessionOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteRemoveAsync(ParseScope(parseResult.GetValue(scopeOption)!), parseResult.GetValue(idOption)!, parseResult.GetValue(sessionOption), globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private async Task<int> ExecuteListAsync(string? scopeText, string? explicitSessionId, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var scope = ParseOptionalScope(scopeText);
        var sessionId = explicitSessionId;
        if (scope is null or TodoScope.Session)
        {
            sessionId = await ResolveSessionIdAsync(context.WorkingDirectory, explicitSessionId ?? context.SessionId, cancellationToken).ConfigureAwait(false);
        }

        var snapshot = await todoService.GetSnapshotAsync(context.WorkingDirectory, sessionId, cancellationToken).ConfigureAwait(false);
        var payload = scope switch
        {
            TodoScope.Session => snapshot with { WorkspaceTodos = [] },
            TodoScope.Workspace => snapshot with { SessionTodos = [] },
            _ => snapshot
        };
        var count = payload.SessionTodos.Count + payload.WorkspaceTodos.Count;
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"{count} todo item(s).", JsonSerializer.Serialize(payload, ProtocolJsonContext.Default.TodoSnapshot)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteAddAsync(
        TodoScope scope,
        string title,
        string? explicitSessionId,
        string? ownerAgentId,
        CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var sessionId = scope == TodoScope.Session
            ? await ResolveSessionIdAsync(context.WorkingDirectory, explicitSessionId ?? context.SessionId, cancellationToken).ConfigureAwait(false)
            : explicitSessionId ?? context.SessionId;
        var todo = await todoService.AddAsync(context.WorkingDirectory, scope, title, sessionId, ownerAgentId, cancellationToken).ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"Added todo '{todo.Id}'.", JsonSerializer.Serialize(todo, ProtocolJsonContext.Default.TodoItem)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteUpdateAsync(
        TodoScope scope,
        string id,
        string? title,
        TodoStatus? status,
        string? explicitSessionId,
        string? ownerAgentId,
        CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var sessionId = scope == TodoScope.Session
            ? await ResolveSessionIdAsync(context.WorkingDirectory, explicitSessionId ?? context.SessionId, cancellationToken).ConfigureAwait(false)
            : explicitSessionId ?? context.SessionId;
        var todo = await todoService.UpdateAsync(context.WorkingDirectory, scope, id, sessionId, title, status, ownerAgentId, cancellationToken).ConfigureAwait(false);
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"Updated todo '{todo.Id}'.", JsonSerializer.Serialize(todo, ProtocolJsonContext.Default.TodoItem)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteRemoveAsync(
        TodoScope scope,
        string id,
        string? explicitSessionId,
        CommandExecutionContext context,
        CancellationToken cancellationToken)
    {
        var sessionId = scope == TodoScope.Session
            ? await ResolveSessionIdAsync(context.WorkingDirectory, explicitSessionId ?? context.SessionId, cancellationToken).ConfigureAwait(false)
            : explicitSessionId ?? context.SessionId;
        var removed = await todoService.RemoveAsync(context.WorkingDirectory, scope, id, sessionId, cancellationToken).ConfigureAwait(false);
        var exitCode = removed ? 0 : 1;
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(removed, exitCode, context.OutputFormat, removed ? $"Removed todo '{id}'." : $"Todo '{id}' was not found.", JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = id }, ProtocolJsonContext.Default.DictionaryStringString)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return exitCode;
    }

    private async Task<string?> ResolveSessionIdAsync(string workspaceRoot, string? preferredSessionId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(preferredSessionId))
        {
            return preferredSessionId;
        }

        var attached = await sessionCoordinator.GetAttachedSessionIdAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(attached))
        {
            return attached;
        }

        var latest = await sessionStore.GetLatestAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        return latest?.Id;
    }

    private static TodoScope ParseScope(string scope)
        => scope.Trim().ToLowerInvariant() switch
        {
            "session" => TodoScope.Session,
            "workspace" => TodoScope.Workspace,
            _ => throw new InvalidOperationException($"Unsupported todo scope '{scope}'.")
        };

    private static TodoScope? ParseOptionalScope(string? scope)
        => string.IsNullOrWhiteSpace(scope)
            ? null
            : ParseScope(scope);

    private static TodoStatus? TryParseStatus(string? status)
        => string.IsNullOrWhiteSpace(status)
            ? null
            : status.Trim().ToLowerInvariant() switch
            {
                "open" => TodoStatus.Open,
                "inprogress" or "in-progress" => TodoStatus.InProgress,
                "blocked" => TodoStatus.Blocked,
                "done" => TodoStatus.Done,
                _ => throw new InvalidOperationException($"Unsupported todo status '{status}'.")
            };
}
