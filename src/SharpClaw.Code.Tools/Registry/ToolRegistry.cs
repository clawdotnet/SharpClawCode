using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Tools.BuiltIn;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.Registry;

/// <summary>
/// Provides lookup and search over registered SharpClaw tools.
/// </summary>
public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, ISharpClawTool> tools;
    private readonly Func<IPluginManager?>? pluginManagerAccessor;

    /// <summary>
    /// Initializes a new registry from the supplied tool set.
    /// </summary>
    /// <param name="tools">The tool implementations to register.</param>
    /// <param name="pluginManagerAccessor">The optional plugin manager accessor used to surface enabled plugin tools.</param>
    public ToolRegistry(IEnumerable<ISharpClawTool> tools, Func<IPluginManager?>? pluginManagerAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(tools);

        this.tools = new Dictionary<string, ISharpClawTool>(StringComparer.OrdinalIgnoreCase);
        this.pluginManagerAccessor = pluginManagerAccessor;
        foreach (var tool in tools)
        {
            if (!this.tools.TryAdd(tool.Definition.Name, tool))
            {
                throw new InvalidOperationException($"A tool named '{tool.Definition.Name}' is already registered.");
            }
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToolDefinition>> ListAsync(
        string? workspaceRootForPluginTools = null,
        CancellationToken cancellationToken = default)
    {
        var all = await GetAllToolsAsync(workspaceRootForPluginTools, cancellationToken).ConfigureAwait(false);
        return all.Values
            .Select(tool => tool.Definition)
            .OrderBy(definition => definition.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToolDefinition>> SearchAsync(
        string? query,
        int? limit = null,
        string? workspaceRootForPluginTools = null,
        CancellationToken cancellationToken = default)
    {
        var definitions = await ListAsync(workspaceRootForPluginTools, cancellationToken).ConfigureAwait(false);
        var matches = string.IsNullOrWhiteSpace(query)
            ? definitions.AsEnumerable()
            : definitions.Where(definition =>
                definition.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || definition.Description.Contains(query, StringComparison.OrdinalIgnoreCase)
                || definition.InputTypeName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || definition.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)));

        return matches
            .Take(limit.GetValueOrDefault(20))
            .ToArray();
    }

    /// <inheritdoc />
    public async Task<ISharpClawTool> GetRequiredAsync(
        string toolName,
        string? workspaceRootForPluginTools = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        var allTools = await GetAllToolsAsync(workspaceRootForPluginTools, cancellationToken).ConfigureAwait(false);
        return allTools.TryGetValue(toolName, out var tool)
            ? tool
            : throw new InvalidOperationException($"Tool '{toolName}' is not registered.");
    }

    private async Task<Dictionary<string, ISharpClawTool>> GetAllToolsAsync(
        string? workspaceRootForPluginTools,
        CancellationToken cancellationToken)
    {
        var allTools = new Dictionary<string, ISharpClawTool>(tools, StringComparer.OrdinalIgnoreCase);
        var pluginManager = pluginManagerAccessor?.Invoke();
        if (pluginManager is null)
        {
            return allTools;
        }

        var workspaceRoot = string.IsNullOrWhiteSpace(workspaceRootForPluginTools)
            ? Environment.CurrentDirectory
            : workspaceRootForPluginTools!;

        var pluginTools = await pluginManager
            .ListToolDescriptorsAsync(workspaceRoot, cancellationToken)
            .ConfigureAwait(false);

        foreach (var pluginTool in pluginTools)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (allTools.ContainsKey(pluginTool.Name))
            {
                continue;
            }

            allTools[pluginTool.Name] = new PluginToolProxyTool(pluginManager, pluginTool);
        }

        return allTools;
    }
}
