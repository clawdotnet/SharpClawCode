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
}
