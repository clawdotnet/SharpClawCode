using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Describes a plugin that can be discovered or loaded by the runtime.
/// </summary>
/// <param name="Id">The unique plugin identifier.</param>
/// <param name="Name">The plugin display name.</param>
/// <param name="Version">The plugin version string.</param>
/// <param name="EntryPoint">The entry point or manifest location for the plugin.</param>
/// <param name="Description">A concise description of the plugin.</param>
/// <param name="Capabilities">The capabilities exposed by the plugin.</param>
/// <param name="Trust">The declared trust tier used by policy surfaces.</param>
public sealed record PluginDescriptor(
    string Id,
    string Name,
    string Version,
    string EntryPoint,
    string? Description,
    string[]? Capabilities,
    PluginTrustLevel Trust = PluginTrustLevel.Untrusted);
