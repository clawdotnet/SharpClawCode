using System.Text;

namespace SharpClaw.Code.Runtime.CustomCommands;

/// <summary>
/// Substitutes <c>$ARGUMENTS</c> and <c>$1..$n</c> placeholders in a command template.
/// </summary>
public static class CustomCommandTemplateExpander
{
    /// <summary>
    /// Expands template placeholders using whitespace-separated arguments (quotes supported).
    /// </summary>
    public static string Expand(string template, string argumentsLine)
    {
        ArgumentNullException.ThrowIfNull(template);
        var parts = SplitArguments(argumentsLine);
        var result = template.Replace("$ARGUMENTS", argumentsLine, StringComparison.Ordinal);
        for (var i = 0; i < parts.Length; i++)
        {
            result = result.Replace($"${i + 1}", parts[i], StringComparison.Ordinal);
        }

        return result;
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
}
