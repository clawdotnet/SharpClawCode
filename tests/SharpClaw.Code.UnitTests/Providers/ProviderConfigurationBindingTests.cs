using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Infrastructure;
using SharpClaw.Code.Providers;
using SharpClaw.Code.Providers.Configuration;

namespace SharpClaw.Code.UnitTests.Providers;

/// <summary>
/// Verifies provider options bind from configuration.
/// </summary>
public sealed class ProviderConfigurationBindingTests
{
    /// <summary>
    /// Ensures provider options and aliases bind from configuration sections.
    /// </summary>
    [Fact]
    public void AddSharpClawProviders_should_bind_options_from_configuration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SharpClaw:Providers:Catalog:DefaultProvider"] = "anthropic",
                ["SharpClaw:Providers:Catalog:ModelAliases:sonnet:ProviderName"] = "anthropic",
                ["SharpClaw:Providers:Catalog:ModelAliases:sonnet:ModelId"] = "claude-3-7-sonnet-latest",
                ["SharpClaw:Providers:Anthropic:ApiKey"] = "anthropic-key",
                ["SharpClaw:Providers:Anthropic:BaseUrl"] = "https://anthropic.example.com",
                ["SharpClaw:Providers:OpenAiCompatible:ApiKey"] = "openai-key",
                ["SharpClaw:Providers:OpenAiCompatible:BaseUrl"] = "https://openai.example.com/v1"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSharpClawInfrastructure();
        services.AddSharpClawProviders(configuration);
        using var serviceProvider = services.BuildServiceProvider();

        var catalog = serviceProvider.GetRequiredService<IOptions<ProviderCatalogOptions>>().Value;
        var anthropic = serviceProvider.GetRequiredService<IOptions<AnthropicProviderOptions>>().Value;
        var openAi = serviceProvider.GetRequiredService<IOptions<OpenAiCompatibleProviderOptions>>().Value;

        catalog.DefaultProvider.Should().Be("anthropic");
        catalog.ModelAliases["sonnet"].Should().Be(new ModelAliasDefinition("anthropic", "claude-3-7-sonnet-latest"));
        anthropic.ApiKey.Should().Be("anthropic-key");
        anthropic.BaseUrl.Should().Be("https://anthropic.example.com");
        openAi.ApiKey.Should().Be("openai-key");
        openAi.BaseUrl.Should().Be("https://openai.example.com/v1");
    }
}
