using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Providers.Resilience;

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
    private const string ResilienceSectionName = $"{ProviderRootSectionName}:Resilience";

    /// <summary>
    /// Adds the default provider layer services and binds provider options from configuration.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <param name="configureCatalog">Optional provider catalog configuration.</param>
    /// <param name="configureAnthropic">Optional Anthropic provider configuration.</param>
    /// <param name="configureOpenAiCompatible">Optional OpenAI-compatible provider configuration.</param>
    /// <param name="configureResilience">Optional provider resilience configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawProviders(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ProviderCatalogOptions>? configureCatalog = null,
        Action<AnthropicProviderOptions>? configureAnthropic = null,
        Action<OpenAiCompatibleProviderOptions>? configureOpenAiCompatible = null,
        Action<ProviderResilienceOptions>? configureResilience = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ProviderCatalogOptions>()
            .Bind(configuration.GetSection(CatalogSectionName));
        services.AddOptions<AnthropicProviderOptions>()
            .Bind(configuration.GetSection(AnthropicSectionName));
        services.AddOptions<OpenAiCompatibleProviderOptions>()
            .Bind(configuration.GetSection(OpenAiCompatibleSectionName));
        services.AddOptions<ProviderResilienceOptions>()
            .Bind(configuration.GetSection(ResilienceSectionName));

        return AddSharpClawProvidersCore(services, configureCatalog, configureAnthropic, configureOpenAiCompatible, configureResilience);
    }

    /// <summary>
    /// Adds the default provider layer services.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configureCatalog">Optional provider catalog configuration.</param>
    /// <param name="configureAnthropic">Optional Anthropic provider configuration.</param>
    /// <param name="configureOpenAiCompatible">Optional OpenAI-compatible provider configuration.</param>
    /// <param name="configureResilience">Optional provider resilience configuration.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawProviders(
        this IServiceCollection services,
        Action<ProviderCatalogOptions>? configureCatalog = null,
        Action<AnthropicProviderOptions>? configureAnthropic = null,
        Action<OpenAiCompatibleProviderOptions>? configureOpenAiCompatible = null,
        Action<ProviderResilienceOptions>? configureResilience = null)
        => AddSharpClawProvidersCore(services, configureCatalog, configureAnthropic, configureOpenAiCompatible, configureResilience);

    private static IServiceCollection AddSharpClawProvidersCore(
        IServiceCollection services,
        Action<ProviderCatalogOptions>? configureCatalog,
        Action<AnthropicProviderOptions>? configureAnthropic,
        Action<OpenAiCompatibleProviderOptions>? configureOpenAiCompatible,
        Action<ProviderResilienceOptions>? configureResilience)
    {
        services.AddOptions<ProviderCatalogOptions>();
        services.AddOptions<AnthropicProviderOptions>();
        services.AddOptions<OpenAiCompatibleProviderOptions>();
        services.AddOptions<ProviderResilienceOptions>();

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

        if (configureResilience is not null)
        {
            services.Configure(configureResilience);
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
        services.AddSingleton<IProviderCatalogService, ProviderCatalogService>();

        services.AddSingleton<IModelProvider>(serviceProvider =>
            WrapWithResilience(serviceProvider, serviceProvider.GetRequiredService<AnthropicProvider>()));
        services.AddSingleton<IModelProvider>(serviceProvider =>
            WrapWithResilience(serviceProvider, serviceProvider.GetRequiredService<OpenAiCompatibleProvider>()));

        return services;
    }

    private static IModelProvider WrapWithResilience(IServiceProvider serviceProvider, IModelProvider inner)
    {
        var options = serviceProvider.GetRequiredService<IOptions<ProviderResilienceOptions>>().Value;
        if (!options.Enabled)
        {
            return inner;
        }

        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger<ResilientProviderDecorator>();
        return new ResilientProviderDecorator(inner, options, logger);
    }
}
