using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Searches the persisted workspace knowledge index.
/// </summary>
public sealed class WorkspaceSearchTool(IWorkspaceSearchService workspaceSearchService) : SharpClawToolBase
{
    /// <summary>
    /// Gets the stable tool name.
    /// </summary>
    public const string ToolName = "workspace_search";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: ToolName,
        Description: "Search indexed workspace content, symbols, and semantic matches.",
        ApprovalScope: ApprovalScope.ToolExecution,
        IsDestructive: false,
        RequiresApproval: false,
        InputTypeName: nameof(WorkspaceSearchToolArguments),
        InputDescription: "JSON object with query, limit, and optional symbol/semantic flags.",
        Tags: ["search", "workspace", "semantic", "symbols"]);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<WorkspaceSearchToolArguments>(request);
        var result = await workspaceSearchService
            .SearchAsync(
                context.WorkspaceRoot,
                new WorkspaceSearchRequest(arguments.Query, arguments.Limit, arguments.IncludeSymbols, arguments.IncludeSemantic),
                cancellationToken)
            .ConfigureAwait(false);
        var text = result.Hits.Length == 0
            ? "No workspace matches were found."
            : string.Join(
                Environment.NewLine,
                result.Hits.Select(hit => $"{hit.Kind}: {hit.Path}" + (hit.StartLine is null ? string.Empty : $":{hit.StartLine}") + $" ({hit.Score:F2})"));

        return CreateSuccessResult(context, request, text, result);
    }
}
