using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Plugins.Models;

/// <summary>
/// Describes a single tool exposed by a plugin manifest.
/// </summary>
/// <param name="Name">The stable tool name.</param>
/// <param name="Description">The tool description presented to the agent.</param>
/// <param name="InputDescription">A concise JSON input description.</param>
/// <param name="Tags">The discoverability tags associated with the tool.</param>
/// <param name="IsDestructive">Indicates whether the tool mutates workspace or environment state.</param>
/// <param name="RequiresApproval">Indicates whether the tool should require approval by default.</param>
/// <param name="SourcePluginId">The plugin identifier that owns the tool, when known.</param>
/// <param name="InputTypeName">Optional argument contract display name surfaced in tool metadata.</param>
/// <param name="InputSchemaJson">Optional JSON Schema or example JSON shape for tool arguments.</param>
/// <param name="Trust">The manifest trust tier propagated for permission evaluation.</param>
public sealed record PluginToolDescriptor(
    string Name,
    string Description,
    string InputDescription,
    string[]? Tags,
    bool IsDestructive = false,
    bool RequiresApproval = false,
    string? SourcePluginId = null,
    string? InputTypeName = null,
    string? InputSchemaJson = null,
    PluginTrustLevel Trust = PluginTrustLevel.Untrusted);
