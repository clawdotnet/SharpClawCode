using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;

namespace SharpClaw.Code.Providers;

/// <summary>
/// Registers provider clients, configuration, and resolution services.
/// </summary>
public static class ProvidersServiceCollectionExtensions
{
    private const string ProviderRootSectionName = "SharpClaw:Providers";
    private const string CatalogSectionName = $"{ProviderRootSectionName}:Catalog";
    private const string AnthropicSectionName = $"{ProviderRootSectionName}:Anthropic";
    private const string OpenAiCompatibleSectionName = $"{ProviderRootSectionName}:OpenAiCompatible";

    /// <summary>
    /// Adds the default provider layer services and binds provider options from configuration.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <param name="configureCatalog">Optional provider catalog configuration.</param>
    /// <param name="configureAnthropic">Optional Anthropic provider configuration.</param>
    /// <param name="configureOpenAiCompatible">Optional OpenAI-compatible provider configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawProviders(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ProviderCatalogOptions>? configureCatalog = null,
        Action<AnthropicProviderOptions>? configureAnthropic = null,
        Action<OpenAiCompatibleProviderOptions>? configureOpenAiCompatible = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ProviderCatalogOptions>()
            .Bind(configuration.GetSection(CatalogSectionName));
        services.AddOptions<AnthropicProviderOptions>()
            .Bind(configuration.GetSection(AnthropicSectionName));
        services.AddOptions<OpenAiCompatibleProviderOptions>()
            .Bind(configuration.GetSection(OpenAiCompatibleSectionName));

        return AddSharpClawProvidersCore(services, configureCatalog, configureAnthropic, configureOpenAiCompatible);
    }

    /// <summary>
    /// Adds the default provider layer services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configureCatalog">Optional provider catalog configuration.</param>
    /// <param name="configureAnthropic">Optional Anthropic provider configuration.</param>
    /// <param name="configureOpenAiCompatible">Optional OpenAI-compatible provider configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawProviders(
        this IServiceCollection services,
        Action<ProviderCatalogOptions>? configureCatalog = null,
        Action<AnthropicProviderOptions>? configureAnthropic = null,
        Action<OpenAiCompatibleProviderOptions>? configureOpenAiCompatible = null)
        => AddSharpClawProvidersCore(services, configureCatalog, configureAnthropic, configureOpenAiCompatible);

    private static IServiceCollection AddSharpClawProvidersCore(
        IServiceCollection services,
        Action<ProviderCatalogOptions>? configureCatalog,
        Action<AnthropicProviderOptions>? configureAnthropic,
        Action<OpenAiCompatibleProviderOptions>? configureOpenAiCompatible)
    {
        services.AddOptions<ProviderCatalogOptions>();
        services.AddOptions<AnthropicProviderOptions>();
        services.AddOptions<OpenAiCompatibleProviderOptions>();

        if (configureCatalog is not null)
        {
            services.Configure(configureCatalog);
        }

        if (configureAnthropic is not null)
        {
            services.Configure(configureAnthropic);
        }

        if (configureOpenAiCompatible is not null)
        {
            services.Configure(configureOpenAiCompatible);
        }

        services.AddSingleton<IValidateOptions<ProviderCatalogOptions>, ProviderCatalogOptionsValidator>();
        services.AddSingleton<IValidateOptions<AnthropicProviderOptions>, AnthropicProviderOptionsValidator>();
        services.AddSingleton<IValidateOptions<OpenAiCompatibleProviderOptions>, OpenAiCompatibleProviderOptionsValidator>();
        services.AddOptions<ProviderCatalogOptions>().ValidateOnStart();
        services.AddOptions<AnthropicProviderOptions>().ValidateOnStart();
        services.AddOptions<OpenAiCompatibleProviderOptions>().ValidateOnStart();

        services.AddSingleton<AnthropicProvider>();
        services.AddSingleton<OpenAiCompatibleProvider>();

        services.AddSingleton<IProviderRequestPreflight, ProviderRequestPreflight>();
        services.AddSingleton<IModelProviderResolver, ModelProviderResolver>();
        services.AddSingleton<IAuthFlowService, AuthFlowService>();
        services.AddSingleton<IModelProvider>(serviceProvider => serviceProvider.GetRequiredService<AnthropicProvider>());
        services.AddSingleton<IModelProvider>(serviceProvider => serviceProvider.GetRequiredService<OpenAiCompatibleProvider>());

        return services;
    }
}
