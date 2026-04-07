namespace SharpClaw.Code.Runtime.Context;

/// <summary>
/// Represents the enriched prompt payload passed into agent execution.
/// </summary>
/// <param name="Prompt">The final prompt text.</param>
/// <param name="Metadata">The merged execution metadata.</param>
public sealed record PromptExecutionContext(
    string Prompt,
    IReadOnlyDictionary<string, string> Metadata);
