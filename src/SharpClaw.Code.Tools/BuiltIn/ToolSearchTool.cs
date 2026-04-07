using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Searches discoverable tool metadata from the active registry.
/// </summary>
public sealed class ToolSearchTool(Func<IToolRegistry> registryAccessor) : SharpClawToolBase
{
    /// <summary>
    /// Gets the stable tool name.
    /// </summary>
    public const string ToolName = "tool_search";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: ToolName,
        Description: "Search the registered tool catalog.",
        ApprovalScope: ApprovalScope.ToolExecution,
        IsDestructive: false,
        RequiresApproval: false,
        InputTypeName: nameof(ToolSearchToolArguments),
        InputDescription: "JSON object with an optional query and result limit.",
        Tags: ["search", "tools", "registry"]);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<ToolSearchToolArguments>(request);
        var matches = await registryAccessor()
            .SearchAsync(arguments.Query, arguments.Limit, context.WorkspaceRoot, cancellationToken)
            .ConfigureAwait(false);
        var matchArray = matches.ToArray();
        var payload = new ToolSearchToolResult(matchArray);
        var textOutput = matchArray.Length == 0
            ? "No tools matched the query."
            : string.Join(Environment.NewLine, matchArray.Select(match => $"{match.Name}: {match.Description}"));

        return CreateSuccessResult(context, request, textOutput, payload);
    }
}
