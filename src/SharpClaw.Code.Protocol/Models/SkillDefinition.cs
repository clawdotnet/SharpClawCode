namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Describes a skill definition available to the runtime.
/// </summary>
/// <param name="Id">The unique skill identifier.</param>
/// <param name="Name">The human-friendly skill name.</param>
/// <param name="Description">A concise skill description.</param>
/// <param name="Source">The source path, package, or origin for the skill.</param>
/// <param name="Version">The skill version, if known.</param>
/// <param name="Tags">Optional tags associated with the skill.</param>
/// <param name="EntryPoint">The skill entry point or file, if known.</param>
public sealed record SkillDefinition(
    string Id,
    string Name,
    string Description,
    string Source,
    string? Version,
    string[]? Tags,
    string? EntryPoint);
