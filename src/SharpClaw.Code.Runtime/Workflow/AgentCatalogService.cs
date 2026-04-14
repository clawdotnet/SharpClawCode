using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.Workflow;

/// <summary>
/// Builds a workspace-visible agent catalog by overlaying configured agents on top of built-ins.
/// </summary>
public sealed class AgentCatalogService(
    IEnumerable<ISharpClawAgent> builtInAgents,
    ISharpClawConfigService configService) : IAgentCatalogService
{
    private readonly ISharpClawAgent[] builtIns = builtInAgents.ToArray();

    /// <inheritdoc />
    public async Task<IReadOnlyList<AgentCatalogEntry>> ListAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        var config = await configService.GetConfigAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var configured = config.Document.Agents ?? [];
        var defaultAgentId = config.Document.DefaultAgentId;

        var entries = builtIns
            .Select(agent => new AgentCatalogEntry(
                agent.AgentId,
                agent.AgentId,
                agent.AgentKind,
                agent.AgentId,
                null,
                null,
                null,
                null,
                IsBuiltIn: true,
                IsDefault: string.Equals(agent.AgentId, defaultAgentId, StringComparison.OrdinalIgnoreCase)
                    || (string.IsNullOrWhiteSpace(defaultAgentId) && string.Equals(agent.AgentId, "primary-coding-agent", StringComparison.OrdinalIgnoreCase))))
            .ToDictionary(static entry => entry.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var definition in configured)
        {
            var baseAgentId = string.IsNullOrWhiteSpace(definition.BaseAgentId)
                ? "primary-coding-agent"
                : definition.BaseAgentId!;
            var baseEntry = entries.TryGetValue(baseAgentId, out var found)
                ? found
                : entries["primary-coding-agent"];

            entries[definition.Id] = new AgentCatalogEntry(
                definition.Id,
                string.IsNullOrWhiteSpace(definition.Name) ? definition.Id : definition.Name,
                string.IsNullOrWhiteSpace(definition.Description) ? baseEntry.Description : definition.Description!,
                baseAgentId,
                definition.Model,
                definition.PrimaryMode,
                definition.AllowedTools,
                definition.InstructionAppendix,
                IsBuiltIn: false,
                IsDefault: definition.IsDefault || string.Equals(definition.Id, defaultAgentId, StringComparison.OrdinalIgnoreCase));
        }

        return entries.Values.OrderBy(static entry => entry.Id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <inheritdoc />
    public async Task<AgentCatalogEntry> ResolveAsync(
        string workspaceRoot,
        string? requestedAgentId,
        string? persistedAgentId,
        CancellationToken cancellationToken)
    {
        var entries = await ListAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var requested = string.IsNullOrWhiteSpace(requestedAgentId) ? persistedAgentId : requestedAgentId;
        if (!string.IsNullOrWhiteSpace(requested))
        {
            var match = entries.FirstOrDefault(entry => string.Equals(entry.Id, requested, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return entries.FirstOrDefault(static entry => entry.IsDefault)
            ?? entries.First(entry => string.Equals(entry.Id, "primary-coding-agent", StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public async Task<PrimaryMode?> ResolvePrimaryModeDefaultAsync(
        string workspaceRoot,
        string? requestedAgentId,
        string? persistedAgentId,
        CancellationToken cancellationToken)
        => (await ResolveAsync(workspaceRoot, requestedAgentId, persistedAgentId, cancellationToken).ConfigureAwait(false)).PrimaryMode;
}
