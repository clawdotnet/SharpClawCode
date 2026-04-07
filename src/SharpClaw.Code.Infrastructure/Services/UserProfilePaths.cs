using SharpClaw.Code.Infrastructure.Abstractions;

namespace SharpClaw.Code.Infrastructure.Services;

/// <inheritdoc />
public sealed class UserProfilePaths(IPathService pathService) : IUserProfilePaths
{
    /// <inheritdoc />
    public string GetUserHomeDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(home)
            ? Environment.GetFolderPath(Environment.SpecialFolder.Personal)
            : pathService.GetFullPath(home);
    }

    /// <inheritdoc />
    public string GetUserSharpClawRoot()
        => pathService.GetFullPath(pathService.Combine(GetUserHomeDirectory(), ".sharpclaw"));

    /// <inheritdoc />
    public string GetUserCustomCommandsDirectory()
        => pathService.GetFullPath(pathService.Combine(GetUserSharpClawRoot(), "commands"));
}
