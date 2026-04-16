using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Lists, saves, and deletes structured memory entries.
/// </summary>
public sealed class MemoryCommandHandler(
    IPersistentMemoryStore persistentMemoryStore,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "memory";

    /// <inheritdoc />
    public string Description => "Lists, saves, and deletes durable project and user memory entries.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);

        var list = new Command("list", "Lists memory entries.");
        var scopeOption = new Option<MemoryScope?>("--scope") { Description = "Optional memory scope filter." };
        var queryOption = new Option<string?>("--query") { Description = "Optional free-text filter." };
        var limitOption = new Option<int?>("--limit") { Description = "Maximum number of rows to return." };
        list.Options.Add(scopeOption);
        list.Options.Add(queryOption);
        list.Options.Add(limitOption);
        list.SetAction((parseResult, cancellationToken) => ExecuteListAsync(
            globalOptions.Resolve(parseResult),
            parseResult.GetValue(scopeOption),
            parseResult.GetValue(queryOption),
            parseResult.GetValue(limitOption),
            cancellationToken));
        command.Subcommands.Add(list);

        var save = new Command("save", "Saves a memory entry.");
        var saveScope = new Option<MemoryScope>("--scope") { Description = "Memory scope.", DefaultValueFactory = _ => MemoryScope.Project };
        var sourceOption = new Option<string>("--source") { Description = "Source label.", DefaultValueFactory = _ => "manual" };
        var contentArgument = new Argument<string>("content") { Description = "Memory content." };
        save.Options.Add(saveScope);
        save.Options.Add(sourceOption);
        save.Arguments.Add(contentArgument);
        save.SetAction((parseResult, cancellationToken) => ExecuteSaveAsync(
            globalOptions.Resolve(parseResult),
            parseResult.GetValue(saveScope),
            parseResult.GetValue(sourceOption) ?? "manual",
            parseResult.GetValue(contentArgument) ?? string.Empty,
            cancellationToken));
        command.Subcommands.Add(save);

        var delete = new Command("delete", "Deletes a memory entry.");
        var deleteScope = new Option<MemoryScope>("--scope") { Description = "Memory scope.", DefaultValueFactory = _ => MemoryScope.Project };
        var idArgument = new Argument<string>("id") { Description = "Memory id." };
        delete.Options.Add(deleteScope);
        delete.Arguments.Add(idArgument);
        delete.SetAction((parseResult, cancellationToken) => ExecuteDeleteAsync(
            globalOptions.Resolve(parseResult),
            parseResult.GetValue(deleteScope),
            parseResult.GetValue(idArgument) ?? string.Empty,
            cancellationToken));
        command.Subcommands.Add(delete);

        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), null, null, null, cancellationToken));
        return command;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        if (command.Arguments.Length > 0 && string.Equals(command.Arguments[0], "save", StringComparison.OrdinalIgnoreCase))
        {
            var parsedScope = MemoryScope.Project;
            var hasExplicitScope = command.Arguments.Length > 1 && Enum.TryParse(command.Arguments[1], true, out parsedScope);
            var scope = hasExplicitScope ? parsedScope : MemoryScope.Project;
            var content = string.Join(' ', command.Arguments.Skip(hasExplicitScope ? 2 : 1));
            return ExecuteSaveAsync(context, scope, "manual", content, cancellationToken);
        }

        if (command.Arguments.Length > 0 && string.Equals(command.Arguments[0], "delete", StringComparison.OrdinalIgnoreCase))
        {
            var id = command.Arguments.Length > 1 ? command.Arguments[1] : string.Empty;
            return ExecuteDeleteAsync(context, MemoryScope.Project, id, cancellationToken);
        }

        var query = command.Arguments.Length > 1 && string.Equals(command.Arguments[0], "list", StringComparison.OrdinalIgnoreCase)
            ? string.Join(' ', command.Arguments.Skip(1))
            : string.Join(' ', command.Arguments);
        return ExecuteListAsync(context, null, string.IsNullOrWhiteSpace(query) ? null : query, null, cancellationToken);
    }

    private async Task<int> ExecuteListAsync(
        CommandExecutionContext context,
        MemoryScope? scope,
        string? query,
        int? limit,
        CancellationToken cancellationToken)
    {
        var rows = await persistentMemoryStore
            .ListAsync(context.WorkingDirectory, scope, query, Math.Clamp(limit.GetValueOrDefault(20), 1, 100), cancellationToken)
            .ConfigureAwait(false);
        return await RenderAsync(context, rows.ToList(), $"{rows.Count} memory entr{(rows.Count == 1 ? "y" : "ies")}.", cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ExecuteSaveAsync(
        CommandExecutionContext context,
        MemoryScope scope,
        string source,
        string content,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new MemoryEntry(
            Id: $"memory-{Guid.NewGuid():N}",
            Scope: scope,
            Content: content,
            Source: source,
            SourceSessionId: context.SessionId,
            SourceTurnId: null,
            Tags: [],
            Confidence: null,
            RelatedFilePath: null,
            RelatedSymbolName: null,
            CreatedAtUtc: now,
            UpdatedAtUtc: now);
        var saved = await persistentMemoryStore
            .SaveAsync(scope == MemoryScope.Project ? context.WorkingDirectory : null, entry, cancellationToken)
            .ConfigureAwait(false);
        return await RenderAsync(context, saved, $"Saved {scope.ToString().ToLowerInvariant()} memory {saved.Id}.", cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> ExecuteDeleteAsync(
        CommandExecutionContext context,
        MemoryScope scope,
        string id,
        CancellationToken cancellationToken)
    {
        var deleted = await persistentMemoryStore
            .DeleteAsync(scope == MemoryScope.Project ? context.WorkingDirectory : null, scope, id, cancellationToken)
            .ConfigureAwait(false);
        var result = new CommandResult(
            deleted,
            deleted ? 0 : 1,
            context.OutputFormat,
            deleted ? $"Deleted memory {id}." : $"Memory {id} was not found.",
            null);
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return result.ExitCode;
    }

    private async Task<int> RenderAsync<TPayload>(
        CommandExecutionContext context,
        TPayload payload,
        string message,
        CancellationToken cancellationToken)
    {
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            message,
            JsonSerializer.Serialize(payload, ProtocolJsonContext.Default.Options));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }
}
