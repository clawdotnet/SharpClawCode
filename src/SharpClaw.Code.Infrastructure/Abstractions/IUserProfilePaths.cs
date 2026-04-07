namespace SharpClaw.Code.Infrastructure.Abstractions;

/// <summary>
/// Resolves user-scoped directories for SharpClaw state outside workspaces.
/// </summary>
public interface IUserProfilePaths
{
    /// <summary>Gets the current user's home/profile directory (cross-platform).</summary>
    string GetUserHomeDirectory();

    /// <summary>Gets <c>{home}/.sharpclaw</c> as a normalized path.</summary>
    string GetUserSharpClawRoot();

    /// <summary>Gets the directory for global custom command markdown files.</summary>
    string GetUserCustomCommandsDirectory();
}
