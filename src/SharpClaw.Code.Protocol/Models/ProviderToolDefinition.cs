namespace SharpClaw.Code.Protocol.Models;

/// <summary>
/// A lightweight tool definition for provider requests, decoupled from the full tool registry.
/// </summary>
/// <param name="Name">The tool name.</param>
/// <param name="Description">A description of what the tool does.</param>
/// <param name="InputSchemaJson">The JSON Schema describing the tool's input parameters.</param>
public sealed record ProviderToolDefinition(
    string Name,
    string Description,
    string? InputSchemaJson);
