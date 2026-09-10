using System.Text.Json;
using System.Text.Json.Nodes;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.Sessions.Storage;

/// <summary>
/// Applies versioned compatibility migrations to durable session snapshots.
/// </summary>
internal static class SessionSnapshotSerializer
{
    internal const int CurrentSchemaVersion = 1;

    public static string Serialize(ConversationSession session)
    {
        var node = JsonSerializer.SerializeToNode(session, ProtocolJsonContext.Default.ConversationSession)
            ?? throw new JsonException("The session snapshot could not be serialized.");
        node["schemaVersion"] = CurrentSchemaVersion;
        return node.ToJsonString(ProtocolJsonContext.Default.Options);
    }

    public static ConversationSession? Deserialize(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        var node = JsonNode.Parse(payload) as JsonObject
            ?? throw new JsonException("The session snapshot root must be a JSON object.");
        var schemaVersion = 0;
        if (node["schemaVersion"] is JsonValue schemaVersionNode
            && !schemaVersionNode.TryGetValue<int>(out schemaVersion))
        {
            throw new JsonException("Session snapshot schemaVersion must be an integer.");
        }
        if (schemaVersion > CurrentSchemaVersion)
        {
            throw new JsonException($"Session snapshot schema version {schemaVersion} is newer than supported version {CurrentSchemaVersion}.");
        }

        if (schemaVersion == 0)
        {
            MigrateVersionZero(node);
        }

        return node.Deserialize(ProtocolJsonContext.Default.ConversationSession);
    }

    private static void MigrateVersionZero(JsonObject node)
    {
        if (node["permissionMode"] is JsonValue permissionModeValue
            && permissionModeValue.TryGetValue<string>(out var permissionMode))
        {
            node["permissionMode"] = permissionMode.Trim().ToLowerInvariant() switch
            {
                "prompt" or "autoapprovesafe" or "auto-approve-safe" => "workspaceWrite",
                "fulltrust" or "full-trust" => "dangerFullAccess",
                _ => permissionMode,
            };
        }

        node["schemaVersion"] = CurrentSchemaVersion;
    }
}
