using FluentAssertions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.CustomCommands;

namespace SharpClaw.Code.UnitTests.Runtime;

public sealed class CustomCommandWorkflowTests
{
    [Fact]
    public void Template_expander_substitutes_arguments_and_placeholders()
    {
        var body = "Hello $ARGUMENTS / $1 / $2";
        var expanded = CustomCommandTemplateExpander.Expand(body, @"one ""quoted two""");
        expanded.Should().Contain("one");
        expanded.Should().Contain("quoted two");
    }

    [Fact]
    public void Markdown_parser_reads_frontmatter_and_body()
    {
        ICustomCommandMarkdownParser parser = new CustomCommandMarkdownParser();
        var md = """
            ---
            description: Test command
            primaryMode: plan
            permissionMode: readOnly
            agent: advisor-agent
            ---
            Do something with $ARGUMENTS
            """;
        var (def, issues) = parser.Parse("demo", "/tmp/demo.md", CustomCommandSourceScope.Workspace, md);
        issues.Should().BeEmpty();
        def.Should().NotBeNull();
        def!.Description.Should().Be("Test command");
        def.PrimaryModeOverride.Should().Be(PrimaryMode.Plan);
        def.PermissionMode.Should().Be(PermissionMode.ReadOnly);
        def.AgentId.Should().Be("advisor-agent");
        def.TemplateBody.Trim().Should().StartWith("Do something");
    }
}
