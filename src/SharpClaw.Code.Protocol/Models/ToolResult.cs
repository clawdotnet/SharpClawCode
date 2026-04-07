using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents the result of a tool execution attempt.
/// </summary>
/// <param name="RequestId">The related tool request identifier.</param>
/// <param name="ToolName">The executed tool name.</param>
/// <param name="Succeeded">Indicates whether the execution completed successfully.</param>
/// <param name="OutputFormat">The format of the output payload.</param>
/// <param name="Output">The primary output payload, if any.</param>
/// <param name="ErrorMessage">The error message, if execution failed.</param>
/// <param name="ExitCode">The process or tool exit code, if available.</param>
/// <param name="DurationMilliseconds">The execution duration in milliseconds, if available.</param>
/// <param name="StructuredOutputJson">Structured JSON output, if available.</param>
public sealed record ToolResult(
    string RequestId,
    string ToolName,
    bool Succeeded,
    OutputFormat OutputFormat,
    string? Output,
    string? ErrorMessage,
    int? ExitCode,
    long? DurationMilliseconds,
    string? StructuredOutputJson);
