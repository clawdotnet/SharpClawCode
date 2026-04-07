namespace SharpClaw.Code.Skills.Models;

/// <summary>
/// Describes a skill to install into the local SharpClaw skills directory.
/// </summary>
/// <param name="Id">The stable skill identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Description">The skill description.</param>
/// <param name="PromptTemplate">The prompt template content.</param>
/// <param name="Version">The skill version.</param>
/// <param name="Tags">The skill tags.</param>
/// <param name="Metadata">Optional machine-readable metadata.</param>
public sealed record SkillInstallRequest(
    string Id,
    string Name,
    string Description,
    string PromptTemplate,
    string? Version,
    string[]? Tags,
    IReadOnlyDictionary<string, string>? Metadata);
