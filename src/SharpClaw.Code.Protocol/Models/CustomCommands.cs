using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Origin of a discovered custom command definition.
/// </summary>
public enum CustomCommandSourceScope
{
    /// <summary>User-level <c>~/.sharpclaw/commands</c>.</summary>
    Global,

    /// <summary>Workspace <c>.sharpclaw/commands</c>.</summary>
    Workspace,
}

/// <summary>
/// A single non-fatal discovery problem for a command file or the catalog.
/// </summary>
/// <param name="Path">Source path, if any.</param>
/// <param name="Message">Human-readable explanation.</param>
public sealed record CustomCommandDiscoveryIssue(string? Path, string Message);

/// <summary>
/// Parsed custom command from markdown with optional YAML-like frontmatter.
/// </summary>
/// <param name="Name">Slash/CLI name derived from the file stem.</param>
/// <param name="Description">From frontmatter.</param>
/// <param name="AgentId">Optional agent id override.</param>
/// <param name="Model">Optional model override.</param>
/// <param name="PermissionMode">Optional permission mode override; null keeps request/session mode.</param>
/// <param name="PrimaryModeOverride">Optional primary mode override.</param>
/// <param name="Arguments">Optional opaque argument hints.</param>
/// <param name="Tags">Optional tags.</param>
/// <param name="TemplateBody">Prompt template after frontmatter.</param>
/// <param name="SourcePath">Absolute or normalized path to the source file.</param>
/// <param name="SourceScope">Global vs workspace catalog.</param>
/// <param name="ExtensionMetadata">Unknown frontmatter keys preserved for forward compatibility.</param>
public sealed record CustomCommandDefinition(
    string Name,
    string? Description,
    string? AgentId,
    string? Model,
    PermissionMode? PermissionMode,
    PrimaryMode? PrimaryModeOverride,
    Dictionary<string, string>? Arguments,
    List<string>? Tags,
    string TemplateBody,
    string SourcePath,
    CustomCommandSourceScope SourceScope,
    Dictionary<string, string>? ExtensionMetadata);

/// <summary>
/// Snapshot of the custom command catalog after discovery.
/// </summary>
public sealed record CustomCommandCatalogSnapshot(
    List<CustomCommandDefinition> Commands,
    List<CustomCommandDiscoveryIssue> Issues,
    DateTimeOffset GeneratedAtUtc);
