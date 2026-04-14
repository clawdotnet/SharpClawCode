using System.Text.Json;
using Microsoft.Extensions.AI;
using SharpClaw.Code.Protocol.Models;
using MeaiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using ProtocolChatMessage = SharpClaw.Code.Protocol.Models.ChatMessage;

namespace SharpClaw.Code.Providers.Internal;

/// <summary>
/// Maps SharpClaw conversation types to Microsoft.Extensions.AI request parameters.
/// </summary>
internal static class OpenAiMessageBuilder
{
    /// <summary>
    /// Converts an array of SharpClaw <see cref="ProtocolChatMessage"/> records into MEAI <see cref="MeaiChatMessage"/> instances.
    /// </summary>
    /// <remarks>
    /// Role mapping:
    /// <list type="bullet">
    ///   <item><term>system</term><description>→ <see cref="ChatRole.System"/> with <see cref="TextContent"/></description></item>
    ///   <item><term>assistant</term><description>→ <see cref="ChatRole.Assistant"/> with <see cref="TextContent"/> or <see cref="FunctionCallContent"/></description></item>
    ///   <item><term>user</term><description>→ <see cref="ChatRole.User"/> with <see cref="TextContent"/> or <see cref="FunctionResultContent"/></description></item>
    /// </list>
    /// </remarks>
    public static List<MeaiChatMessage> BuildMessages(IReadOnlyList<ProtocolChatMessage> messages, string? systemPrompt = null)
    {
        var result = new List<MeaiChatMessage>(messages.Count + (string.IsNullOrWhiteSpace(systemPrompt) ? 0 : 1));
        if (!string.IsNullOrWhiteSpace(systemPrompt)
            && !HasLeadingSystemMessage(messages))
        {
            result.Add(new MeaiChatMessage(ChatRole.System, systemPrompt));
        }

        foreach (var message in messages)
        {
            var meaiMessage = BuildMeaiMessage(message);
            if (meaiMessage is not null)
            {
                result.Add(meaiMessage);
            }
        }

        return result;
    }

    private static bool HasLeadingSystemMessage(IReadOnlyList<ProtocolChatMessage> messages)
        => messages.Count > 0 && string.Equals(messages[0].Role, "system", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Converts an array of <see cref="ProviderToolDefinition"/> records into MEAI <see cref="AITool"/> instances.
    /// Uses <see cref="AIFunctionFactory.CreateDeclaration"/> to build tool declarations from the JSON schema.
    /// </summary>
    public static List<AITool> BuildTools(IReadOnlyList<ProviderToolDefinition> tools)
    {
        var result = new List<AITool>(tools.Count);
        foreach (var tool in tools)
        {
            result.Add(BuildAiTool(tool));
        }

        return result;
    }

    private static MeaiChatMessage? BuildMeaiMessage(ProtocolChatMessage message)
    {
        var role = message.Role.ToLowerInvariant() switch
        {
            "system" => ChatRole.System,
            "assistant" => ChatRole.Assistant,
            _ => ChatRole.User,
        };

        var contentItems = new List<AIContent>(message.Content.Count);
        foreach (var block in message.Content)
        {
            var item = BuildAiContent(block);
            if (item is not null)
            {
                contentItems.Add(item);
            }
        }

        if (contentItems.Count == 0)
        {
            return null;
        }

        return new MeaiChatMessage(role, contentItems);
    }

    private static AIContent? BuildAiContent(ContentBlock block)
    {
        return block.Kind switch
        {
            ContentBlockKind.Text => new TextContent(block.Text ?? string.Empty),

            ContentBlockKind.ToolUse => new FunctionCallContent(
                callId: block.ToolUseId ?? string.Empty,
                name: block.ToolName ?? string.Empty,
                arguments: ParseArguments(block.ToolInputJson)),

            ContentBlockKind.ToolResult => new FunctionResultContent(
                callId: block.ToolUseId ?? string.Empty,
                result: (object?)(block.Text ?? string.Empty)),

            _ => null,
        };
    }

    private static AITool BuildAiTool(ProviderToolDefinition definition)
    {
        var schemaJson = definition.InputSchemaJson ?? """{"type":"object","properties":{}}""";
        JsonElement schemaElement;
        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            schemaElement = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var fallback = JsonDocument.Parse("""{"type":"object","properties":{}}""");
            schemaElement = fallback.RootElement.Clone();
        }

        return AIFunctionFactory.CreateDeclaration(
            definition.Name,
            definition.Description,
            schemaElement);
    }

    private static Dictionary<string, object?>? ParseArguments(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
