using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Abstractions;

/// <summary>
/// Discovers custom commands from workspace and user profile directories.
/// </summary>
public interface ICustomCommandDiscoveryService
{
    /// <summary>
    /// Refreshes the command catalog for <paramref name="workspacePath"/>.
    /// </summary>
    /// <param name="workspacePath">Normalized workspace root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Catalog snapshot with workspace overriding global names.</returns>
    Task<CustomCommandCatalogSnapshot> DiscoverAsync(string workspacePath, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a command by name using the latest <see cref="DiscoverAsync"/> rules without caching requirement.
    /// </summary>
    Task<CustomCommandDefinition?> FindAsync(string workspacePath, string commandName, CancellationToken cancellationToken);
}
