using System.Text.Json.Serialization;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Discriminates the kind of content carried by a <see cref="ContentBlock"/>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ContentBlockKind>))]
public enum ContentBlockKind
{
    /// <summary>
    /// Plain text content.
    /// </summary>
    [JsonStringEnumMemberName("text")]
    Text,

    /// <summary>
    /// The model is requesting a tool call.
    /// </summary>
    [JsonStringEnumMemberName("tool_use")]
    ToolUse,

    /// <summary>
    /// The result of a prior tool invocation.
    /// </summary>
    [JsonStringEnumMemberName("tool_result")]
    ToolResult,
}

/// <summary>
/// A single block of content within a <see cref="ChatMessage"/>.
/// </summary>
/// <param name="Kind">The block discriminator.</param>
/// <param name="Text">Text content, used for <see cref="ContentBlockKind.Text"/> and <see cref="ContentBlockKind.ToolResult"/> kinds.</param>
/// <param name="ToolUseId">Tool call identifier, used for <see cref="ContentBlockKind.ToolUse"/> and <see cref="ContentBlockKind.ToolResult"/> kinds.</param>
/// <param name="ToolName">Tool name, used for <see cref="ContentBlockKind.ToolUse"/> kind.</param>
/// <param name="ToolInputJson">Tool input serialized as a JSON string, used for <see cref="ContentBlockKind.ToolUse"/> kind.</param>
/// <param name="IsError">Whether the tool result represents an error, used for <see cref="ContentBlockKind.ToolResult"/> kind.</param>
public sealed record ContentBlock(
    ContentBlockKind Kind,
    string? Text,
    string? ToolUseId,
    string? ToolName,
    string? ToolInputJson,
    bool? IsError);
