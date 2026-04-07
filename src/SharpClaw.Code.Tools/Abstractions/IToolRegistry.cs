using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.Abstractions;

/// <summary>
/// Resolves and searches registered SharpClaw tools.
/// </summary>
public interface IToolRegistry
{
    /// <summary>
    /// Gets the registered tool metadata.
    /// </summary>
    /// <param name="workspaceRootForPluginTools">Workspace root used to resolve plugin-surfaced tools; defaults to the process current directory.</param>
    /// <param name="cancellationToken">Token cancelled when the caller is aborted.</param>
    /// <returns>All registered tool definitions.</returns>
    Task<IReadOnlyList<ToolDefinition>> ListAsync(
        string? workspaceRootForPluginTools = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches registered tool metadata.
    /// </summary>
    /// <param name="query">The optional free-text query.</param>
    /// <param name="limit">The maximum number of results to return.</param>
    /// <param name="workspaceRootForPluginTools">Workspace root used to resolve plugin-surfaced tools.</param>
    /// <param name="cancellationToken">Token cancelled when the caller is aborted.</param>
    /// <returns>The matching tool definitions.</returns>
    Task<IReadOnlyList<ToolDefinition>> SearchAsync(
        string? query,
        int? limit = null,
        string? workspaceRootForPluginTools = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a registered tool by name.
    /// </summary>
    /// <param name="toolName">The tool name to resolve.</param>
    /// <param name="workspaceRootForPluginTools">Workspace root used to resolve plugin-surfaced tools.</param>
    /// <param name="cancellationToken">Token cancelled when the caller is aborted.</param>
    /// <returns>The matching tool implementation.</returns>
    Task<ISharpClawTool> GetRequiredAsync(
        string toolName,
        string? workspaceRootForPluginTools = null,
        CancellationToken cancellationToken = default);
}
