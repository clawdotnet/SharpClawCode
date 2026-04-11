using System.Globalization;
using System.Text.Json;
using Anthropic.Models.Messages;
using FluentAssertions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Providers.Internal;
using ProtocolModels = SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.UnitTests.Providers;

/// <summary>
/// Tests for tool-use stream events and message/tool builder mappings.
/// </summary>
public sealed class ToolUseStreamAdapterTests
{
    [Fact]
    public void ToolUse_factory_creates_event_with_tool_metadata()
    {
        var clock = new FixedClock();
        var ev = ProviderStreamEventFactory.ToolUse("req-1", clock, "call-1", "read_file", "{\"path\":\"a.cs\"}");

        ev.Kind.Should().Be("tool_use");
        ev.BlockType.Should().Be("tool_use");
        ev.ToolUseId.Should().Be("call-1");
        ev.ToolName.Should().Be("read_file");
        ev.ToolInputJson.Should().Be("{\"path\":\"a.cs\"}");
        ev.IsTerminal.Should().BeFalse();
        ev.Content.Should().BeNull();
        ev.Usage.Should().BeNull();
        ev.RequestId.Should().Be("req-1");
    }

    [Fact]
    public async Task Anthropic_sdk_adapter_emits_tool_use_event_on_complete_tool_block()
    {
        var clock = new FixedClock();

        async IAsyncEnumerable<RawMessageStreamEvent> Stream()
        {
            var toolUseBlock = MakeToolUseBlock("call-42", "list_files");
            yield return new RawMessageStreamEvent(
                new RawContentBlockStartEvent
                {
                    Index = 0,
                    ContentBlock = new RawContentBlockStartEventContentBlock(toolUseBlock, null),
                },
                default);

            yield return new RawMessageStreamEvent(
                new RawContentBlockDeltaEvent
                {
                    Index = 0,
                    Delta = new RawContentBlockDelta(new InputJsonDelta { PartialJson = "{\"pa" }, null),
                },
                default);

            yield return new RawMessageStreamEvent(
                new RawContentBlockDeltaEvent
                {
                    Index = 0,
                    Delta = new RawContentBlockDelta(new InputJsonDelta { PartialJson = "th\":\"src\"}" }, null),
                },
                default);

            yield return new RawMessageStreamEvent(new RawContentBlockStopEvent { Index = 0 }, default);
            yield return new RawMessageStreamEvent(new RawMessageStopEvent(), default);
        }

        var events = new List<ProtocolModels.ProviderEvent>();
        await foreach (var e in AnthropicSdkStreamAdapter.AdaptAsync(Stream(), "req-tool", clock, CancellationToken.None))
        {
            events.Add(e);
        }

        events.Should().HaveCount(2);

        var toolEvent = events[0];
        toolEvent.Kind.Should().Be("tool_use");
        toolEvent.BlockType.Should().Be("tool_use");
        toolEvent.ToolUseId.Should().Be("call-42");
        toolEvent.ToolName.Should().Be("list_files");
        toolEvent.ToolInputJson.Should().Be("{\"path\":\"src\"}");
        toolEvent.IsTerminal.Should().BeFalse();

        var completedEvent = events[1];
        completedEvent.Kind.Should().Be("completed");
        completedEvent.IsTerminal.Should().BeTrue();
    }

    [Fact]
    public async Task Anthropic_sdk_adapter_mixes_text_and_tool_use_blocks()
    {
        var clock = new FixedClock();

        async IAsyncEnumerable<RawMessageStreamEvent> Stream()
        {
            yield return new RawMessageStreamEvent(
                new RawContentBlockDeltaEvent
                {
                    Index = 0,
                    Delta = new RawContentBlockDelta(new TextDelta("Let me check."), null),
                },
                default);

            var toolUseBlock = MakeToolUseBlock("call-7", "read_file");
            yield return new RawMessageStreamEvent(
                new RawContentBlockStartEvent
                {
                    Index = 1,
                    ContentBlock = new RawContentBlockStartEventContentBlock(toolUseBlock, null),
                },
                default);

            yield return new RawMessageStreamEvent(
                new RawContentBlockDeltaEvent
                {
                    Index = 1,
                    Delta = new RawContentBlockDelta(new InputJsonDelta { PartialJson = "{\"path\":\"b.cs\"}" }, null),
                },
                default);

            yield return new RawMessageStreamEvent(new RawContentBlockStopEvent { Index = 1 }, default);
            yield return new RawMessageStreamEvent(new RawMessageStopEvent(), default);
        }

        var events = new List<ProtocolModels.ProviderEvent>();
        await foreach (var e in AnthropicSdkStreamAdapter.AdaptAsync(Stream(), "req-mixed", clock, CancellationToken.None))
        {
            events.Add(e);
        }

        events.Should().HaveCount(3);
        events[0].Kind.Should().Be("delta");
        events[0].Content.Should().Be("Let me check.");
        events[1].Kind.Should().Be("tool_use");
        events[1].ToolName.Should().Be("read_file");
        events[2].Kind.Should().Be("completed");
    }

