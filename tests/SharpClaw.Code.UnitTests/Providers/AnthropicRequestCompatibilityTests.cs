using System.Text.Json;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers;

namespace SharpClaw.Code.UnitTests.Providers;

/// <summary>Verifies wire compatibility with Anthropic models that reject temperature.</summary>
public sealed class AnthropicRequestCompatibilityTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Requests_omit_temperature_and_preserve_message_options(bool useHistory)
    {
        var request = new ProviderRequest(
            "request", "session", "turn", "anthropic", "claude-model", "Hello", "Be concise.",
            OutputFormat.Text, 0.2m, null,
            Messages: useHistory ? [new ChatMessage("user", [new ContentBlock(ContentBlockKind.Text, "Hello", null, null, null, null)])] : null,
            Tools: useHistory ? [new ProviderToolDefinition("read_file", "Read a file", "{\"type\":\"object\"}")] : null,
            MaxTokens: 2048);

        var parameters = AnthropicProvider.CreateMessageParameters(request, request.Model);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(parameters.RawBodyData));
        var root = json.RootElement;

        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.Equal("claude-model", root.GetProperty("model").GetString());
        Assert.Equal(2048, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal("Be concise.", root.GetProperty("system").GetString());
        Assert.Single(root.GetProperty("messages").EnumerateArray());
        if (useHistory)
        {
            Assert.Equal("read_file", root.GetProperty("tools")[0].GetProperty("name").GetString());
        }
    }
}
