namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents a persisted memory artifact associated with the project or session.
/// </summary>
/// <param name="Id">The unique memory identifier.</param>
/// <param name="Scope">The scope or namespace for the memory artifact.</param>
/// <param name="Content">The memory content payload.</param>
/// <param name="Source">The source that produced the memory artifact.</param>
/// <param name="UpdatedAtUtc">The UTC timestamp when the memory artifact was last updated.</param>
/// <param name="Tags">Optional tags associated with the memory artifact.</param>
/// <param name="Metadata">Additional machine-readable metadata.</param>
public sealed record ProjectMemory(
    string Id,
    string Scope,
    string Content,
    string Source,
    DateTimeOffset UpdatedAtUtc,
    string[]? Tags,
    Dictionary<string, string>? Metadata);
