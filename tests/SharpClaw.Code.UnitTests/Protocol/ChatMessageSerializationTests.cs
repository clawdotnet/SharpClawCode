using System.Text.Json;
using FluentAssertions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.UnitTests.Protocol;

/// <summary>
/// Verifies roundtrip JSON serialization of <see cref="ChatMessage"/> and <see cref="ContentBlock"/>.
/// </summary>
public sealed class ChatMessageSerializationTests
{
    /// <summary>
    /// A user message carrying a plain text block survives a full serialize/deserialize cycle.
    /// </summary>
    [Fact]
    public void ChatMessage_with_text_block_roundtrips()
    {
        var message = new ChatMessage(
            Role: "user",
            Content:
            [
                new ContentBlock(
                    Kind: ContentBlockKind.Text,
                    Text: "Hello, world!",
                    ToolUseId: null,
                    ToolName: null,
                    ToolInputJson: null,
                    IsError: null)
            ]);

        var json = JsonSerializer.Serialize(message, ProtocolJsonContext.Default.ChatMessage);
        var deserialized = JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.ChatMessage);

        deserialized.Should().NotBeNull();
        deserialized!.Role.Should().Be("user");
        deserialized.Content.Should().HaveCount(1);
        deserialized.Content[0].Kind.Should().Be(ContentBlockKind.Text);
        deserialized.Content[0].Text.Should().Be("Hello, world!");
    }

    /// <summary>
    /// An assistant message carrying a tool-use block survives a full serialize/deserialize cycle.
    /// </summary>
    [Fact]
    public void ChatMessage_with_tool_use_block_roundtrips()
    {
        var message = new ChatMessage(
            Role: "assistant",
            Content:
            [
                new ContentBlock(
                    Kind: ContentBlockKind.ToolUse,
                    Text: null,
                    ToolUseId: "call-1",
                    ToolName: "read_file",
                    ToolInputJson: "{\"path\":\"a.cs\"}",
                    IsError: null)
            ]);

        var json = JsonSerializer.Serialize(message, ProtocolJsonContext.Default.ChatMessage);
        var deserialized = JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.ChatMessage);

        deserialized.Should().NotBeNull();
        deserialized!.Role.Should().Be("assistant");
        deserialized.Content.Should().HaveCount(1);

        var block = deserialized.Content[0];
        block.Kind.Should().Be(ContentBlockKind.ToolUse);
        block.ToolUseId.Should().Be("call-1");
        block.ToolName.Should().Be("read_file");
        block.ToolInputJson.Should().Be("{\"path\":\"a.cs\"}");
    }

    /// <summary>
    /// A user message carrying a tool-result block survives a full serialize/deserialize cycle.
    /// </summary>
    [Fact]
    public void ChatMessage_with_tool_result_block_roundtrips()
    {
        var message = new ChatMessage(
            Role: "user",
            Content:
            [
                new ContentBlock(
                    Kind: ContentBlockKind.ToolResult,
                    Text: "file contents",
                    ToolUseId: "call-1",
                    ToolName: null,
                    ToolInputJson: null,
                    IsError: null)
            ]);

        var json = JsonSerializer.Serialize(message, ProtocolJsonContext.Default.ChatMessage);
        var deserialized = JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.ChatMessage);

        deserialized.Should().NotBeNull();
        deserialized!.Role.Should().Be("user");
        deserialized.Content.Should().HaveCount(1);

        var block = deserialized.Content[0];
        block.Kind.Should().Be(ContentBlockKind.ToolResult);
        block.ToolUseId.Should().Be("call-1");
        block.Text.Should().Be("file contents");
    }
}
