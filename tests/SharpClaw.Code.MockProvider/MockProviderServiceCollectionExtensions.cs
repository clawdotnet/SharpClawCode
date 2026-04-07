using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;

namespace SharpClaw.Code.MockProvider;

/// <summary>
/// Registers the deterministic mock model provider for test and parity hosts.
/// </summary>
public static class MockProviderServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="DeterministicMockModelProvider" /> and points the provider catalog default at it.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The updated collection.</returns>
    public static IServiceCollection AddDeterministicMockModelProvider(this IServiceCollection services)
    {
        services.AddSingleton<IModelProvider, DeterministicMockModelProvider>();
        services.PostConfigure<ProviderCatalogOptions>(options =>
        {
            options.DefaultProvider = DeterministicMockModelProvider.ProviderNameConstant;
            options.ModelAliases["default"] = new ModelAliasDefinition(
                DeterministicMockModelProvider.ProviderNameConstant,
                DeterministicMockModelProvider.DefaultModelId);
            options.ModelAliases["deterministic"] = new ModelAliasDefinition(
                DeterministicMockModelProvider.ProviderNameConstant,
                DeterministicMockModelProvider.DefaultModelId);
        });
        return services;
    }
}
