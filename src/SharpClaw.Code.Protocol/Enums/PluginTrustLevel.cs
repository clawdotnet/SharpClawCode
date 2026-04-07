using System.Text.Json.Serialization;

namespace SharpClaw.Code.Protocol.Enums;

/// <summary>
/// Declares how much trust the host grants a plugin for policy and approval defaults.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PluginTrustLevel>))]
public enum PluginTrustLevel
{
    /// <summary>
    /// The plugin is treated as untrusted; destructive capabilities require explicit approval metadata.
    /// </summary>
    [JsonStringEnumMemberName("untrusted")]
    Untrusted,

    /// <summary>
    /// The plugin is trusted for routine workspace-scoped operations after local install.
    /// </summary>
    [JsonStringEnumMemberName("workspaceTrusted")]
    WorkspaceTrusted,

    /// <summary>
    /// The plugin is highly trusted (for example developer-signed or org-approved); use sparingly.
    /// </summary>
    [JsonStringEnumMemberName("developerTrusted")]
    DeveloperTrusted
}
