using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Identifies a tool implementation that is surfaced from an installed plugin manifest.
/// </summary>
/// <param name="PluginId">The originating plugin identifier.</param>
/// <param name="Trust">The manifest-declared trust tier for policy evaluation.</param>
public sealed record PluginToolSource(string PluginId, PluginTrustLevel Trust);
