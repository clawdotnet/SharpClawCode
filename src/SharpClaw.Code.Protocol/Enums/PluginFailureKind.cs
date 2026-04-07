using System.Text.Json.Serialization;

namespace SharpClaw.Code.Protocol.Enums;

/// <summary>
/// Describes the coarse failure category for a plugin lifecycle transition.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PluginFailureKind>))]
public enum PluginFailureKind
{
    /// <summary>
    /// No failure has been recorded.
    /// </summary>
    [JsonStringEnumMemberName("none")]
    None,

    /// <summary>
    /// The manifest failed validation.
    /// </summary>
    [JsonStringEnumMemberName("validation")]
    Validation,

    /// <summary>
    /// The loader could not prepare the plugin successfully.
    /// </summary>
    [JsonStringEnumMemberName("load")]
    Load,

    /// <summary>
    /// The plugin process failed while executing a request.
    /// </summary>
    [JsonStringEnumMemberName("execution")]
    Execution
}
