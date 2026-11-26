using FluentAssertions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.UnitTests.Infrastructure;

/// <summary>
/// Verifies tenant isolation and session-id validation in storage path resolution.
/// </summary>
public sealed class RuntimeStoragePathResolverTests
{
    private readonly PathService pathService = new();
    private readonly RuntimeHostContextAccessor accessor = new();
    private readonly string userRoot = Path.Combine(Path.GetTempPath(), $"sharpclaw-paths-{Guid.NewGuid():N}");

    [Fact]
    public void Distinct_tenant_ids_must_never_share_a_storage_root()
    {
        var resolver = CreateResolver();
        var hashedName = "acme-corp~" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("acme.corp"))).ToLowerInvariant()[..12];
        var legacyGeneratedName = "acme-corp-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("acme.corp"))).ToLowerInvariant()[..12];
        var roots = new[] { "acme-corp", "acme.corp", "acme/corp", "acme!corp", legacyGeneratedName, hashedName }
            .Select(tenant =>
            {
                using var scope = accessor.BeginScope(new RuntimeHostContext("test-host", TenantId: tenant));
                return resolver.GetSharpClawRoot("/workspace");
            })
            .ToArray();

        roots.Should().OnlyHaveUniqueItems();
        roots[0].Should().EndWith("acme-corp", "clean tenant ids keep their readable directory name");
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("/etc")]
    [InlineData(".. ")]
    [InlineData("...")]
    [InlineData("session.")]
    [InlineData("session ")]
    public void Session_ids_must_be_a_single_path_segment(string sessionId)
    {
        var resolver = CreateResolver();

        var act = () => resolver.GetSessionRoot("/workspace", sessionId);

        act.Should().Throw<ArgumentException>();
    }

    private RuntimeStoragePathResolver CreateResolver()
        => new(pathService, new FixedUserProfilePaths(userRoot), accessor);

    private sealed class FixedUserProfilePaths(string root) : IUserProfilePaths
    {
        public string GetUserCustomCommandsDirectory() => Path.Combine(root, "commands");

        public string GetUserHomeDirectory() => root;

        public string GetUserSharpClawRoot() => root;
    }
}
