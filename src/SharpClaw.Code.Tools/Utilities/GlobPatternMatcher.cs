using System.Text.RegularExpressions;

namespace SharpClaw.Code.Tools.Utilities;

internal static partial class GlobPatternMatcher
{
    public static bool IsMatch(string pattern, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalizedPattern = NormalizePattern(pattern);
        var normalizedPath = relativePath.Replace('\\', '/');
        return CreateRegex(normalizedPattern).IsMatch(normalizedPath);
    }

    private static string NormalizePattern(string pattern)
    {
        var normalized = pattern.Replace('\\', '/');
        if (!normalized.StartsWith("**/", StringComparison.Ordinal) && !normalized.Contains('/'))
        {
            normalized = $"**/{normalized}";
        }

        return normalized;
    }

    private static Regex CreateRegex(string pattern)
    {
        var regexPattern = Regex.Escape(pattern)
            .Replace(@"\*\*/", @"(?:.*/)?")
            .Replace(@"\*\*", @".*")
            .Replace(@"\*", @"[^/]*")
            .Replace(@"\?", @"[^/]");

        return new Regex($"^{regexPattern}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }
}
