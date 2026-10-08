using System.Text.Json;
using System.Text.Json.Serialization;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.Configuration;

/// <summary>
/// Loads user and workspace SharpClaw configuration documents and merges them by precedence.
/// </summary>
public sealed class SharpClawConfigService(
    IFileSystem fileSystem,
    IPathService pathService) : ISharpClawConfigService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters =
        {
            new JsonStringEnumConverter()
        }
    };

    /// <inheritdoc />
    public async Task<SharpClawConfigSnapshot> GetConfigAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var normalizedWorkspace = pathService.GetFullPath(workspaceRoot);
        var userConfigPath = GetUserConfigPath();
        var workspaceConfigPath = pathService.Combine(normalizedWorkspace, ".sharpclaw", "config.jsonc");
        var legacyWorkspaceConfigPath = pathService.Combine(normalizedWorkspace, "sharpclaw.jsonc");

        var userDocument = await LoadDocumentAsync(userConfigPath, cancellationToken).ConfigureAwait(false);
        var workspaceDocument = await LoadDocumentAsync(workspaceConfigPath, cancellationToken).ConfigureAwait(false);
        if (workspaceDocument is null)
        {
            workspaceDocument = await LoadDocumentAsync(legacyWorkspaceConfigPath, cancellationToken).ConfigureAwait(false);
            if (workspaceDocument is not null)
            {
                workspaceConfigPath = legacyWorkspaceConfigPath;
            }
        }

        var merged = Merge(userDocument, workspaceDocument);

        return new SharpClawConfigSnapshot(
            normalizedWorkspace,
            userDocument is null ? null : userConfigPath,
            workspaceDocument is null ? null : workspaceConfigPath,
            merged);
    }

    private async Task<SharpClawConfigDocument?> LoadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        var content = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(content, ProtocolJsonContext.Default.SharpClawConfigDocument)
                ?? JsonSerializer.Deserialize<SharpClawConfigDocument>(content, JsonOptions);
        }
        catch (JsonException)
        {
            return JsonSerializer.Deserialize<SharpClawConfigDocument>(content, JsonOptions);
        }
    }

    private static SharpClawConfigDocument Merge(SharpClawConfigDocument? user, SharpClawConfigDocument? workspace)
    {
        if (user is null && workspace is null)
        {
            return CreateDefaultDocument();
        }

        var defaultAgentId = workspace?.DefaultAgentId ?? user?.DefaultAgentId;
        var shareMode = workspace?.ShareMode ?? user?.ShareMode ?? Protocol.Models.ShareMode.Manual;
        var server = workspace?.Server ?? user?.Server ?? new SharpClawServerOptions("127.0.0.1", 7345, null);

        return new SharpClawConfigDocument(
            shareMode,
            server,
            defaultAgentId,
            MergeByKey(user?.Agents, workspace?.Agents, static item => item.Id),
            MergeByKey(user?.LspServers, workspace?.LspServers, static item => item.Id),
            MergeByKey(user?.Hooks, workspace?.Hooks, static item => item.Name),
            MergeByKey(user?.ConnectLinks, workspace?.ConnectLinks, static item => item.Target),
            MergeExternalAgents(user?.ExternalAgents, workspace?.ExternalAgents),
            workspace?.SkillPacks ?? user?.SkillPacks ?? new SkillPacksConfig(),
            workspace?.WorkItems ?? user?.WorkItems ?? new WorkItemsConfig(),
            MergeVerification(user?.Verification, workspace?.Verification));
    }

    private static List<T>? MergeByKey<T>(
        IEnumerable<T>? user,
        IEnumerable<T>? workspace,
        Func<T, string> keySelector)
    {
        var merged = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in user ?? [])
        {
            merged[keySelector(item)] = item;
        }

        foreach (var item in workspace ?? [])
        {
            merged[keySelector(item)] = item;
        }

        return merged.Count == 0 ? null : merged.Values.ToList();
    }

    private static SharpClawConfigDocument CreateDefaultDocument()
        => new(
            Protocol.Models.ShareMode.Manual,
            new SharpClawServerOptions("127.0.0.1", 7345, null),
            null,
            null,
            null,
            null,
            null,
            new ExternalAgentsConfig(),
            new SkillPacksConfig(),
            new WorkItemsConfig(),
            new VerificationOptions(false, VerificationScope.Affected, true, 2, false));

    private static VerificationOptions MergeVerification(VerificationOptions? user, VerificationOptions? workspace)
        => new(workspace?.Enabled ?? user?.Enabled ?? false, workspace?.Scope ?? user?.Scope ?? VerificationScope.Affected,
            workspace?.RunAfterMutatingTurn ?? user?.RunAfterMutatingTurn ?? true, workspace?.MaxRepairIterations ?? user?.MaxRepairIterations ?? 2,
            workspace?.AllowRestore ?? user?.AllowRestore ?? false);

    private static ExternalAgentsConfig MergeExternalAgents(ExternalAgentsConfig? user, ExternalAgentsConfig? workspace)
    {
        var adapters = new Dictionary<string, ExternalAgentAdapterConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in user?.Adapters ?? [])
        {
            adapters[pair.Key] = pair.Value;
        }

        foreach (var pair in workspace?.Adapters ?? [])
        {
            adapters[pair.Key] = pair.Value;
        }

        return new ExternalAgentsConfig(
            workspace?.Enabled ?? user?.Enabled ?? false,
            adapters.Count == 0 ? null : adapters,
            workspace?.RequireApprovalForMutatingRuns ?? user?.RequireApprovalForMutatingRuns ?? true);
    }

    private static string GetUserConfigPath()
    {
        if (OperatingSystem.IsWindows())
        {
            var roaming = Environment.GetEnvironmentVariable("APPDATA");
            if (string.IsNullOrWhiteSpace(roaming))
            {
                roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            return Path.Combine(roaming, "SharpClaw", "config.jsonc");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home))
        {
            home = Environment.GetEnvironmentVariable("HOME") ?? ".";
        }

        return Path.Combine(home, ".config", "sharpclaw", "config.jsonc");
    }
}
