using System.Text.Json.Serialization;

namespace SharpClaw.Code.Protocol.Operational;

/// <summary>
/// Normalized status for a single operational diagnostic check.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<OperationalCheckStatus>))]
public enum OperationalCheckStatus
{
    /// <summary>The check passed.</summary>
    [JsonStringEnumMemberName("ok")]
    Ok,

    /// <summary>The check passed with a non-blocking concern.</summary>
    [JsonStringEnumMemberName("warn")]
    Warn,

    /// <summary>The check failed.</summary>
    [JsonStringEnumMemberName("error")]
    Error,

    /// <summary>The check was not applicable or skipped.</summary>
    [JsonStringEnumMemberName("skipped")]
    Skipped
}
