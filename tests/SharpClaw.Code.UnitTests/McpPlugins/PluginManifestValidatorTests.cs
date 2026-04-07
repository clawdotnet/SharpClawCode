using FluentAssertions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Plugins.Services;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.UnitTests.McpPlugins;

/// <summary>
/// Verifies plugin manifest validation rules for trust and tool schemas.
/// </summary>
public sealed class PluginManifestValidatorTests
{
    /// <summary>
    /// Ensures untrusted plugins cannot expose silent destructive tools.
    /// </summary>
    [Fact]
    public void Validate_should_reject_untrusted_destructive_tool_without_approval()
    {
        var manifest = new PluginManifest(
            Id: "bad.auto",
            Name: "Bad",
            Version: "1.0.0",
            Description: null,
            EntryPoint: "bad",
            Arguments: null,
            Capabilities: null,
            Tools:
            [
                new PluginToolDescriptor(
                    Name: "nuke",
                    Description: "Deletes things.",
                    InputDescription: "{}",
                    Tags: null,
                    IsDestructive: true,
                    RequiresApproval: false)
            ],
            Trust: PluginTrustLevel.Untrusted);

        var act = () => new PluginManifestValidator().Validate(manifest);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("Untrusted");
    }

    /// <summary>
    /// Ensures invalid JSON in inputSchemaJson is rejected.
    /// </summary>
    [Fact]
    public void Validate_should_reject_invalid_input_schema_json()
    {
        var manifest = new PluginManifest(
            Id: "bad.schema",
            Name: "Bad Schema",
            Version: "1.0.0",
            Description: null,
            EntryPoint: "tool",
            Arguments: null,
            Capabilities: null,
            Tools:
            [
                new PluginToolDescriptor(
                    Name: "x",
                    Description: "x",
                    InputDescription: "x",
                    Tags: null,
                    InputSchemaJson: "not-json")
            ],
            Trust: PluginTrustLevel.WorkspaceTrusted);

        var act = () => new PluginManifestValidator().Validate(manifest);

        act.Should().Throw<InvalidOperationException>()
            .Which.Message.Should().Contain("inputSchemaJson");
    }
}
