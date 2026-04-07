using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Web.Abstractions;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Executes structured web search through the configured web search service.
/// </summary>
public sealed class WebSearchTool(IWebSearchService webSearchService) : SharpClawToolBase
{
    /// <summary>
    /// Gets the stable tool name.
    /// </summary>
    public const string ToolName = "web_search";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: ToolName,
        Description: "Search the public web and return structured results.",
        ApprovalScope: ApprovalScope.ToolExecution,
        IsDestructive: false,
        RequiresApproval: false,
        InputTypeName: nameof(WebSearchToolArguments),
        InputDescription: "JSON object with a query string and optional result limit.",
        Tags: ["web", "search", "network"]);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<WebSearchToolArguments>(request);
        var response = await webSearchService.SearchAsync(arguments.Query, arguments.Limit, cancellationToken).ConfigureAwait(false);
        var payload = new WebSearchToolResult(response.Query, response.Provider, response.Results.ToArray());
        var textOutput = response.Results.Count == 0
            ? "No web search results were returned."
            : string.Join(Environment.NewLine, response.Results.Select(result => $"{result.Title} ({result.Url})"));

        return CreateSuccessResult(context, request, textOutput, payload);
    }
}
