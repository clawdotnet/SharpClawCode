using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Plugins.Abstractions;

/// <summary>
/// Executes plugin entry points as external processes.
/// </summary>
public interface IPluginProcessRunner
{
    /// <summary>
    /// Loads a plugin through an isolated process-backed check.
    /// </summary>
    /// <param name="manifest">The manifest to load.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The load result.</returns>
    Task<PluginLoadResult> LoadAsync(PluginManifest manifest, CancellationToken cancellationToken);

    /// <summary>
    /// Executes a plugin-provided tool.
    /// </summary>
    /// <param name="manifest">The plugin manifest.</param>
    /// <param name="tool">The tool descriptor.</param>
    /// <param name="request">The tool execution request.</param>
    /// <param name="workspaceRoot">The active workspace root.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The execution result.</returns>
    Task<PluginExecutionResult> ExecuteAsync(
        PluginManifest manifest,
        PluginToolDescriptor tool,
        ToolExecutionRequest request,
        string workspaceRoot,
        CancellationToken cancellationToken);
}
