using FluentAssertions;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Providers;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.UnitTests.Providers;

/// <summary>
/// Verifies alias resolution for provider preflight. Streaming behavior is covered by <see cref="ProviderStreamAdapterTests"/>.
/// </summary>
public sealed class ProviderAliasAndRequestTests
{
    /// <summary>
    /// Ensures model aliases resolve to a configured provider name and concrete model id.
    /// </summary>
    [Fact]
    public void Preflight_should_resolve_model_alias_to_provider_and_model()
    {
        var catalogOptions = Options.Create(new ProviderCatalogOptions
        {
            DefaultProvider = "anthropic",
            ModelAliases =
            {
                ["sonnet"] = new ModelAliasDefinition("anthropic", "claude-3-7-sonnet-latest"),
                ["fast"] = new ModelAliasDefinition("openai-compatible", "gpt-4.1-mini")
            }
        });

        var preflight = new ProviderRequestPreflight(catalogOptions);
        var request = new ProviderRequest(
            Id: "req-001",
            SessionId: "session-001",
            TurnId: "turn-001",
            ProviderName: string.Empty,
            Model: "sonnet",
            Prompt: "Explain the repo layout.",
            SystemPrompt: "Be concise.",
            OutputFormat: OutputFormat.Text,
            Temperature: 0.2m,
            Metadata: null);

        var resolved = preflight.Prepare(request);

        resolved.ProviderName.Should().Be("anthropic");
        resolved.Model.Should().Be("claude-3-7-sonnet-latest");
    }

    /// <summary>
    /// Ensures the runtime default-model sentinel resolves to the configured provider default model even without an alias entry.
    /// </summary>
    [Fact]
    public void Preflight_should_resolve_default_model_sentinel_to_provider_default_model()
    {
        var catalogOptions = Options.Create(new ProviderCatalogOptions
        {
            DefaultProvider = "openai-compatible",
        });
        var anthropic = Options.Create(new AnthropicProviderOptions { DefaultModel = "claude-default" });
        var openAi = Options.Create(new OpenAiCompatibleProviderOptions { DefaultModel = "gpt-default" });

        var preflight = new ProviderRequestPreflight(catalogOptions, anthropic, openAi);
        var request = new ProviderRequest(
            Id: "req-002",
            SessionId: "session-001",
            TurnId: "turn-001",
            ProviderName: string.Empty,
            Model: "default",
            Prompt: "Explain the repo layout.",
            SystemPrompt: "Be concise.",
            OutputFormat: OutputFormat.Text,
            Temperature: 0.2m,
            Metadata: null);

        var resolved = preflight.Prepare(request);

        resolved.ProviderName.Should().Be("openai-compatible");
        resolved.Model.Should().Be("gpt-default");
    }

}
