using System.Text.Json;
using FluentAssertions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;

namespace SharpClaw.Code.UnitTests.Protocol;

/// <summary>
/// Verifies the generated JSON protocol metadata and representative payloads.
/// </summary>
public sealed class ProtocolJsonContextTests
{
    /// <summary>
    /// Ensures provider requests serialize with camelCase names and string enums.
    /// </summary>
    [Fact]
    public void Provider_request_should_serialize_with_protocol_json_context()
    {
        var request = new ProviderRequest(
            Id: "provider-request-001",
            SessionId: "session-001",
            TurnId: "turn-001",
            ProviderName: "openai",
            Model: "gpt-5.4",
            Prompt: "Inspect the workspace.",
            SystemPrompt: "Be concise.",
            OutputFormat: OutputFormat.Json,
            Temperature: 0.2m,
            Metadata: new Dictionary<string, string>
            {
                ["channel"] = "cli"
            });

        var json = JsonSerializer.Serialize(request, ProtocolJsonContext.Default.ProviderRequest);

        json.Should().Contain("\"providerName\":\"openai\"");
        json.Should().Contain("\"outputFormat\":\"json\"");
        json.Should().Contain("\"metadata\":{\"channel\":\"cli\"}");
    }

    /// <summary>
    /// Ensures runtime events round-trip through the protocol polymorphic contract.
    /// </summary>
    [Fact]
    public void Runtime_events_should_round_trip_polymorphically()
    {
        RuntimeEvent runtimeEvent = new ToolCompletedEvent(
            EventId: "event-001",
            SessionId: "session-001",
            TurnId: "turn-001",
            OccurredAtUtc: DateTimeOffset.Parse("2026-04-05T22:00:00Z"),
            Result: new ToolResult(
                RequestId: "tool-request-001",
                ToolName: "Shell",
                Succeeded: true,
                OutputFormat: OutputFormat.Json,
                Output: "{\"status\":\"ok\"}",
                ErrorMessage: null,
                ExitCode: 0,
                DurationMilliseconds: 42,
                StructuredOutputJson: "{\"status\":\"ok\"}"));

        var json = JsonSerializer.Serialize(runtimeEvent, ProtocolJsonContext.Default.RuntimeEvent);
        var deserialized = JsonSerializer.Deserialize(json, ProtocolJsonContext.Default.RuntimeEvent);

        deserialized.Should().BeOfType<ToolCompletedEvent>();
        json.Should().Contain("\"$eventType\":\"toolCompleted\"");
    }
}
