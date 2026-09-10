using System.Text.Json;
using System.Text.Json.Serialization;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Serialization;

/// <summary>
/// Reads canonical permission modes while preserving compatibility with pre-release names.
/// </summary>
public sealed class PermissionModeJsonConverter : JsonConverter<PermissionMode>
{
    /// <inheritdoc />
    public override PermissionMode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numericValue)
            && Enum.IsDefined(typeof(PermissionMode), numericValue))
        {
            return (PermissionMode)numericValue;
        }

        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Permission mode must be a string or a valid numeric enum value.");
        }

        return reader.GetString()?.Trim().ToLowerInvariant() switch
        {
            "readonly" or "read-only" => PermissionMode.ReadOnly,
            "workspacewrite" or "workspace-write" or "prompt" or "autoapprovesafe" or "auto-approve-safe" => PermissionMode.WorkspaceWrite,
            "dangerfullaccess" or "danger-full-access" or "fulltrust" or "full-trust" => PermissionMode.DangerFullAccess,
            var value => throw new JsonException($"Unknown permission mode '{value}'."),
        };
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, PermissionMode value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            PermissionMode.ReadOnly => "readOnly",
            PermissionMode.WorkspaceWrite => "workspaceWrite",
            PermissionMode.DangerFullAccess => "dangerFullAccess",
            _ => throw new JsonException($"Unknown permission mode value '{value}'."),
        });
    }
}
