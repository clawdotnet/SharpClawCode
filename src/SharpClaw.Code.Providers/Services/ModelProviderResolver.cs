using SharpClaw.Code.Providers.Abstractions;

namespace SharpClaw.Code.Providers;

/// <summary>
/// Resolves configured model providers by name.
/// </summary>
public sealed class ModelProviderResolver(IEnumerable<IModelProvider> providers) : IModelProviderResolver
{
    private readonly IReadOnlyDictionary<string, IModelProvider> _providers = providers.ToDictionary(
        provider => provider.ProviderName,
        provider => provider,
        StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public IModelProvider Resolve(string providerName)
        => _providers.TryGetValue(providerName, out var provider)
            ? provider
            : throw new InvalidOperationException($"Provider '{providerName}' is not registered.");
}
