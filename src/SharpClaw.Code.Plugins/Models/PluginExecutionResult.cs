using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Plugins.Models;

/// <summary>
/// Captures the outcome of a single out-of-process plugin tool execution.
/// </summary>
/// <param name="Succeeded">Indicates whether the execution succeeded.</param>
/// <param name="Output">The primary textual output.</param>
/// <param name="Error">The error payload, if execution failed.</param>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StructuredOutputJson">Optional structured JSON output.</param>
/// <param name="OutputFormat">The resulting output format.</param>
public sealed record PluginExecutionResult(
    bool Succeeded,
    string? Output,
    string? Error,
    int ExitCode,
    string? StructuredOutputJson,
    OutputFormat OutputFormat = OutputFormat.Text);
