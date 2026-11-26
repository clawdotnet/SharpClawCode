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
        var roots = new[] { "acme-corp", "acme.corp", "acme/corp", "acme!corp" }
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
    public void Session_ids_must_be_a_single_path_segment(string sessionId)
    {
        var resolver = CreateResolver();

        var act = () => resolver.GetSessionRoot("/workspace", sessionId);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Hosted_workspace_keys_ignore_path_casing_when_paths_are_case_insensitive()
    {
        var storageRoot = Path.Combine(userRoot, "hosted");
        var resolver = new RuntimeStoragePathResolver(pathService, new FixedUserProfilePaths(userRoot), accessor, caseInsensitivePaths: true);
        using var scope = accessor.BeginScope(new RuntimeHostContext("test-host", StorageRoot: storageRoot));

        var upper = resolver.GetSharpClawRoot(Path.Combine(userRoot, "Work", "Repo"));
        var lower = resolver.GetSharpClawRoot(Path.Combine(userRoot, "work", "repo"));

        upper.ToUpperInvariant().Should().Be(lower.ToUpperInvariant(), "C:\\Repo and c:\\repo are the same workspace on Windows");
    }

    [Fact]
    public void Hosted_workspace_keys_keep_using_an_existing_legacy_exact_case_root()
    {
        var storageRoot = Path.Combine(userRoot, "hosted-legacy");
        var workspacePath = Path.Combine(userRoot, "Work", "Repo");
        using var scope = accessor.BeginScope(new RuntimeHostContext("test-host", StorageRoot: storageRoot));
        var legacyRoot = new RuntimeStoragePathResolver(pathService, new FixedUserProfilePaths(userRoot), accessor, caseInsensitivePaths: false)
            .GetSharpClawRoot(workspacePath);
        var legacyWorkspaceDirectory = Path.GetDirectoryName(legacyRoot)!;
        Directory.CreateDirectory(legacyWorkspaceDirectory);

        try
        {
            var resolved = new RuntimeStoragePathResolver(pathService, new FixedUserProfilePaths(userRoot), accessor, caseInsensitivePaths: true)
                .GetSharpClawRoot(workspacePath);

            resolved.Should().Be(legacyRoot, "existing hosted sessions must not disappear after the key change");
        }
        finally
        {
            Directory.Delete(userRoot, recursive: true);
        }
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
