using FluentAssertions;
using SharpClaw.Code.Agents.Agents;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.CustomCommands;

namespace SharpClaw.Code.UnitTests.Runtime;

public sealed class CustomCommandMarkdownParserTests
{
    private readonly CustomCommandMarkdownParser _parser = new();

    [Fact]
    public void Parse_rejects_sub_agent_worker_until_delegation_is_wired()
    {
        var md = """
            ---
            agent: sub-agent-worker
            ---
            body
            """;

        var (def, issues) = _parser.Parse("test", "/tmp/x.md", CustomCommandSourceScope.Workspace, md);

        def.Should().BeNull();
        issues.Should().ContainSingle(i => i.Message.Contains(SubAgentWorker.SubAgentId, StringComparison.Ordinal));
    }
}
