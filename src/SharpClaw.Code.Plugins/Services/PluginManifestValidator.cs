using System.Text.Json;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Plugins.Services;

/// <summary>
/// Validates plugin manifests before install or enable operations.
/// </summary>
public sealed class PluginManifestValidator
{
    private const int MaxPluginIdLength = 128;

    /// <summary>
    /// Validates the supplied plugin manifest.
    /// </summary>
    /// <param name="manifest">The manifest to validate.</param>
    public void Validate(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Id);
        if (manifest.Id is "." or "..")
        {
            throw new InvalidOperationException("Plugin id cannot be '.' or '..'.");
        }

        if (manifest.Id.Length > MaxPluginIdLength)
        {
            throw new InvalidOperationException($"Plugin id length cannot exceed {MaxPluginIdLength} characters.");
        }

        // Same rule the package catalog enforces on enable/disable/remove, so an installable plugin is always manageable.
        try
        {
            LocalPackagePath.ValidateId(manifest.Id);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException(
                $"Plugin id '{manifest.Id}' must contain only letters, digits, dots, hyphens, and underscores.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.Version);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifest.EntryPoint);
        if (manifest.EntryPoint.Length > 4096)
        {
            throw new InvalidOperationException("Plugin entryPoint is unreasonably long.");
        }

        var duplicateToolName = (manifest.Tools ?? [])
            .GroupBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicateToolName is not null)
        {
            throw new InvalidOperationException($"Plugin '{manifest.Id}' declares duplicate tool '{duplicateToolName.Key}'.");
        }

        if (manifest.Trust == PluginTrustLevel.Untrusted)
        {
            foreach (var tool in manifest.Tools ?? [])
            {
                if (tool.IsDestructive && !tool.RequiresApproval)
                {
                    throw new InvalidOperationException(
                        $"Plugin '{manifest.Id}' tool '{tool.Name}' is destructive while the plugin trust is Untrusted; set requiresApproval or reduce trust constraints.");
                }
            }
        }

        foreach (var tool in manifest.Tools ?? [])
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(tool.Name);
            ArgumentException.ThrowIfNullOrWhiteSpace(tool.Description);
            ArgumentException.ThrowIfNullOrWhiteSpace(tool.InputDescription);

            if (!string.IsNullOrWhiteSpace(tool.InputSchemaJson))
            {
                try
                {
                    using var document = JsonDocument.Parse(tool.InputSchemaJson);
                    if (document.RootElement.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
                    {
                        throw new InvalidOperationException(
                            $"Plugin '{manifest.Id}' tool '{tool.Name}' inputSchemaJson must be a JSON object or array.");
                    }
                }
                catch (JsonException ex)
                {
                    throw new InvalidOperationException(
                        $"Plugin '{manifest.Id}' tool '{tool.Name}' has invalid inputSchemaJson.", ex);
                }
            }
        }
    }
}
