using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Searches indexed workspace symbols only.
/// </summary>
public sealed class SymbolSearchTool(IWorkspaceKnowledgeStore knowledgeStore) : SharpClawToolBase
{
    /// <summary>
    /// Gets the stable tool name.
    /// </summary>
    public const string ToolName = "symbol_search";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: ToolName,
        Description: "Search indexed workspace symbols by name or container.",
        ApprovalScope: ApprovalScope.ToolExecution,
        IsDestructive: false,
        RequiresApproval: false,
        InputTypeName: nameof(SymbolSearchToolArguments),
        InputDescription: "JSON object with query and optional limit.",
        Tags: ["search", "symbols", "workspace"]);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<SymbolSearchToolArguments>(request);
        var hits = await knowledgeStore
            .SearchSymbolsAsync(context.WorkspaceRoot, arguments.Query, Math.Clamp(arguments.Limit.GetValueOrDefault(8), 1, 50), cancellationToken)
            .ConfigureAwait(false);
        var payload = new WorkspaceSearchResult(arguments.Query, DateTimeOffset.UtcNow, null, hits.ToArray());
        var text = hits.Count == 0
            ? "No matching symbols were found."
            : string.Join(Environment.NewLine, hits.Select(hit => $"{hit.SymbolKind}: {hit.Excerpt} ({hit.Path}:{hit.StartLine})"));

        return CreateSuccessResult(context, request, text, payload);
    }
}
