using System.Text.Json;
using FluentAssertions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Verification;
namespace SharpClaw.Code.UnitTests.Verification;
public sealed class VerificationPolicyTests
{
    [Fact]
    public async Task Defaults_are_disabled_and_metadata_overrides_configuration()
    {
        var resolver = new VerificationPolicyResolver(new Configuration(new(true, VerificationScope.All, true, 1, true)));
        var policy = await resolver.ResolveAsync("workspace", new Dictionary<string, string> { [SharpClawWorkflowMetadataKeys.VerificationEnabled] = "false", [SharpClawWorkflowMetadataKeys.VerificationScope] = "build", [SharpClawWorkflowMetadataKeys.VerificationMaxRepairIterations] = "2", [SharpClawWorkflowMetadataKeys.VerificationAllowRestore] = "false" }, CancellationToken.None);
        policy.Should().Be(new VerificationPolicy(false, VerificationScope.Build, true, 2, false));
        (await new VerificationPolicyResolver(new Configuration(null)).ResolveAsync("workspace", null, CancellationToken.None)).Should().Be(new VerificationPolicy());
        var old = JsonSerializer.Deserialize("""{"shareMode":"manual"}""", ProtocolJsonContext.Default.SharpClawConfigDocument)!;
        old.Verification.Should().BeNull();
    }
    [Theory]
    [InlineData("sharpclaw.verification.maxRepairIterations", "-1")]
    [InlineData("sharpclaw.verification.maxRepairIterations", "6")]
    [InlineData("sharpclaw.verification.scope", "unknown")]
    [InlineData("sharpclaw.verification.enabled", "yes")]
    public async Task Invalid_metadata_is_rejected_before_agent_execution(string key, string value)
    {
        var resolver = new VerificationPolicyResolver(new Configuration(null));
        var action = () => resolver.ResolveAsync("workspace", new Dictionary<string, string> { [key] = value }, CancellationToken.None);
        await action.Should().ThrowAsync<ArgumentException>();
    }
    private sealed class Configuration(VerificationOptions? options) : ISharpClawConfigService
    {
        public Task<SharpClawConfigSnapshot> GetConfigAsync(string workspaceRoot, CancellationToken cancellationToken) => Task.FromResult(new SharpClawConfigSnapshot(workspaceRoot, null, null, new(ShareMode.Manual, null, null, null, null, null, null, Verification: options)));
    }
}
