using Microsoft.Extensions.Options;

namespace SharpClaw.Code.Providers.Configuration;

/// <summary>
/// Validates <see cref="ProviderCatalogOptions"/> after configuration binding.
/// </summary>
public sealed class ProviderCatalogOptionsValidator : IValidateOptions<ProviderCatalogOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ProviderCatalogOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.DefaultProvider))
        {
            return ValidateOptionsResult.Fail("ProviderCatalogOptions.DefaultProvider must be set.");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Validates Anthropic provider options.
/// </summary>
public sealed class AnthropicProviderOptionsValidator : IValidateOptions<AnthropicProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, AnthropicProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProviderName))
        {
            return ValidateOptionsResult.Fail($"{nameof(AnthropicProviderOptions.ProviderName)} must be set.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            return ValidateOptionsResult.Fail($"{nameof(AnthropicProviderOptions.BaseUrl)} must be an absolute URL when set.");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultModel))
        {
            return ValidateOptionsResult.Fail($"{nameof(AnthropicProviderOptions.DefaultModel)} must be set.");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Validates OpenAI-compatible provider options.
/// </summary>
public sealed class OpenAiCompatibleProviderOptionsValidator : IValidateOptions<OpenAiCompatibleProviderOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpenAiCompatibleProviderOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ProviderName))
        {
            return ValidateOptionsResult.Fail($"{nameof(OpenAiCompatibleProviderOptions.ProviderName)} must be set.");
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl)
            && !Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            return ValidateOptionsResult.Fail($"{nameof(OpenAiCompatibleProviderOptions.BaseUrl)} must be an absolute URL when set.");
        }

        if (string.IsNullOrWhiteSpace(options.DefaultModel))
        {
            return ValidateOptionsResult.Fail($"{nameof(OpenAiCompatibleProviderOptions.DefaultModel)} must be set.");
        }

        foreach (var runtime in options.LocalRuntimes)
        {
            if (string.IsNullOrWhiteSpace(runtime.Key))
            {
                return ValidateOptionsResult.Fail("Local runtime profile names must be non-empty.");
            }

            if (!string.IsNullOrWhiteSpace(runtime.Value.BaseUrl)
                && !Uri.TryCreate(runtime.Value.BaseUrl, UriKind.Absolute, out _))
            {
                return ValidateOptionsResult.Fail($"Local runtime '{runtime.Key}' must define an absolute BaseUrl.");
            }

            if (string.IsNullOrWhiteSpace(runtime.Value.DefaultChatModel))
            {
                return ValidateOptionsResult.Fail($"Local runtime '{runtime.Key}' must define a DefaultChatModel.");
            }
        }

        return ValidateOptionsResult.Success;
    }
}
