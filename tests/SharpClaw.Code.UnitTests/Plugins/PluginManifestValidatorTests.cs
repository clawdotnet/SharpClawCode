using FluentAssertions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Plugins.Services;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.UnitTests.Plugins;

/// <summary>
/// Tests for <see cref="PluginManifestValidator" /> safety rules.
/// </summary>
public sealed class PluginManifestValidatorTests
{
    private readonly PluginManifestValidator validator = new();

    /// <summary>
    /// Ensures plugin ids cannot embed path segments.
    /// </summary>
    [Fact]
    public void Validate_should_reject_plugin_id_with_path_separators()
    {
        var manifest = new PluginManifest("acme/bad", "Bad", "1", null, "tool.exe", null, null, null);
        var act = () => validator.Validate(manifest);
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Ensures reserved directory names are rejected as plugin ids.
    /// </summary>
    [Fact]
    public void Validate_should_reject_dot_plugin_ids()
    {
        var act = () => validator.Validate(new PluginManifest(".", "Bad", "1", null, "tool.exe", null, null, null));
        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>
    /// Ensures conventional plugin ids pass validation.
    /// </summary>
    [Fact]
    public void Validate_should_accept_well_formed_plugin_id()
    {
        var manifest = new PluginManifest(
            "acme.hello",
            "Hello",
            "1.0.0",
            null,
            "hello.exe",
            null,
            null,
            null,
            Trust: PluginTrustLevel.WorkspaceTrusted);
        var act = () => validator.Validate(manifest);
        act.Should().NotThrow();
    }
}
