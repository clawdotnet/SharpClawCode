using System.Text.Json;
using Anthropic.Models.Messages;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers.Internal;

/// <summary>
/// Maps SharpClaw conversation types to Anthropic SDK request parameters.
/// </summary>
internal static class AnthropicMessageBuilder
{
    /// <summary>
    /// Converts an array of <see cref="ChatMessage"/> records into Anthropic <see cref="MessageParam"/> instances.
    /// </summary>
    public static MessageParam[] BuildMessages(IReadOnlyList<ChatMessage> messages)
    {
        var result = new MessageParam[messages.Count];
        for (var i = 0; i < messages.Count; i++)
        {
            result[i] = BuildMessageParam(messages[i]);
        }

        return result;
    }

    /// <summary>
    /// Converts an array of <see cref="ProviderToolDefinition"/> records into Anthropic <see cref="ToolUnion"/> instances.
    /// </summary>
    public static ToolUnion[] BuildTools(IReadOnlyList<ProviderToolDefinition> tools)
    {
        var result = new ToolUnion[tools.Count];
        for (var i = 0; i < tools.Count; i++)
        {
            result[i] = BuildTool(tools[i]);
        }

        return result;
    }

    private static MessageParam BuildMessageParam(ChatMessage message)
    {
        var role = message.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
            ? Role.Assistant
            : Role.User;

        var contentBlocks = new List<ContentBlockParam>(message.Content.Count);
        foreach (var block in message.Content)
        {
            var param = BuildContentBlockParam(block);
            if (param is not null)
            {
                contentBlocks.Add(param);
            }
        }

        return new MessageParam
        {
            Role = role,
            Content = contentBlocks,
        };
    }

    private static ContentBlockParam? BuildContentBlockParam(Protocol.Models.ContentBlock block)
    {
        switch (block.Kind)
        {
            case ContentBlockKind.Text:
                var textParam = new TextBlockParam { Text = block.Text ?? string.Empty };
                return new ContentBlockParam(textParam, null);

            case ContentBlockKind.ToolUse:
                var input = ParseInputJson(block.ToolInputJson);
                var toolUseParam = new ToolUseBlockParam
                {
                    ID = block.ToolUseId ?? string.Empty,
                    Name = block.ToolName ?? string.Empty,
                    Input = input,
                };
                return new ContentBlockParam(toolUseParam, null);

            case ContentBlockKind.ToolResult:
                var toolResult = new ToolResultBlockParam(block.ToolUseId ?? string.Empty)
                {
                    Content = block.Text ?? string.Empty,
                    IsError = block.IsError,
                };
                return new ContentBlockParam(toolResult, null);

            default:
                return null;
        }
    }

    private static Tool BuildTool(ProviderToolDefinition definition)
    {
        var schemaJson = definition.InputSchemaJson ?? """{"type":"object","properties":{}}""";
        var rawData = ParseSchemaToRawData(schemaJson);
        var schema = InputSchema.FromRawUnchecked(rawData);

        return new Tool
        {
            Name = definition.Name,
            Description = definition.Description,
            InputSchema = schema,
        };
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseInputJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, JsonElement>();
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => p.Value.Clone());
        }
        catch (JsonException)
        {
            return new Dictionary<string, JsonElement>();
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> ParseSchemaToRawData(string schemaJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(schemaJson);
            return doc.RootElement.EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => p.Value.Clone());
        }
        catch (JsonException)
        {
            using var fallback = JsonDocument.Parse("""{"type":"object","properties":{}}""");
            return fallback.RootElement.EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => p.Value.Clone());
        }
    }
}
