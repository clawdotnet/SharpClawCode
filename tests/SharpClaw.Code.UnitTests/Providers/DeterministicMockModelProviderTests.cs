using FluentAssertions;
using SharpClaw.Code.MockProvider;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.UnitTests.Providers;

public sealed class DeterministicMockModelProviderTests
{
    [Fact]
    public async Task StartStreamAsync_should_emit_deterministic_ids_and_timestamps()
    {
        var provider = new DeterministicMockModelProvider();
        var request = new ProviderRequest(
            Id: "provider-request-001",
            SessionId: "session-1",
            TurnId: "turn-1",
            ProviderName: DeterministicMockModelProvider.ProviderNameConstant,
            Model: DeterministicMockModelProvider.DefaultModelId,
            Prompt: "hello",
            SystemPrompt: "system",
            OutputFormat: OutputFormat.Text,
            Temperature: 0.1m,
            Metadata: new Dictionary<string, string>
            {
                [ParityMetadataKeys.Scenario] = ParityProviderScenario.StreamingText,
            });

        var handle = await provider.StartStreamAsync(request, CancellationToken.None);
        var events = new List<ProviderEvent>();

        await foreach (var providerEvent in handle.Events)
        {
            events.Add(providerEvent);
        }

        events.Should().HaveCount(3);
        events.Select(e => e.Id).Should().ContainInOrder(
            "provider-request-001-evt-01",
            "provider-request-001-evt-02",
            "provider-request-001-evt-03");
        events.Select(e => e.CreatedAtUtc).Should().ContainInOrder(
            new DateTimeOffset(2026, 4, 6, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(1),
            new DateTimeOffset(2026, 4, 6, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(2),
            new DateTimeOffset(2026, 4, 6, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(3));
    }
}
