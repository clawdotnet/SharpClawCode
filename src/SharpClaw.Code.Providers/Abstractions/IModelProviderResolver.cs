namespace SharpClaw.Code.Providers.Abstractions;

/// <summary>
/// Resolves concrete providers by name.
/// </summary>
public interface IModelProviderResolver
{
    /// <summary>
    /// Resolves a provider by name.
    /// </summary>
    /// <param name="providerName">The provider name.</param>
    /// <returns>The resolved provider.</returns>
    IModelProvider Resolve(string providerName);

    /// <summary>
    /// Resolves the requested provider followed by any configured fallback providers.
    /// </summary>
    /// <param name="providerName">The primary provider name.</param>
    /// <returns>The ordered provider candidates.</returns>
    IReadOnlyList<IModelProvider> ResolveCandidates(string providerName) => [Resolve(providerName)];
}
