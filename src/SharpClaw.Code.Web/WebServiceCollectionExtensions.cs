using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Web.Abstractions;
using SharpClaw.Code.Web.Configuration;
using SharpClaw.Code.Web.Services;

namespace SharpClaw.Code.Web;

/// <summary>
/// Registers SharpClaw web services and their HTTP dependencies.
/// </summary>
public static class WebServiceCollectionExtensions
{
    private const string WebSectionName = "SharpClaw:Web";

    /// <summary>
    /// Adds SharpClaw web services with configuration binding.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <param name="configuration">The application configuration root.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawWeb(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<WebSearchOptions>()
            .Bind(configuration.GetSection(WebSectionName));
        return AddSharpClawWebCore(services);
    }

    /// <summary>
    /// Adds SharpClaw web services to the service collection.
    /// </summary>
    /// <param name="services">The service collection to update.</param>
    /// <returns>The updated service collection.</returns>
    public static IServiceCollection AddSharpClawWeb(this IServiceCollection services)
    {
        services.AddOptions<WebSearchOptions>();
        return AddSharpClawWebCore(services);
    }

    private static IServiceCollection AddSharpClawWebCore(IServiceCollection services)
    {
        services.AddSingleton<IValidateOptions<WebSearchOptions>, WebSearchOptionsValidator>();
        services.AddHttpClient<IWebSearchService, WebSearchService>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(60));
        services.AddHttpClient<IWebFetchService, WebFetchService>()
            .ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(60));
        return services;
    }
}
