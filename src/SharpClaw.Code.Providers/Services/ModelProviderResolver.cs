using Microsoft.Extensions.Options;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;

namespace SharpClaw.Code.Providers;

/// <summary>
/// Resolves configured model providers by name.
/// </summary>
public sealed class ModelProviderResolver(
    IEnumerable<IModelProvider> providers,
    IOptions<ProviderCatalogOptions> catalogOptions) : IModelProviderResolver
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

    /// <inheritdoc />
    public IReadOnlyList<IModelProvider> ResolveCandidates(string providerName)
    {
        var candidates = new List<IModelProvider> { Resolve(providerName) };
        foreach (var fallbackName in catalogOptions.Value.FallbackProviders)
        {
            if (candidates.Any(candidate => string.Equals(candidate.ProviderName, fallbackName, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            if (_providers.TryGetValue(fallbackName, out var fallback))
            {
                candidates.Add(fallback);
            }
        }

        return candidates;
    }
}
