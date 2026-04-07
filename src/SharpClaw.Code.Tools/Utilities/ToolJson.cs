using System.Text.Json;

namespace SharpClaw.Code.Tools.Utilities;

internal static class ToolJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static T Deserialize<T>(string json)
    {
        var value = JsonSerializer.Deserialize<T>(json, SerializerOptions);
        if (value is null)
        {
            throw new InvalidOperationException($"Unable to deserialize tool arguments as {typeof(T).Name}.");
        }

        return value;
    }

    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, SerializerOptions);
}
