using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.CustomCommands;

/// <inheritdoc />
public sealed class CustomCommandDiscoveryService(
    IFileSystem fileSystem,
    IPathService pathService,
    IUserProfilePaths userProfilePaths,
    ICustomCommandMarkdownParser parser,
    ISystemClock clock) : ICustomCommandDiscoveryService
{
    /// <inheritdoc />
    public async Task<CustomCommandCatalogSnapshot> DiscoverAsync(string workspacePath, CancellationToken cancellationToken)
    {
        var issues = new List<CustomCommandDiscoveryIssue>();
        var byName = new Dictionary<string, CustomCommandDefinition>(StringComparer.OrdinalIgnoreCase);

        await IngestDirectoryAsync(
            userProfilePaths.GetUserCustomCommandsDirectory(),
            CustomCommandSourceScope.Global,
            byName,
            issues,
            cancellationToken).ConfigureAwait(false);

        var wsCommands = pathService.Combine(pathService.GetFullPath(workspacePath), ".sharpclaw", "commands");
        await IngestDirectoryAsync(
            wsCommands,
            CustomCommandSourceScope.Workspace,
            byName,
            issues,
            cancellationToken).ConfigureAwait(false);

        return new CustomCommandCatalogSnapshot(
            Commands: byName.Values.OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            Issues: issues,
            GeneratedAtUtc: clock.UtcNow);
    }

    /// <inheritdoc />
    public async Task<CustomCommandDefinition?> FindAsync(string workspacePath, string commandName, CancellationToken cancellationToken)
    {
        var snap = await DiscoverAsync(workspacePath, cancellationToken).ConfigureAwait(false);
        return snap.Commands.FirstOrDefault(c => string.Equals(c.Name, commandName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task IngestDirectoryAsync(
        string directory,
        CustomCommandSourceScope scope,
        Dictionary<string, CustomCommandDefinition> byName,
        List<CustomCommandDiscoveryIssue> issues,
        CancellationToken cancellationToken)
    {
        if (!fileSystem.DirectoryExists(directory))
        {
            return;
        }

        var canonicalRoot = pathService.GetFullPath(directory);
        var canonicalRootWithSep = canonicalRoot.EndsWith(Path.DirectorySeparatorChar)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;

        foreach (var file in fileSystem.EnumerateFiles(directory, "*.md"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var canonicalFile = pathService.GetFullPath(file);
            if (!canonicalFile.StartsWith(canonicalRootWithSep, StringComparison.Ordinal))
            {
                issues.Add(new CustomCommandDiscoveryIssue(file, "Command file path escapes the commands directory; refusing to load."));
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(file);
            if (string.IsNullOrWhiteSpace(name))
            {
                issues.Add(new CustomCommandDiscoveryIssue(file, "Invalid command file name."));
                continue;
            }

            var text = await fileSystem.ReadAllTextIfExistsAsync(file, cancellationToken).ConfigureAwait(false);
            if (text is null)
            {
                issues.Add(new CustomCommandDiscoveryIssue(file, "Unable to read command file."));
                continue;
            }

            var (def, parseIssues) = parser.Parse(name, pathService.GetFullPath(file), scope, text);
            issues.AddRange(parseIssues);
            if (def is not null)
            {
                byName[def.Name] = def;
            }
        }
    }
}
