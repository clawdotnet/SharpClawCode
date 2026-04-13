namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents a provider-emitted event such as a stream delta or completion signal.
/// </summary>
/// <param name="Id">The unique provider event identifier.</param>
/// <param name="RequestId">The related provider request identifier.</param>
/// <param name="Kind">The provider event kind.</param>
/// <param name="CreatedAtUtc">The UTC timestamp when the event was emitted.</param>
/// <param name="Content">The textual or structured event payload, if any.</param>
/// <param name="IsTerminal">Indicates whether the event terminates the provider interaction.</param>
/// <param name="Usage">The usage snapshot associated with the event, if any.</param>
/// <param name="BlockType">The content block type for structured provider responses, if any.</param>
/// <param name="ToolUseId">The tool-use block identifier when the provider requests a tool call, if any.</param>
/// <param name="ToolName">The name of the tool the provider is requesting to call, if any.</param>
/// <param name="ToolInputJson">The JSON-encoded input arguments for the requested tool call, if any.</param>
public sealed record ProviderEvent(
    string Id,
    string RequestId,
    string Kind,
    DateTimeOffset CreatedAtUtc,
    string? Content,
    bool IsTerminal,
    UsageSnapshot? Usage,
    string? BlockType = null,
    string? ToolUseId = null,
    string? ToolName = null,
    string? ToolInputJson = null);
