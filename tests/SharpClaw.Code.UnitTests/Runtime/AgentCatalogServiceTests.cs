using FluentAssertions;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Agents.Models;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Workflow;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>
/// Verifies configured agents preserve their immediate parent relationship in the resolved catalog.
/// </summary>
public sealed class AgentCatalogServiceTests
{
    [Fact]
    public async Task ListAsync_preserves_immediate_parent_for_multi_level_configured_agents()
    {
        var service = new AgentCatalogService(
            [
                new StubAgent("primary-coding-agent", "primary"),
            ],
            new FixedConfigService(
                new SharpClawConfigDocument(
                    ShareMode.Manual,
                    null,
                    null,
                    [
                        new ConfiguredAgentDefinition(
                            Id: "derived-parent",
                            Name: "Derived Parent",
                            Description: "Parent agent.",
                            BaseAgentId: "primary-coding-agent",
                            Model: null,
                            PrimaryMode: null,
                            AllowedTools: null,
                            InstructionAppendix: null),
                        new ConfiguredAgentDefinition(
                            Id: "derived-child",
                            Name: "Derived Child",
                            Description: "Child agent.",
                            BaseAgentId: "derived-parent",
                            Model: null,
                            PrimaryMode: null,
                            AllowedTools: null,
                            InstructionAppendix: null),
                    ],
                    null,
                    null,
                    null)));

        var entries = await service.ListAsync(Path.Combine(Path.GetTempPath(), "sharpclaw-agent-catalog-tests"), CancellationToken.None);

        entries.Should().ContainSingle(entry => entry.Id == "derived-child" && entry.BaseAgentId == "derived-parent");
    }

    private sealed class FixedConfigService(SharpClawConfigDocument document) : ISharpClawConfigService
    {
        public Task<SharpClawConfigSnapshot> GetConfigAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(new SharpClawConfigSnapshot(workspaceRoot, null, null, document));
    }

    private sealed class StubAgent(string agentId, string agentKind) : ISharpClawAgent
    {
        public string AgentId { get; } = agentId;

        public string AgentKind { get; } = agentKind;

        public Task<AgentRunResult> RunAsync(AgentRunContext context, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
