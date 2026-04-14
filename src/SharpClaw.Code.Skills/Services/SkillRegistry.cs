using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Skills.Abstractions;
using SharpClaw.Code.Skills.Models;

namespace SharpClaw.Code.Skills.Services;

/// <summary>
/// Provides local skill install, list, and resolve operations for workspace skills.
/// </summary>
public sealed class SkillRegistry(
    IFileSystem fileSystem,
    IPathService pathService,
    ISystemClock systemClock) : ISkillRegistry
{
    private const string SharpClawDirectoryName = ".sharpclaw";
    private const string SkillsDirectoryName = "skills";
    private const string ManifestFileName = "skill.json";
    private const string PromptFileName = "prompt.txt";
    private const string DefaultExecutionRoute = "tool-agent-layer";

    /// <inheritdoc />
    public async Task<IReadOnlyList<SkillDefinition>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var skillsRoot = EnsureSkillsRoot(workspaceRoot);
        if (!fileSystem.DirectoryExists(skillsRoot))
        {
            return [];
        }

        var definitions = new List<SkillDefinition>();
        foreach (var directory in fileSystem.EnumerateDirectories(skillsRoot))
        {
            var resolved = await ResolveFromDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
            if (resolved is not null)
            {
                definitions.Add(resolved.Definition);
            }
        }

        return definitions
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<ResolvedSkill?> ResolveAsync(string workspaceRoot, string skillIdOrName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillIdOrName);

        var skillsRoot = EnsureSkillsRoot(workspaceRoot);
        if (!fileSystem.DirectoryExists(skillsRoot))
        {
            return null;
        }

        foreach (var directory in fileSystem.EnumerateDirectories(skillsRoot))
        {
            var resolved = await ResolveFromDirectoryAsync(directory, cancellationToken).ConfigureAwait(false);
            if (resolved is null)
            {
                continue;
            }

            if (string.Equals(resolved.Definition.Id, skillIdOrName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(resolved.Definition.Name, skillIdOrName, StringComparison.OrdinalIgnoreCase))
            {
                return resolved;
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<ResolvedSkill> InstallAsync(string workspaceRoot, SkillInstallRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Description);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PromptTemplate);

        var skillsRoot = EnsureSkillsRoot(workspaceRoot);
        fileSystem.CreateDirectory(skillsRoot);

        var skillDirectory = pathService.Combine(skillsRoot, request.Id);
        fileSystem.CreateDirectory(skillDirectory);

        var metadata = new Dictionary<string, string>(request.Metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal)
        {
            ["executionRoute"] = request.Metadata is not null && request.Metadata.TryGetValue("executionRoute", out var route)
                ? route
                : DefaultExecutionRoute,
            ["installedAtUtc"] = systemClock.UtcNow.ToString("O")
        };

        var definition = new SkillDefinition(
            Id: request.Id,
            Name: request.Name,
            Description: request.Description,
            Source: skillDirectory,
            Version: request.Version,
            Tags: request.Tags,
            EntryPoint: PromptFileName);

        await fileSystem.WriteAllTextAsync(
            pathService.Combine(skillDirectory, ManifestFileName),
            JsonSerializer.Serialize(new SkillManifest(definition, metadata), new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            }),
            cancellationToken).ConfigureAwait(false);

        await fileSystem.WriteAllTextAsync(
            pathService.Combine(skillDirectory, PromptFileName),
            request.PromptTemplate,
            cancellationToken).ConfigureAwait(false);

        return new ResolvedSkill(definition, request.PromptTemplate, metadata);
    }

    /// <inheritdoc />
    public Task<bool> UninstallAsync(string workspaceRoot, string skillId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(skillId);

        var skillDirectory = pathService.Combine(EnsureSkillsRoot(workspaceRoot), skillId);
        if (!fileSystem.DirectoryExists(skillDirectory))
        {
            return Task.FromResult(false);
        }

        fileSystem.DeleteDirectoryRecursive(skillDirectory);
        return Task.FromResult(true);
    }

    private async Task<ResolvedSkill?> ResolveFromDirectoryAsync(string directory, CancellationToken cancellationToken)
    {
        var manifestPath = pathService.Combine(directory, ManifestFileName);
        var promptPath = pathService.Combine(directory, PromptFileName);
        var manifestText = await fileSystem.ReadAllTextIfExistsAsync(manifestPath, cancellationToken).ConfigureAwait(false);
        var promptTemplate = await fileSystem.ReadAllTextIfExistsAsync(promptPath, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(manifestText) || string.IsNullOrWhiteSpace(promptTemplate))
        {
            return null;
        }

        var manifest = JsonSerializer.Deserialize<SkillManifest>(manifestText, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (manifest is null)
        {
            return null;
        }

        var definition = manifest.Definition with
        {
            Source = directory,
            EntryPoint = PromptFileName
        };

        return new ResolvedSkill(definition, promptTemplate, manifest.Metadata);
    }

    private string EnsureSkillsRoot(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        var normalizedWorkspaceRoot = pathService.GetFullPath(workspaceRoot);
        return pathService.Combine(normalizedWorkspaceRoot, SharpClawDirectoryName, SkillsDirectoryName);
    }

    private sealed record SkillManifest(
        SkillDefinition Definition,
        IReadOnlyDictionary<string, string> Metadata);
}
