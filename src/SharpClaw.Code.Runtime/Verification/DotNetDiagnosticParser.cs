using System.Text.RegularExpressions;
using SharpClaw.Code.Protocol.Models;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>Recognizes compiler/MSBuild diagnostics without deriving success from log content.</summary>
public sealed partial class DotNetDiagnosticParser : IDotNetDiagnosticParser
{
    /// <inheritdoc />
    public VerificationDiagnostic[] Parse(string standardOutput, string standardError, string workspaceRoot)
    {
        var result = new List<VerificationDiagnostic>();
        foreach (var line in (standardOutput + "\n" + standardError).Split('\n'))
        {
            var match = DiagnosticPattern().Match(line.TrimEnd('\r'));
            if (!match.Success) continue;
            var origin = match.Groups["origin"].Value.Trim();
            var location = LocationPattern().Match(origin);
            string? path = location.Success ? location.Groups["path"].Value : origin;
            if (path is "" or "MSBUILD" || path?.StartsWith("CSC", StringComparison.OrdinalIgnoreCase) == true) path = null;
            result.Add(new(Relative(path), Number(location.Groups["line"].Value), Number(location.Groups["column"].Value), match.Groups["code"].Value, match.Groups["severity"].Value.ToLowerInvariant(), Bound(match.Groups["message"].Value.Trim(), 4000), Relative(match.Groups["project"].Value)));
        }
        return result.Distinct().Take(500).ToArray();
        string? Relative(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var normalized = path.Replace('\\', '/');
            var root = workspaceRoot.Replace('\\', '/').TrimEnd('/') + "/";
            return normalized.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ? normalized[root.Length..] : normalized;
        }
    }
    internal static string Bound(string value, int limit) => value.Length <= limit ? value : value[..limit] + "\n[truncated]";
    private static int? Number(string value) => int.TryParse(value, out var number) ? number : null;
    [GeneratedRegex(@"^\s*(?:(?<origin>.*?)\s*:\s*)?(?<severity>error|warning)\s+(?<code>[A-Za-z]+\d+)\s*:\s*(?<message>.*?)(?:\s+\[(?<project>[^\]]+)\])?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex DiagnosticPattern();
    [GeneratedRegex(@"^(?<path>.*)\((?<line>\d+),(?<column>\d+)(?:,\d+,\d+)?\)$")]
    private static partial Regex LocationPattern();
}
