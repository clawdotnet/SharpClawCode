using System.CommandLine;
using System.Text.Json;
using SharpClaw.Code.Commands.Models;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Skills.Abstractions;
using SharpClaw.Code.Skills.Models;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Manages workspace-local skills under <c>.sharpclaw/skills</c>.
/// </summary>
public sealed class SkillsCommandHandler(
    ISkillRegistry skillRegistry,
    IFileSystem fileSystem,
    OutputRendererDispatcher outputRendererDispatcher) : ICommandHandler, ISlashCommandHandler
{
    /// <inheritdoc />
    public string Name => "skills";

    /// <inheritdoc />
    public string Description => "Lists, installs, inspects, and removes local skills.";

    /// <inheritdoc />
    public string CommandName => Name;

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command(Name, Description);
        command.Subcommands.Add(BuildListCommand(globalOptions));
        command.Subcommands.Add(BuildShowCommand(globalOptions));
        command.Subcommands.Add(BuildInstallCommand(globalOptions));
        command.Subcommands.Add(BuildUninstallCommand(globalOptions));
        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    /// <inheritdoc />
    public Task<int> ExecuteAsync(SlashCommandParseResult command, CommandExecutionContext context, CancellationToken cancellationToken)
        => command.Arguments.Length switch
        {
            0 => ExecuteListAsync(context, cancellationToken),
            _ when string.Equals(command.Arguments[0], "list", StringComparison.OrdinalIgnoreCase)
                => ExecuteListAsync(context, cancellationToken),
            _ when string.Equals(command.Arguments[0], "show", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 2
                => ExecuteShowAsync(command.Arguments[1], context, cancellationToken),
            _ when string.Equals(command.Arguments[0], "install", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 2
                => ExecuteInstallAsync(command.Arguments[1], context, cancellationToken),
            _ when string.Equals(command.Arguments[0], "uninstall", StringComparison.OrdinalIgnoreCase) && command.Arguments.Length >= 2
                => ExecuteUninstallAsync(command.Arguments[1], context, cancellationToken),
            _ => ExecuteListAsync(context, cancellationToken)
        };

    private Command BuildListCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("list", "Lists installed skills.");
        command.SetAction((parseResult, cancellationToken) => ExecuteListAsync(globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private Command BuildShowCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("show", "Shows a skill manifest and prompt.");
        var idOption = new Option<string>("--id") { Required = true, Description = "Skill id or name." };
        command.Options.Add(idOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteShowAsync(parseResult.GetValue(idOption)!, globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private Command BuildInstallCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("install", "Installs a skill from a JSON manifest.");
        var manifestOption = new Option<string>("--manifest") { Required = true, Description = "Path to a serialized SkillInstallRequest JSON document." };
        command.Options.Add(manifestOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteInstallAsync(parseResult.GetValue(manifestOption)!, globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private Command BuildUninstallCommand(GlobalCliOptions globalOptions)
    {
        var command = new Command("uninstall", "Removes an installed skill.");
        var idOption = new Option<string>("--id") { Required = true, Description = "Skill id." };
        command.Options.Add(idOption);
        command.SetAction((parseResult, cancellationToken) => ExecuteUninstallAsync(parseResult.GetValue(idOption)!, globalOptions.Resolve(parseResult), cancellationToken));
        return command;
    }

    private async Task<int> ExecuteListAsync(CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var skills = await skillRegistry.ListAsync(context.WorkingDirectory, cancellationToken).ConfigureAwait(false);
        var result = new CommandResult(
            true,
            0,
            context.OutputFormat,
            skills.Count == 0 ? "No skills installed." : $"{skills.Count} skill(s).",
            JsonSerializer.Serialize(skills.ToList(), ProtocolJsonContext.Default.ListSkillDefinition));
        await outputRendererDispatcher.RenderCommandResultAsync(result, context.OutputFormat, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteShowAsync(string id, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var skill = await skillRegistry.ResolveAsync(context.WorkingDirectory, id, cancellationToken).ConfigureAwait(false);
        if (skill is null)
        {
            await outputRendererDispatcher.RenderCommandResultAsync(
                new CommandResult(false, 1, context.OutputFormat, $"Skill '{id}' was not found.", null),
                context.OutputFormat,
                cancellationToken).ConfigureAwait(false);
            return 1;
        }

        var payload = new SkillInspectionRecord(skill.Definition, skill.PromptTemplate, skill.Metadata);
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"{skill.Definition.Id}: {skill.Definition.Description}", JsonSerializer.Serialize(payload, ProtocolJsonContext.Default.SkillInspectionRecord)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteInstallAsync(string manifestPath, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var manifestJson = await fileSystem.ReadAllTextIfExistsAsync(manifestPath, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Skill manifest '{manifestPath}' was not found.");
        var request = JsonSerializer.Deserialize<SkillInstallRequest>(manifestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidOperationException($"Skill manifest '{manifestPath}' could not be parsed.");
        var installed = await skillRegistry.InstallAsync(context.WorkingDirectory, request, cancellationToken).ConfigureAwait(false);
        var payload = new SkillInspectionRecord(installed.Definition, installed.PromptTemplate, installed.Metadata);
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(true, 0, context.OutputFormat, $"Installed skill '{installed.Definition.Id}'.", JsonSerializer.Serialize(payload, ProtocolJsonContext.Default.SkillInspectionRecord)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private async Task<int> ExecuteUninstallAsync(string id, CommandExecutionContext context, CancellationToken cancellationToken)
    {
        var removed = await skillRegistry.UninstallAsync(context.WorkingDirectory, id, cancellationToken).ConfigureAwait(false);
        var exitCode = removed ? 0 : 1;
        await outputRendererDispatcher.RenderCommandResultAsync(
            new CommandResult(
                removed,
                exitCode,
                context.OutputFormat,
                removed ? $"Removed skill '{id}'." : $"Skill '{id}' was not found.",
                JsonSerializer.Serialize(new Dictionary<string, string> { ["id"] = id }, ProtocolJsonContext.Default.DictionaryStringString)),
            context.OutputFormat,
            cancellationToken).ConfigureAwait(false);
        return exitCode;
    }
}
