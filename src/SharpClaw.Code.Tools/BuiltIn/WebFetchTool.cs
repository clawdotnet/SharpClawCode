using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Web.Abstractions;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Fetches a structured web document through the configured web fetch service.
/// </summary>
public sealed class WebFetchTool(IWebFetchService webFetchService) : SharpClawToolBase
{
    /// <summary>
    /// Gets the stable tool name.
    /// </summary>
    public const string ToolName = "web_fetch";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: ToolName,
        Description: "Fetch a web page and return structured text content.",
        ApprovalScope: ApprovalScope.ToolExecution,
        IsDestructive: false,
        RequiresApproval: false,
        InputTypeName: nameof(WebFetchToolArguments),
        InputDescription: "JSON object with the target URL.",
        Tags: ["web", "fetch", "network"]);

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<WebFetchToolArguments>(request);
        var document = await webFetchService.FetchAsync(arguments.Url, cancellationToken).ConfigureAwait(false);
        var payload = new WebFetchToolResult(document.Url, document.StatusCode, document.ContentType, document.Title, document.Content);
        var textOutput = string.IsNullOrWhiteSpace(document.Title)
            ? document.Content
            : $"{document.Title}{Environment.NewLine}{Environment.NewLine}{document.Content}";

        return CreateSuccessResult(context, request, textOutput, payload, exitCode: document.StatusCode is >= 200 and < 300 ? 0 : document.StatusCode);
    }
}
