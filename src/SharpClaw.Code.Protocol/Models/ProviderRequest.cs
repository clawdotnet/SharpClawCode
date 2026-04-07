using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// Represents a provider invocation request issued by the runtime.
/// </summary>
/// <param name="Id">The unique provider request identifier.</param>
/// <param name="SessionId">The parent session identifier.</param>
/// <param name="TurnId">The parent turn identifier.</param>
/// <param name="ProviderName">The provider name or id.</param>
/// <param name="Model">The requested model name.</param>
/// <param name="Prompt">The user prompt content.</param>
/// <param name="SystemPrompt">The system prompt, if any.</param>
/// <param name="OutputFormat">The preferred output format.</param>
/// <param name="Temperature">The requested sampling temperature, if any.</param>
/// <param name="Metadata">Additional machine-readable provider metadata.</param>
public sealed record ProviderRequest(
    string Id,
    string SessionId,
    string TurnId,
    string ProviderName,
    string Model,
    string Prompt,
    string? SystemPrompt,
    OutputFormat OutputFormat,
    decimal? Temperature,
    Dictionary<string, string>? Metadata);
