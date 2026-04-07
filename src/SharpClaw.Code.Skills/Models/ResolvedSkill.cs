using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Skills.Models;

/// <summary>
/// Represents a resolved skill definition with prompt template and execution metadata.
/// </summary>
/// <param name="Definition">The skill definition.</param>
/// <param name="PromptTemplate">The prompt template content.</param>
/// <param name="Metadata">Additional skill metadata.</param>
public sealed record ResolvedSkill(
    SkillDefinition Definition,
    string PromptTemplate,
    IReadOnlyDictionary<string, string> Metadata);
