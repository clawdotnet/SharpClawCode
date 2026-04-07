using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Memory.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Services;

/// <summary>
/// Loads local SharpClaw memory documents and repo settings from the workspace.
/// </summary>
public sealed class ProjectMemoryService(
    IFileSystem fileSystem,
    IPathService pathService,
    ISystemClock systemClock) : IProjectMemoryService
{
    private const string SharpClawDirectoryName = ".sharpclaw";
    private const string MemoryFileName = "SHARPCLAW.md";
    private const string SettingsFileName = "settings.json";

    /// <inheritdoc />
    public async Task<ProjectMemoryContext> BuildContextAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        var sharpClawDirectory = pathService.Combine(normalizedWorkspaceRoot, SharpClawDirectoryName);
        var memoryPath = pathService.Combine(sharpClawDirectory, MemoryFileName);
        var settingsPath = pathService.Combine(sharpClawDirectory, SettingsFileName);

        var memoryContent = await fileSystem.ReadAllTextIfExistsAsync(memoryPath, cancellationToken).ConfigureAwait(false);
        var settingsContent = await fileSystem.ReadAllTextIfExistsAsync(settingsPath, cancellationToken).ConfigureAwait(false);

        var memory = string.IsNullOrWhiteSpace(memoryContent)
            ? null
            : new ProjectMemory(
                Id: "project-memory",
                Scope: "project",
                Content: memoryContent.Trim(),
                Source: memoryPath,
                UpdatedAtUtc: systemClock.UtcNow,
                Tags: ["sharpclaw", "project"],
                Metadata: new Dictionary<string, string>
                {
                    ["path"] = memoryPath
                });

        return new ProjectMemoryContext(memory, ParseSettings(settingsContent));
    }

    private static IReadOnlyDictionary<string, string> ParseSettings(string? settingsContent)
    {
        if (string.IsNullOrWhiteSpace(settingsContent))
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        using var document = JsonDocument.Parse(settingsContent);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            settings[property.Name] = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => property.Value.ToString(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => property.Value.GetRawText()
            };
        }

        return settings;
    }
}
