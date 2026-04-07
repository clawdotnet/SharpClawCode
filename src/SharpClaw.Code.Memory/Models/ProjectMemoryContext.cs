using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Models;

/// <summary>
/// Represents the effective project memory and repo-local settings for a workspace.
/// </summary>
/// <param name="Memory">The loaded project memory document, if present.</param>
/// <param name="RepositorySettings">The repo-local settings discovered for the workspace.</param>
public sealed record ProjectMemoryContext(
    ProjectMemory? Memory,
    IReadOnlyDictionary<string, string> RepositorySettings)
{
    /// <summary>
    /// Renders the project memory context as a prompt-ready section.
    /// </summary>
    /// <returns>The prompt section text.</returns>
    public string RenderForPrompt()
    {
        var sections = new List<string>();
        if (Memory is not null && !string.IsNullOrWhiteSpace(Memory.Content))
        {
            sections.Add($"Project memory:\n{Memory.Content.Trim()}");
        }

        if (RepositorySettings.Count > 0)
        {
            sections.Add(
                "Repo-local settings:\n"
                + string.Join(Environment.NewLine, RepositorySettings.Select(pair => $"- {pair.Key}: {pair.Value}")));
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }
}
