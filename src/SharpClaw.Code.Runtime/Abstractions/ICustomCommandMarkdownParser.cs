using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Parses a markdown command file into a <see cref="CustomCommandDefinition"/> or issues.
/// </summary>
public interface ICustomCommandMarkdownParser
{
    /// <summary>
    /// Parses command content from <paramref name="absolutePath"/>.
    /// </summary>
    /// <param name="name">Command name (file stem).</param>
    /// <param name="absolutePath">Path to the markdown file.</param>
    /// <param name="scope">Global vs workspace catalog.</param>
    /// <param name="markdownText">Raw file text.</param>
    /// <returns>Definition when valid; otherwise issues only.</returns>
    (CustomCommandDefinition? Definition, IReadOnlyList<CustomCommandDiscoveryIssue> Issues) Parse(
        string name,
        string absolutePath,
        CustomCommandSourceScope scope,
        string markdownText);
}
