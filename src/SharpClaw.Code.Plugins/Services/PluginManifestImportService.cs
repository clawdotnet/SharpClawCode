using System.Text;
using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Plugins.Services;

/// <summary>
/// Adapts deterministic external plugin manifests into SharpClaw-local install requests.
/// </summary>
public sealed class PluginManifestImportService(IFileSystem fileSystem) : IPluginManifestImportService
{
    /// <inheritdoc />
    public async Task<(PluginInstallRequest Request, ImportedPluginManifestResult Result)> ImportAsync(
        string manifestPath,
        string? format,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);

        var content = await fileSystem.ReadAllTextIfExistsAsync(manifestPath, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Manifest '{manifestPath}' was not found.");

        using var document = JsonDocument.Parse(content);
        var sourceFormat = string.IsNullOrWhiteSpace(format) ? "auto" : format.Trim().ToLowerInvariant();
        List<string> warnings = [];
        var sharpClawManifest = TryImportSharpClaw(document.RootElement);
        var externalManifest = sourceFormat is "auto" or "external"
            ? ImportExternal(document.RootElement, out warnings)
            : null;
        var manifest = sharpClawManifest
            ?? externalManifest
            ?? throw new InvalidOperationException("Manifest format is not supported. Provide a SharpClaw manifest or a deterministic external manifest with name and command fields.");

        return (
            new PluginInstallRequest(manifest, content),
            new ImportedPluginManifestResult(
                sourceFormat,
                manifest.Id,
                manifest.Name,
                manifest.Version,
                manifest.EntryPoint,
                manifest.Tools?.Length ?? 0,
                warnings.ToArray()));
    }

    private static PluginManifest? TryImportSharpClaw(JsonElement root)
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(root.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return manifest is not null && !string.IsNullOrWhiteSpace(manifest.Id) && !string.IsNullOrWhiteSpace(manifest.EntryPoint)
            ? manifest
            : null;
    }

    private static PluginManifest? ImportExternal(JsonElement root, out List<string> warnings)
    {
        warnings = [];

        var name = ReadString(root, "name");
        var id = NormalizeId(ReadString(root, "id") ?? name);
        var entryPoint = ReadString(root, "entryPoint") ?? ReadString(root, "command") ?? ReadString(root, "binary");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(entryPoint))
        {
            return null;
        }

        var version = ReadString(root, "version") ?? "0.0.0";
        var description = ReadString(root, "description");
        var arguments = ReadStringArray(root, "arguments") ?? ReadStringArray(root, "args");
        var capabilities = ReadStringArray(root, "capabilities");
        var tools = ReadTools(root, warnings);
        var publisherId = ReadString(root, "publisher") ?? ReadString(root, "publisherId");

        foreach (var property in root.EnumerateObject())
        {
            if (property.NameEquals("id")
                || property.NameEquals("name")
                || property.NameEquals("version")
                || property.NameEquals("description")
                || property.NameEquals("entryPoint")
                || property.NameEquals("command")
                || property.NameEquals("binary")
                || property.NameEquals("arguments")
                || property.NameEquals("args")
                || property.NameEquals("capabilities")
                || property.NameEquals("tools")
                || property.NameEquals("publisher")
                || property.NameEquals("publisherId"))
            {
                continue;
            }

            warnings.Add($"Preserved unsupported field '{property.Name}' only in package content.");
        }

        return new PluginManifest(
            id,
            name,
            version,
            description,
            entryPoint,
            arguments,
            capabilities,
            tools,
            PluginTrustLevel.Untrusted,
            publisherId,
            null);
    }

    private static PluginToolDescriptor[]? ReadTools(JsonElement root, List<string> warnings)
    {
        if (!root.TryGetProperty("tools", out var toolsElement) || toolsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var tools = new List<PluginToolDescriptor>();
        foreach (var item in toolsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                warnings.Add("Ignored non-object tool entry.");
                continue;
            }

            var name = ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                warnings.Add("Ignored tool entry without a name.");
                continue;
            }

            tools.Add(new PluginToolDescriptor(
                name,
                ReadString(item, "description") ?? name,
                ReadString(item, "inputDescription") ?? ReadString(item, "input_schema") ?? "json",
                ReadStringArray(item, "tags"),
                ReadBool(item, "isDestructive"),
                ReadBool(item, "requiresApproval"),
                null,
                ReadString(item, "inputTypeName"),
                item.TryGetProperty("inputSchemaJson", out var schemaJson)
                    ? schemaJson.GetRawText()
                    : item.TryGetProperty("inputSchema", out var inputSchema)
                        ? inputSchema.GetRawText()
                        : null));
        }

        return tools.Count == 0 ? null : tools.ToArray();
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string[]? ReadStringArray(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return value.EnumerateArray()
            .Where(static item => item.ValueKind == JsonValueKind.String)
            .Select(static item => item.GetString()!)
            .Where(static item => !string.IsNullOrWhiteSpace(item))
            .ToArray();
    }

    private static bool ReadBool(JsonElement element, string name)
        => element.TryGetProperty(name, out var value)
            && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            && value.GetBoolean();

    private static string NormalizeId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "plugin-" + Guid.NewGuid().ToString("N")[..8];
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim().ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '-');
        }

        var normalized = builder.ToString().Trim('-');
        while (normalized.Contains("--", StringComparison.Ordinal))
        {
            normalized = normalized.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(normalized) ? $"plugin-{Guid.NewGuid():N}".Substring(0, 15) : normalized;
    }
}
