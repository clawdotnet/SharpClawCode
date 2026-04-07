using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Plugins.Abstractions;

/// <summary>
/// Loads plugins and executes plugin-provided tools through an isolated strategy.
/// </summary>
public interface IPluginLoader
{
    /// <summary>
    /// Prepares a plugin for enablement.
    /// </summary>
    /// <param name="manifest">The manifest to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The load outcome.</returns>
    Task<PluginLoadResult> LoadAsync(PluginManifest manifest, CancellationToken cancellationToken);

    /// <summary>
    /// Executes a plugin-provided tool.
    /// </summary>
    /// <param name="manifest">The manifest owning the tool.</param>
    /// <param name="tool">The tool descriptor to execute.</param>
    /// <param name="request">The tool execution request.</param>
    /// <param name="workspaceRoot">The active workspace root.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The execution result.</returns>
    Task<PluginExecutionResult> ExecuteToolAsync(
        PluginManifest manifest,
        PluginToolDescriptor tool,
        ToolExecutionRequest request,
        string workspaceRoot,
        CancellationToken cancellationToken);
}
