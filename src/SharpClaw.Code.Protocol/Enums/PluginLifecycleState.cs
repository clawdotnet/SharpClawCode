using System.Text.Json.Serialization;

namespace SharpClaw.Code.Protocol.Enums;

/// <summary>
/// Represents the lifecycle state of a plugin within the host.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PluginLifecycleState>))]
public enum PluginLifecycleState
{
    /// <summary>
    /// The plugin has been discovered but not loaded.
    /// </summary>
    [JsonStringEnumMemberName("discovered")]
    Discovered,

    /// <summary>
    /// The plugin has been loaded into the runtime.
    /// </summary>
    [JsonStringEnumMemberName("loaded")]
    Loaded,

    /// <summary>
    /// The plugin is enabled and available for use.
    /// </summary>
    [JsonStringEnumMemberName("enabled")]
    Enabled,

    /// <summary>
    /// The plugin is intentionally disabled.
    /// </summary>
    [JsonStringEnumMemberName("disabled")]
    Disabled,

    /// <summary>
    /// The plugin failed to load or execute correctly.
    /// </summary>
    [JsonStringEnumMemberName("faulted")]
    Faulted,
}
