using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Provides shared helpers for built-in tools.
/// </summary>
public abstract class SharpClawToolBase : ISharpClawTool
{
    /// <inheritdoc />
    public abstract ToolDefinition Definition { get; }

    /// <inheritdoc />
    public virtual PluginToolSource? PluginSource => null;

    /// <inheritdoc />
    public abstract Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deserializes tool arguments from the request payload.
    /// </summary>
    /// <typeparam name="TArguments">The expected argument contract type.</typeparam>
    /// <param name="request">The current tool execution request.</param>
    /// <returns>The deserialized tool arguments.</returns>
    protected static TArguments DeserializeArguments<TArguments>(ToolExecutionRequest request)
        => ToolJson.Deserialize<TArguments>(request.ArgumentsJson);

    /// <summary>
    /// Creates a successful tool result with both text and structured payloads.
    /// </summary>
    /// <typeparam name="TPayload">The structured payload type.</typeparam>
    /// <param name="context">The current tool execution context.</param>
    /// <param name="request">The current tool execution request.</param>
    /// <param name="textOutput">The human-readable output text.</param>
    /// <param name="payload">The structured payload.</param>
    /// <param name="exitCode">The optional exit code.</param>
    /// <param name="durationMilliseconds">The optional duration in milliseconds.</param>
    /// <returns>The successful tool result.</returns>
    protected static ToolResult CreateSuccessResult<TPayload>(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        string textOutput,
        TPayload payload,
        int? exitCode = 0,
        long? durationMilliseconds = null)
        => new(
            RequestId: request.Id,
            ToolName: request.ToolName,
            Succeeded: true,
            OutputFormat: context.OutputFormat,
            Output: context.OutputFormat == OutputFormat.Json ? ToolJson.Serialize(payload) : textOutput,
            ErrorMessage: null,
            ExitCode: exitCode,
            DurationMilliseconds: durationMilliseconds,
            StructuredOutputJson: ToolJson.Serialize(payload));

    /// <summary>
    /// Creates a failed tool result.
    /// </summary>
    /// <param name="context">The current tool execution context.</param>
    /// <param name="request">The current tool execution request.</param>
    /// <param name="errorMessage">The failure message.</param>
    /// <param name="exitCode">The optional exit code.</param>
    /// <param name="durationMilliseconds">The optional duration in milliseconds.</param>
    /// <returns>The failed tool result.</returns>
    protected static ToolResult CreateFailureResult(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        string errorMessage,
        int? exitCode = 1,
        long? durationMilliseconds = null)
        => new(
            RequestId: request.Id,
            ToolName: request.ToolName,
            Succeeded: false,
            OutputFormat: context.OutputFormat,
            Output: null,
            ErrorMessage: errorMessage,
            ExitCode: exitCode,
            DurationMilliseconds: durationMilliseconds,
            StructuredOutputJson: null);
}
