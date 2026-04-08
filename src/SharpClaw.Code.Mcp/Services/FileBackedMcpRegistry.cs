using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Mcp.Services;

/// <summary>
/// Persists MCP definitions and status snapshots under the local <c>.sharpclaw/mcp</c> directory.
/// </summary>
public sealed class FileBackedMcpRegistry(
    IFileSystem fileSystem,
    IPathService pathService,
    ISystemClock systemClock) : IMcpRegistry
{
    private const string SharpClawDirectoryName = ".sharpclaw";
    private const string McpDirectoryName = "mcp";
    private const string RegistryFileName = "servers.json";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    /// <inheritdoc />
    public async Task<RegisteredMcpServer> RegisterAsync(string workspaceRoot, McpServerDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(definition);

        return await ExecuteLockedAsync(
            workspaceRoot,
            async ct =>
            {
                var document = await LoadAsync(workspaceRoot, ct).ConfigureAwait(false);
                document.Definitions[definition.Id] = definition;
                if (!document.Statuses.TryGetValue(definition.Id, out var status))
                {
                    status = CreateInitialStatus(definition.Id, definition.EnabledByDefault);
                }

                document.Statuses[definition.Id] = status with { UpdatedAtUtc = systemClock.UtcNow };
                await SaveAsync(workspaceRoot, document, ct).ConfigureAwait(false);
                return new RegisteredMcpServer(definition, document.Statuses[definition.Id]);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RegisteredMcpServer>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
        => await ExecuteLockedAsync(
            workspaceRoot,
            async ct =>
            {
                var document = await LoadAsync(workspaceRoot, ct).ConfigureAwait(false);
                return (IReadOnlyList<RegisteredMcpServer>)document.Definitions.Values
                    .OrderBy(definition => definition.Id, StringComparer.OrdinalIgnoreCase)
                    .Select(definition => new RegisteredMcpServer(
                        definition,
                        document.Statuses.TryGetValue(definition.Id, out var status)
                            ? status
                            : CreateInitialStatus(definition.Id, definition.EnabledByDefault)))
                    .ToArray();
            },
            cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<RegisteredMcpServer?> GetAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);

        return await ExecuteLockedAsync(
            workspaceRoot,
            async ct =>
            {
                var document = await LoadAsync(workspaceRoot, ct).ConfigureAwait(false);
                return document.Definitions.TryGetValue(serverId, out var definition)
                    ? new RegisteredMcpServer(
                        definition,
                        document.Statuses.TryGetValue(serverId, out var status)
                            ? status
                            : CreateInitialStatus(serverId, definition.EnabledByDefault))
                    : null;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdateStatusAsync(string workspaceRoot, McpServerStatus status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(status);

        await ExecuteLockedAsync(
            workspaceRoot,
            async ct =>
            {
                var document = await LoadAsync(workspaceRoot, ct).ConfigureAwait(false);
                if (!document.Definitions.ContainsKey(status.ServerId))
                {
                    throw new InvalidOperationException($"MCP server '{status.ServerId}' is not registered.");
                }

                document.Statuses[status.ServerId] = status;
                await SaveAsync(workspaceRoot, document, ct).ConfigureAwait(false);
                return 0;
            },
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpRegistryDocument> LoadAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var registryPath = GetRegistryPath(workspaceRoot);
        var content = await fileSystem.ReadAllTextIfExistsAsync(registryPath, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new McpRegistryDocument(
                new Dictionary<string, McpServerDefinition>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, McpServerStatus>(StringComparer.OrdinalIgnoreCase));
        }

        return JsonSerializer.Deserialize<McpRegistryDocument>(content, JsonOptions)
            ?? new McpRegistryDocument(
                new Dictionary<string, McpServerDefinition>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, McpServerStatus>(StringComparer.OrdinalIgnoreCase));
    }

    private async Task SaveAsync(string workspaceRoot, McpRegistryDocument document, CancellationToken cancellationToken)
    {
        var directoryPath = GetRegistryDirectory(workspaceRoot);
        fileSystem.CreateDirectory(directoryPath);
        await fileSystem.WriteAllTextAsync(
            GetRegistryPath(workspaceRoot),
            JsonSerializer.Serialize(document, JsonOptions),
            cancellationToken).ConfigureAwait(false);
    }

    private string GetRegistryDirectory(string workspaceRoot)
        => pathService.Combine(pathService.GetFullPath(workspaceRoot), SharpClawDirectoryName, McpDirectoryName);

    private string GetRegistryPath(string workspaceRoot)
        => pathService.Combine(GetRegistryDirectory(workspaceRoot), RegistryFileName);

    private string GetRegistryLockPath(string workspaceRoot)
        => pathService.Combine(GetRegistryDirectory(workspaceRoot), "servers.lock");

    private McpServerStatus CreateInitialStatus(string serverId, bool enabledByDefault)
        => new(
            ServerId: serverId,
            State: enabledByDefault ? McpLifecycleState.Stopped : McpLifecycleState.Disabled,
            UpdatedAtUtc: systemClock.UtcNow,
            StatusMessage: enabledByDefault ? "Registered and ready to start." : "Registered but disabled.",
            ToolCount: 0,
            IsHealthy: false);

    private async Task<T> ExecuteLockedAsync<T>(string workspaceRoot, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentNullException.ThrowIfNull(operation);

        await using var fileLock = await fileSystem
            .AcquireExclusiveFileLockAsync(GetRegistryLockPath(workspaceRoot), cancellationToken)
            .ConfigureAwait(false);
        return await operation(cancellationToken).ConfigureAwait(false);
    }

    private sealed record McpRegistryDocument(
        Dictionary<string, McpServerDefinition> Definitions,
        Dictionary<string, McpServerStatus> Statuses);
}
