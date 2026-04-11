using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.BuiltIn;
using SharpClaw.Code.Tools.Models;

namespace McpToolAgent;

/// <summary>
/// A simple echo tool that returns its input unchanged.
/// Demonstrates the minimal pattern for implementing a custom SharpClaw tool.
/// </summary>
public sealed class EchoTool : SharpClawToolBase
{
    /// <summary>
    /// The stable tool name used by the agent to invoke this tool.
    /// </summary>
    public const string ToolName = "echo";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: ToolName,
        Description: "Returns the supplied message unchanged. Useful for testing tool dispatch.",
        ApprovalScope: ApprovalScope.None,
        IsDestructive: false,
        RequiresApproval: false,
        InputTypeName: nameof(EchoToolArguments),
        InputDescription: "JSON object with a single 'message' string field.",
        Tags: ["echo", "test", "example"]);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments<EchoToolArguments>(request);
        var payload = new EchoToolResult(arguments.Message);
        return Task.FromResult(CreateSuccessResult(context, request, arguments.Message, payload));
    }
}

/// <summary>
/// Arguments accepted by <see cref="EchoTool"/>.
/// </summary>
/// <param name="Message">The message to echo back.</param>
public sealed record EchoToolArguments(string Message);

/// <summary>
/// Structured result produced by <see cref="EchoTool"/>.
/// </summary>
/// <param name="Message">The echoed message.</param>
public sealed record EchoToolResult(string Message);
