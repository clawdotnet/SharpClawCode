using System.Text;
using System.Text.RegularExpressions;

namespace SharpClaw.Code.Runtime.CustomCommands;

/// <summary>
/// Substitutes <c>$ARGUMENTS</c> and <c>$1..$n</c> placeholders in a command template.
/// </summary>
public static partial class CustomCommandTemplateExpander
{
    /// <summary>
    /// Expands template placeholders using whitespace-separated arguments (quotes supported).
    /// </summary>
    public static string Expand(string template, string argumentsLine)
    {
        ArgumentNullException.ThrowIfNull(template);
        var parts = SplitArguments(argumentsLine);
        return PlaceholderRegex().Replace(
            template,
            match =>
            {
                var token = match.Groups[1].Value;
                if (string.Equals(token, "ARGUMENTS", StringComparison.Ordinal))
                {
                    return argumentsLine;
                }

                return int.TryParse(token, out var index) && index > 0 && index <= parts.Length
                    ? parts[index - 1]
                    : match.Value;
            });
    }

    private static string[] SplitArguments(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return [];
        }

        var results = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0)
                {
                    results.Add(current.ToString());
                    current.Clear();
                }

                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0)
        {
            results.Add(current.ToString());
        }

        return results.ToArray();
    }

    [GeneratedRegex(@"\$(ARGUMENTS|[1-9][0-9]*)", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderRegex();
}
