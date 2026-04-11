using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Context;

/// <summary>
/// Represents the enriched prompt payload passed into agent execution.
/// </summary>
/// <param name="Prompt">The final prompt text.</param>
/// <param name="Metadata">The merged execution metadata.</param>
/// <param name="ConversationHistory">
/// Prior turn messages assembled from session events, ready to be prepended to the
/// provider request. May be empty for a brand-new session.
/// </param>
public sealed record PromptExecutionContext(
    string Prompt,
    IReadOnlyDictionary<string, string> Metadata,
    IReadOnlyList<ChatMessage>? ConversationHistory = null);
