using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Plugins.Models;

/// <summary>
/// Represents the manifest persisted for a locally installed plugin.
/// </summary>
/// <param name="Id">The stable plugin identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Version">The semantic version string.</param>
/// <param name="Description">A concise plugin description.</param>
/// <param name="EntryPoint">The executable or script entry point.</param>
/// <param name="Arguments">The default process arguments.</param>
/// <param name="Capabilities">The declared plugin capabilities.</param>
/// <param name="Tools">The tool descriptors exposed by the plugin, if any.</param>
/// <param name="Trust">The declared trust tier for permission policy.</param>
/// <param name="PublisherId">An optional publisher or provenance identifier.</param>
/// <param name="SignatureHint">Optional opaque hint for future signature verification workflows.</param>
public sealed record PluginManifest(
    string Id,
    string Name,
    string Version,
    string? Description,
    string EntryPoint,
    string[]? Arguments,
    string[]? Capabilities,
    PluginToolDescriptor[]? Tools,
    PluginTrustLevel Trust = PluginTrustLevel.Untrusted,
    string? PublisherId = null,
    string? SignatureHint = null);
