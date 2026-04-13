namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// A single message in a conversation history, carrying one or more content blocks.
/// </summary>
/// <param name="Role">The message author role: "user", "assistant", or "system".</param>
/// <param name="Content">The ordered list of content blocks that make up this message.</param>
public sealed record ChatMessage(
    string Role,
    IReadOnlyList<ContentBlock> Content);
