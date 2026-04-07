using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Plugins.Services;

/// <summary>
/// Manages manifest-based plugins under <see cref="PluginLocalStore" />.
/// Tool descriptors are surfaced only for plugins in the <see cref="PluginLifecycleState.Enabled" /> state; execution goes through <see cref="IPluginLoader" /> (out-of-process by default).
/// Trust on the manifest flows to permission evaluation via tool metadata.
/// </summary>
public sealed class PluginManager(
    IPluginLoader pluginLoader,
    PluginManifestValidator manifestValidator,
    IFileSystem fileSystem,
    IPathService pathService,
    ISystemClock systemClock,
    IRuntimeEventPublisher? runtimeEventPublisher = null,
    ILogger<PluginManager>? logger = null) : IPluginManager
{
    private const string CatalogSessionId = "system";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };
    private readonly ILogger<PluginManager> logger = logger ?? NullLogger<PluginManager>.Instance;

    /// <inheritdoc />
    public async Task<IReadOnlyList<LoadedPlugin>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var pluginRoot = GetPluginsRoot(workspaceRoot);
        if (!fileSystem.DirectoryExists(pluginRoot))
        {
            return [];
        }

        var plugins = new List<LoadedPlugin>();
        foreach (var pluginDirectory in fileSystem.EnumerateDirectories(pluginRoot))
        {
            var manifest = await LoadManifestAsync(pluginDirectory, cancellationToken).ConfigureAwait(false);
            if (manifest is null)
            {
                continue;
            }

            plugins.Add(await LoadPluginStateAsync(pluginDirectory, manifest, cancellationToken).ConfigureAwait(false));
        }

        return plugins
            .OrderBy(plugin => plugin.Descriptor.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public Task<LoadedPlugin> InstallAsync(string workspaceRoot, PluginInstallRequest request, CancellationToken cancellationToken)
        => InstallCoreAsync(workspaceRoot, request, emitInstallCatalogEvent: true, cancellationToken);

    private async Task<LoadedPlugin> InstallCoreAsync(
        string workspaceRoot,
        PluginInstallRequest request,
        bool emitInstallCatalogEvent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        manifestValidator.Validate(request.Manifest);

        var pluginDirectory = EnsurePluginDirectory(workspaceRoot, request.Manifest.Id);
        await WriteManifestAsync(pluginDirectory, request.Manifest, cancellationToken).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(request.PackageContent))
        {
            await fileSystem.WriteAllTextAsync(pathService.Combine(pluginDirectory, PluginLocalStore.PackageContentFileName), request.PackageContent, cancellationToken).ConfigureAwait(false);
        }

        var plugin = CreateLoadedPlugin(request.Manifest, PluginLifecycleState.Discovered, null, PluginFailureKind.None);
        await WritePluginStateAsync(pluginDirectory, plugin, cancellationToken).ConfigureAwait(false);
        if (emitInstallCatalogEvent)
        {
            await EmitPluginInstalledAsync(workspaceRoot, plugin, cancellationToken).ConfigureAwait(false);
        }

        return plugin;
    }

    /// <inheritdoc />
    public async Task<LoadedPlugin> EnableAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
    {
        var manifest = await GetRequiredManifestAsync(workspaceRoot, pluginId, cancellationToken).ConfigureAwait(false);
        manifestValidator.Validate(manifest);

        var pluginDirectory = GetPluginDirectory(workspaceRoot, pluginId);
        var previousPlugin = await LoadPluginStateAsync(pluginDirectory, manifest, cancellationToken).ConfigureAwait(false);
        var loadResult = await pluginLoader.LoadAsync(manifest, cancellationToken).ConfigureAwait(false);
        var nextPlugin = loadResult.Succeeded
            ? CreateLoadedPlugin(manifest, PluginLifecycleState.Enabled, null, PluginFailureKind.None)
            : CreateLoadedPlugin(manifest, PluginLifecycleState.Faulted, loadResult.FailureReason, loadResult.FailureKind);

        await WritePluginStateAsync(pluginDirectory, nextPlugin, cancellationToken).ConfigureAwait(false);
        await EmitPluginStateChangedAsync(workspaceRoot, previousPlugin, nextPlugin, cancellationToken).ConfigureAwait(false);
        return nextPlugin;
    }

    /// <inheritdoc />
    public async Task<LoadedPlugin> DisableAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
    {
        var manifest = await GetRequiredManifestAsync(workspaceRoot, pluginId, cancellationToken).ConfigureAwait(false);
        var pluginDirectory = GetPluginDirectory(workspaceRoot, pluginId);
        var previousPlugin = await LoadPluginStateAsync(pluginDirectory, manifest, cancellationToken).ConfigureAwait(false);
        var nextPlugin = CreateLoadedPlugin(manifest, PluginLifecycleState.Disabled, null, PluginFailureKind.None);

        await WritePluginStateAsync(pluginDirectory, nextPlugin, cancellationToken).ConfigureAwait(false);
        await EmitPluginStateChangedAsync(workspaceRoot, previousPlugin, nextPlugin, cancellationToken).ConfigureAwait(false);
        return nextPlugin;
    }

    /// <inheritdoc />
    public async Task UninstallAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
        var pluginDirectory = GetPluginDirectory(workspaceRoot, pluginId);
        if (!fileSystem.DirectoryExists(pluginDirectory))
        {
            return;
        }

        fileSystem.DeleteDirectoryRecursive(pluginDirectory);
        await EmitPluginUninstalledAsync(workspaceRoot, pluginId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<LoadedPlugin> UpdateAsync(string workspaceRoot, PluginInstallRequest request, CancellationToken cancellationToken)
    {
        var pluginDirectory = GetPluginDirectory(workspaceRoot, request.Manifest.Id);
        var existingManifest = await LoadManifestAsync(pluginDirectory, cancellationToken).ConfigureAwait(false);
        var existingPlugin = existingManifest is null
            ? null
            : await LoadPluginStateAsync(pluginDirectory, existingManifest, cancellationToken).ConfigureAwait(false);

        // Install always writes a fresh "discovered" snapshot; avoid leaving "enabled" while the manifest on disk changed without re-load.
        var installed = await InstallCoreAsync(workspaceRoot, request, emitInstallCatalogEvent: false, cancellationToken).ConfigureAwait(false);
        if (existingPlugin is null)
        {
            await EmitPluginUpdatedAsync(workspaceRoot, request.Manifest.Id, request.Manifest.Version, request.Manifest.Trust, cancellationToken).ConfigureAwait(false);
            return installed;
        }

        var newState = existingPlugin.State switch
        {
            PluginLifecycleState.Enabled or PluginLifecycleState.Faulted => PluginLifecycleState.Discovered,
            _ => existingPlugin.State
        };
        var updatedPlugin = installed with { State = newState };
        await WritePluginStateAsync(pluginDirectory, updatedPlugin, cancellationToken).ConfigureAwait(false);
        await EmitPluginUpdatedAsync(workspaceRoot, request.Manifest.Id, request.Manifest.Version, request.Manifest.Trust, cancellationToken).ConfigureAwait(false);
        if (newState != existingPlugin.State)
        {
            await EmitPluginStateChangedAsync(workspaceRoot, existingPlugin, updatedPlugin, cancellationToken).ConfigureAwait(false);
        }

        return updatedPlugin;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PluginToolDescriptor>> ListToolDescriptorsAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var plugins = await ListAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var descriptors = new List<PluginToolDescriptor>();
        foreach (var plugin in plugins.Where(plugin => plugin.State == PluginLifecycleState.Enabled))
        {
            var manifest = await GetRequiredManifestAsync(workspaceRoot, plugin.Descriptor.Id, cancellationToken).ConfigureAwait(false);
            descriptors.AddRange((manifest.Tools ?? []).Select(tool => tool with
            {
                SourcePluginId = manifest.Id,
                Trust = manifest.Trust
            }));
        }

        var ordered = descriptors
            .OrderBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ThrowIfDuplicateToolNames(ordered);
        return ordered;
    }

    /// <inheritdoc />
    public async Task<ToolResult> ExecuteToolAsync(string workspaceRoot, string toolName, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(request);

        var plugins = await ListAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        foreach (var plugin in plugins.Where(plugin => plugin.State == PluginLifecycleState.Enabled))
        {
            var manifest = await GetRequiredManifestAsync(workspaceRoot, plugin.Descriptor.Id, cancellationToken).ConfigureAwait(false);
            var tool = (manifest.Tools ?? []).FirstOrDefault(candidate => string.Equals(candidate.Name, toolName, StringComparison.OrdinalIgnoreCase));
            if (tool is null)
            {
                continue;
            }

            var executionResult = await pluginLoader.ExecuteToolAsync(manifest, tool, request, workspaceRoot, cancellationToken).ConfigureAwait(false);
            return new ToolResult(
                RequestId: request.Id,
                ToolName: tool.Name,
                Succeeded: executionResult.Succeeded,
                OutputFormat: executionResult.OutputFormat,
                Output: executionResult.Output,
                ErrorMessage: executionResult.Error,
                ExitCode: executionResult.ExitCode,
                DurationMilliseconds: null,
                StructuredOutputJson: executionResult.StructuredOutputJson);
        }

        throw new InvalidOperationException($"No enabled plugin exposes tool '{toolName}'.");
    }

    private async Task<PluginManifest?> LoadManifestAsync(string pluginDirectory, CancellationToken cancellationToken)
    {
        var content = await fileSystem.ReadAllTextIfExistsAsync(pathService.Combine(pluginDirectory, PluginLocalStore.ManifestFileName), cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(content)
            ? null
            : JsonSerializer.Deserialize<PluginManifest>(content, JsonOptions);
    }

    private async Task<PluginManifest> GetRequiredManifestAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
        => await LoadManifestAsync(GetPluginDirectory(workspaceRoot, pluginId), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Plugin '{pluginId}' is not installed.");

    private async Task<LoadedPlugin> LoadPluginStateAsync(string pluginDirectory, PluginManifest manifest, CancellationToken cancellationToken)
    {
        var content = await fileSystem.ReadAllTextIfExistsAsync(pathService.Combine(pluginDirectory, PluginLocalStore.StateFileName), cancellationToken).ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(content)
            ? CreateLoadedPlugin(manifest, PluginLifecycleState.Discovered, null, PluginFailureKind.None)
            : JsonSerializer.Deserialize<LoadedPlugin>(content, JsonOptions)
                ?? CreateLoadedPlugin(manifest, PluginLifecycleState.Discovered, null, PluginFailureKind.None);
    }

    private Task WriteManifestAsync(string pluginDirectory, PluginManifest manifest, CancellationToken cancellationToken)
        => fileSystem.WriteAllTextAsync(pathService.Combine(pluginDirectory, PluginLocalStore.ManifestFileName), JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken);

    private Task WritePluginStateAsync(string pluginDirectory, LoadedPlugin plugin, CancellationToken cancellationToken)
        => fileSystem.WriteAllTextAsync(pathService.Combine(pluginDirectory, PluginLocalStore.StateFileName), JsonSerializer.Serialize(plugin, JsonOptions), cancellationToken);

    private string GetPluginsRoot(string workspaceRoot)
        => pathService.Combine(
            pathService.GetFullPath(workspaceRoot),
            PluginLocalStore.SharpClawRelativeDirectoryName,
            PluginLocalStore.PluginsRelativeDirectoryName);

    private string GetPluginDirectory(string workspaceRoot, string pluginId)
        => pathService.Combine(GetPluginsRoot(workspaceRoot), pluginId);

    private string EnsurePluginDirectory(string workspaceRoot, string pluginId)
    {
        var pluginDirectory = GetPluginDirectory(workspaceRoot, pluginId);
        fileSystem.CreateDirectory(pluginDirectory);
        return pluginDirectory;
    }

    private LoadedPlugin CreateLoadedPlugin(PluginManifest manifest, PluginLifecycleState state, string? failureReason, PluginFailureKind failureKind)
        => new(
            Descriptor: new PluginDescriptor(
                Id: manifest.Id,
                Name: manifest.Name,
                Version: manifest.Version,
                EntryPoint: manifest.EntryPoint,
                Description: manifest.Description,
                Capabilities: manifest.Capabilities,
                Trust: manifest.Trust),
            State: state,
            LoadedAtUtc: systemClock.UtcNow,
            FailureReason: failureReason,
            FailureKind: failureKind);

    private async Task EmitPluginStateChangedAsync(
        string workspaceRoot,
        LoadedPlugin previousPlugin,
        LoadedPlugin currentPlugin,
        CancellationToken cancellationToken)
    {
        var runtimeEvent = new PluginStateChangedEvent(
            EventId: $"event-{Guid.NewGuid():N}",
            SessionId: CatalogSessionId,
            TurnId: null,
            OccurredAtUtc: systemClock.UtcNow,
            PluginId: currentPlugin.Descriptor.Id,
            PreviousState: previousPlugin.State,
            CurrentState: currentPlugin.State,
            Message: currentPlugin.FailureReason ?? currentPlugin.State.ToString());

        logger.LogInformation(
            "Plugin {PluginId} transitioned from {PreviousState} to {CurrentState}. {@RuntimeEvent}",
            runtimeEvent.PluginId,
            runtimeEvent.PreviousState,
            runtimeEvent.CurrentState,
            runtimeEvent);

        if (runtimeEventPublisher is not null)
        {
            await runtimeEventPublisher
                .PublishAsync(
                    runtimeEvent,
                    new RuntimeEventPublishOptions(pathService.GetFullPath(workspaceRoot), CatalogSessionId, PersistToSessionStore: false),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task EmitPluginInstalledAsync(string workspaceRoot, LoadedPlugin plugin, CancellationToken cancellationToken)
    {
        var runtimeEvent = new PluginInstalledEvent(
            EventId: $"event-{Guid.NewGuid():N}",
            SessionId: CatalogSessionId,
            TurnId: null,
            OccurredAtUtc: systemClock.UtcNow,
            PluginId: plugin.Descriptor.Id,
            Version: plugin.Descriptor.Version,
            Trust: plugin.Descriptor.Trust);

        logger.LogInformation(
            "Plugin catalog: installed {PluginId} v{Version} trust {Trust}. {@RuntimeEvent}",
            runtimeEvent.PluginId,
            runtimeEvent.Version,
            runtimeEvent.Trust,
            runtimeEvent);

        if (runtimeEventPublisher is not null)
        {
            await runtimeEventPublisher
                .PublishAsync(
                    runtimeEvent,
                    new RuntimeEventPublishOptions(pathService.GetFullPath(workspaceRoot), CatalogSessionId, PersistToSessionStore: false),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task EmitPluginUninstalledAsync(string workspaceRoot, string pluginId, CancellationToken cancellationToken)
    {
        var runtimeEvent = new PluginUninstalledEvent(
            EventId: $"event-{Guid.NewGuid():N}",
            SessionId: CatalogSessionId,
            TurnId: null,
            OccurredAtUtc: systemClock.UtcNow,
            PluginId: pluginId);

        logger.LogInformation("Plugin catalog: uninstalled {PluginId}. {@RuntimeEvent}", runtimeEvent.PluginId, runtimeEvent);

        if (runtimeEventPublisher is not null)
        {
            await runtimeEventPublisher
                .PublishAsync(
                    runtimeEvent,
                    new RuntimeEventPublishOptions(pathService.GetFullPath(workspaceRoot), CatalogSessionId, PersistToSessionStore: false),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task EmitPluginUpdatedAsync(
        string workspaceRoot,
        string pluginId,
        string version,
        PluginTrustLevel trust,
        CancellationToken cancellationToken)
    {
        var runtimeEvent = new PluginUpdatedEvent(
            EventId: $"event-{Guid.NewGuid():N}",
            SessionId: CatalogSessionId,
            TurnId: null,
            OccurredAtUtc: systemClock.UtcNow,
            PluginId: pluginId,
            Version: version,
            Trust: trust);

        logger.LogInformation(
            "Plugin catalog: updated {PluginId} v{Version} trust {Trust}. {@RuntimeEvent}",
            runtimeEvent.PluginId,
            runtimeEvent.Version,
            runtimeEvent.Trust,
            runtimeEvent);

        if (runtimeEventPublisher is not null)
        {
            await runtimeEventPublisher
                .PublishAsync(
                    runtimeEvent,
                    new RuntimeEventPublishOptions(pathService.GetFullPath(workspaceRoot), CatalogSessionId, PersistToSessionStore: false),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static void ThrowIfDuplicateToolNames(PluginToolDescriptor[] descriptors)
    {
        var duplicate = descriptors
            .GroupBy(tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is null)
        {
            return;
        }

        var owners = string.Join(", ", duplicate.Select(tool => tool.SourcePluginId ?? "?").Distinct(StringComparer.OrdinalIgnoreCase));
        throw new InvalidOperationException(
            $"Multiple enabled plugins expose the tool '{duplicate.Key}' (plugins: {owners}). Rename a tool or disable one plugin.");
    }
}
