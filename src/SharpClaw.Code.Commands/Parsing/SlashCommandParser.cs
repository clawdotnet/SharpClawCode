using System.Text.RegularExpressions;
using SharpClaw.Code.Commands.Models;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Parses slash commands from REPL input.
/// </summary>
public sealed partial class SlashCommandParser
{
    /// <summary>
    /// Parses input text into a slash command result.
    /// </summary>
    /// <param name="input">The raw input text.</param>
    /// <returns>The parsed slash command result.</returns>
    public SlashCommandParseResult Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new SlashCommandParseResult(false, null, []);
        }

        var trimmed = input.Trim();
        if (!trimmed.StartsWith('/'))
        {
            return new SlashCommandParseResult(false, null, []);
        }

        var tokens = TokenRegex().Matches(trimmed[1..]).Select(match => match.Value.Trim('"')).Where(token => !string.IsNullOrWhiteSpace(token)).ToArray();
        if (tokens.Length == 0)
        {
            return new SlashCommandParseResult(true, string.Empty, []);
        }

        return new SlashCommandParseResult(true, tokens[0].ToLowerInvariant(), tokens.Skip(1).ToArray());
    }

    [GeneratedRegex("\"[^\"]+\"|\\S+")]
    private static partial Regex TokenRegex();
}
