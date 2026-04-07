namespace SharpClaw.Code.Commands.Models;

/// <summary>
/// Represents the parsed result of user input that may contain a slash command.
/// </summary>
/// <param name="IsSlashCommand">Indicates whether the input is a slash command.</param>
/// <param name="CommandName">The slash command name, if any.</param>
/// <param name="Arguments">The parsed slash command arguments.</param>
public sealed record SlashCommandParseResult(
    bool IsSlashCommand,
    string? CommandName,
    string[] Arguments);
