using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Infrastructure;
using SharpClaw.Code.Providers;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Protocol.Models;

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
                ["SharpClaw:Providers:OpenAiCompatible:BaseUrl"] = "https://openai.example.com/v1",
                ["SharpClaw:Providers:OpenAiCompatible:AuthMode"] = "Optional",
                ["SharpClaw:Providers:OpenAiCompatible:DefaultEmbeddingModel"] = "text-embedding-3-small",
                ["SharpClaw:Providers:OpenAiCompatible:SupportsEmbeddings"] = "true",
                ["SharpClaw:Providers:OpenAiCompatible:LocalRuntimes:ollama:Kind"] = "Ollama",
                ["SharpClaw:Providers:OpenAiCompatible:LocalRuntimes:ollama:BaseUrl"] = "http://127.0.0.1:11434/v1/",
                ["SharpClaw:Providers:OpenAiCompatible:LocalRuntimes:ollama:DefaultChatModel"] = "qwen2.5-coder",
                ["SharpClaw:Providers:OpenAiCompatible:LocalRuntimes:ollama:DefaultEmbeddingModel"] = "nomic-embed-text",
                ["SharpClaw:Providers:OpenAiCompatible:LocalRuntimes:ollama:AuthMode"] = "Optional",
                ["SharpClaw:Providers:OpenAiCompatible:LocalRuntimes:ollama:SupportsToolCalls"] = "true",
                ["SharpClaw:Providers:OpenAiCompatible:LocalRuntimes:ollama:SupportsEmbeddings"] = "true"
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
        openAi.AuthMode.Should().Be(ProviderAuthMode.Optional);
        openAi.DefaultEmbeddingModel.Should().Be("text-embedding-3-small");
        openAi.SupportsEmbeddings.Should().BeTrue();
        openAi.LocalRuntimes.Should().ContainKey("ollama");
        openAi.LocalRuntimes["ollama"].Kind.Should().Be(LocalRuntimeKind.Ollama);
        openAi.LocalRuntimes["ollama"].DefaultChatModel.Should().Be("qwen2.5-coder");
        openAi.LocalRuntimes["ollama"].DefaultEmbeddingModel.Should().Be("nomic-embed-text");
    }
}
