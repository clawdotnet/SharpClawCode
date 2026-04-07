using System.Text.Json.Serialization;

namespace SharpClaw.Code.Protocol.Enums;

/// <summary>
/// Indicates the preferred output representation for commands and tools.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<OutputFormat>))]
public enum OutputFormat
{
    /// <summary>
    /// Produces human-readable plain text.
    /// </summary>
    [JsonStringEnumMemberName("text")]
    Text,

    /// <summary>
    /// Produces machine-readable JSON.
    /// </summary>
    [JsonStringEnumMemberName("json")]
    Json,

    /// <summary>
    /// Produces Markdown-formatted output.
    /// </summary>
    [JsonStringEnumMemberName("markdown")]
    Markdown,
}
