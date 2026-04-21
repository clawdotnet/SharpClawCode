using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;

namespace SharpClaw.Code.UnitTests.Support;

internal static class TestRuntimeStorageResolver
{
    public static IRuntimeStoragePathResolver Create(string userRoot, IPathService? pathService = null)
    {
        var effectivePathService = pathService ?? new PathService();
        return new RuntimeStoragePathResolver(
            effectivePathService,
            new FixedUserProfilePaths(userRoot, effectivePathService),
            new RuntimeHostContextAccessor());
    }

    private sealed class FixedUserProfilePaths(string root, IPathService pathService) : IUserProfilePaths
    {
        public string GetUserCustomCommandsDirectory()
            => pathService.Combine(root, "commands");

        public string GetUserHomeDirectory()
            => root;

        public string GetUserSharpClawRoot()
            => root;
    }
}