    [Fact]
    public void AnthropicMessageBuilder_builds_messages_from_chat_history()
    {
        var messages = new[]
        {
            new ProtocolModels.ChatMessage("user", new[]
            {
                new ProtocolModels.ContentBlock(ProtocolModels.ContentBlockKind.Text, "Hello", null, null, null, null),
            }),
            new ProtocolModels.ChatMessage("assistant", new[]
            {
                new ProtocolModels.ContentBlock(ProtocolModels.ContentBlockKind.Text, "Hi there", null, null, null, null),
                new ProtocolModels.ContentBlock(ProtocolModels.ContentBlockKind.ToolUse, null, "call-1", "get_time", "{}", null),
            }),
            new ProtocolModels.ChatMessage("user", new[]
            {
                new ProtocolModels.ContentBlock(ProtocolModels.ContentBlockKind.ToolResult, "12:00 PM", "call-1", null, null, false),
            }),
        };

        var result = AnthropicMessageBuilder.BuildMessages(messages);

        result.Should().HaveCount(3);
        result[0].Role.Raw().Should().Be("user");
        result[1].Role.Raw().Should().Be("assistant");
        result[2].Role.Raw().Should().Be("user");
    }

    [Fact]
    public void AnthropicMessageBuilder_builds_tools_from_definitions()
    {
        var definitions = new[]
        {
            new ProtocolModels.ProviderToolDefinition(
                "read_file",
                "Read the contents of a file.",
                """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""),
        };

        var result = AnthropicMessageBuilder.BuildTools(definitions);

        result.Should().HaveCount(1);
        result[0].TryPickTool(out var tool).Should().BeTrue();
        tool!.Name.Should().Be("read_file");
        tool.Description.Should().Be("Read the contents of a file.");
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void AnthropicMessageBuilder_handles_null_input_schema_gracefully()
    {
        var definitions = new[]
        {
            new ProtocolModels.ProviderToolDefinition("noop", "Does nothing.", null),
        };

        var result = AnthropicMessageBuilder.BuildTools(definitions);

        result.Should().HaveCount(1);
        result[0].TryPickTool(out var tool).Should().BeTrue();
        tool!.Name.Should().Be("noop");
        tool.InputSchema.Should().NotBeNull();
    }

    [Fact]
    public void OpenAi_message_builder_maps_roles_correctly()
    {
        var messages = new[]
        {
            new ProtocolModels.ChatMessage("system", new[]
            {
                new ProtocolModels.ContentBlock(ProtocolModels.ContentBlockKind.Text, "You are a helpful assistant.", null, null, null, null),
            }),
            new ProtocolModels.ChatMessage("user", new[]
            {
                new ProtocolModels.ContentBlock(ProtocolModels.ContentBlockKind.Text, "Hello", null, null, null, null),
            }),
            new ProtocolModels.ChatMessage("assistant", new[]
            {
                new ProtocolModels.ContentBlock(ProtocolModels.ContentBlockKind.Text, "Hi there", null, null, null, null),
            }),
        };

        var result = OpenAiMessageBuilder.BuildMessages(messages);

        result.Should().HaveCount(3);
        result[0].Role.Should().Be(Microsoft.Extensions.AI.ChatRole.System);
        result[1].Role.Should().Be(Microsoft.Extensions.AI.ChatRole.User);
        result[2].Role.Should().Be(Microsoft.Extensions.AI.ChatRole.Assistant);
    }

    [Fact]
    public void OpenAi_message_builder_maps_tool_use_to_function_call_content()
    {
        var messages = new[]
        {
            new ProtocolModels.ChatMessage("assistant", new[]
            {
                new ProtocolModels.ContentBlock(ProtocolModels.ContentBlockKind.Text, "Let me check.", null, null, null, null),
                new ProtocolModels.ContentBlock(ProtocolModels.ContentBlockKind.ToolUse, null, "call-99", "read_file", "{\"path\":\"src/main.cs\"}", null),
            }),
        };

        var result = OpenAiMessageBuilder.BuildMessages(messages);

        result.Should().HaveCount(1);
        result[0].Role.Should().Be(Microsoft.Extensions.AI.ChatRole.Assistant);
        result[0].Contents.Should().HaveCount(2);

        var textItem = result[0].Contents[0];
        textItem.Should().BeOfType<Microsoft.Extensions.AI.TextContent>();
        ((Microsoft.Extensions.AI.TextContent)textItem).Text.Should().Be("Let me check.");

        var callItem = result[0].Contents[1];
        callItem.Should().BeOfType<Microsoft.Extensions.AI.FunctionCallContent>();
        var functionCall = (Microsoft.Extensions.AI.FunctionCallContent)callItem;
        functionCall.CallId.Should().Be("call-99");
        functionCall.Name.Should().Be("read_file");
    }

    [Fact]
    public void OpenAi_message_builder_maps_tool_result_to_function_result_content()
    {
        var messages = new[]
        {
            new ProtocolModels.ChatMessage("user", new[]
            {
                new ProtocolModels.ContentBlock(ProtocolModels.ContentBlockKind.ToolResult, "namespace Foo;", "call-99", null, null, false),
            }),
        };

        var result = OpenAiMessageBuilder.BuildMessages(messages);

        result.Should().HaveCount(1);
        result[0].Role.Should().Be(Microsoft.Extensions.AI.ChatRole.User);
        result[0].Contents.Should().HaveCount(1);

        var resultItem = result[0].Contents[0];
        resultItem.Should().BeOfType<Microsoft.Extensions.AI.FunctionResultContent>();
        var functionResult = (Microsoft.Extensions.AI.FunctionResultContent)resultItem;
        functionResult.CallId.Should().Be("call-99");
        functionResult.Result.Should().Be("namespace Foo;");
    }

    [Fact]
    public void OpenAi_message_builder_builds_tools_from_definitions()
    {
        var definitions = new[]
        {
            new ProtocolModels.ProviderToolDefinition(
                "read_file",
                "Read the contents of a file.",
                """{"type":"object","properties":{"path":{"type":"string"}},"required":["path"]}"""),
        };

        var result = OpenAiMessageBuilder.BuildTools(definitions);

        result.Should().HaveCount(1);
        result[0].Should().NotBeNull();

        var funcDecl = result[0] as Microsoft.Extensions.AI.AIFunctionDeclaration;
        funcDecl.Should().NotBeNull();
        funcDecl!.Name.Should().Be("read_file");
        funcDecl.Description.Should().Be("Read the contents of a file.");
    }

    [Fact]
    public void OpenAi_message_builder_handles_null_input_schema_for_tools()
    {
        var definitions = new[]
        {
            new ProtocolModels.ProviderToolDefinition("noop", "Does nothing.", null),
        };

        var result = OpenAiMessageBuilder.BuildTools(definitions);

        result.Should().HaveCount(1);
        var funcDecl = result[0] as Microsoft.Extensions.AI.AIFunctionDeclaration;
        funcDecl.Should().NotBeNull();
        funcDecl!.Name.Should().Be("noop");
    }

    /// <summary>
    /// Creates a <see cref="ToolUseBlock"/> with all required members set, using the raw JSON deserialization path.
    /// </summary>
    private static ToolUseBlock MakeToolUseBlock(string id, string name)
    {
        var json = $"{{\"id\":\"{id}\",\"name\":\"{name}\",\"input\":{{}},\"type\":\"tool_use\",\"caller\":{{\"type\":\"direct\"}}}}";
        using var doc = JsonDocument.Parse(json);
        var rawData = doc.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone());
        return ToolUseBlock.FromRawUnchecked(rawData);
    }

    private sealed class FixedClock : ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-04-08T00:00:00Z", CultureInfo.InvariantCulture);
    }
}
