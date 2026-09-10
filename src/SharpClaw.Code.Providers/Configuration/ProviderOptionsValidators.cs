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

        if (options.FallbackProviders.Any(string.IsNullOrWhiteSpace))
        {
            return ValidateOptionsResult.Fail("ProviderCatalogOptions.FallbackProviders cannot contain empty provider names.");
        }

        if (options.FallbackProviders.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.FallbackProviders.Count)
        {
            return ValidateOptionsResult.Fail("ProviderCatalogOptions.FallbackProviders cannot contain duplicate provider names.");
        }

        if (options.FallbackModels.Any(entry => string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value)))
        {
            return ValidateOptionsResult.Fail("ProviderCatalogOptions.FallbackModels must contain non-empty provider names and model ids.");
        }

        return ValidateOptionsResult.Success;
    }
}

/// <summary>
/// Validates provider resilience settings after configuration binding.
/// </summary>
public sealed class ProviderResilienceOptionsValidator : IValidateOptions<ProviderResilienceOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, ProviderResilienceOptions options)
    {
        if (options.MaxRetries < 0)
        {
            return ValidateOptionsResult.Fail("ProviderResilienceOptions.MaxRetries cannot be negative.");
        }

        if (options.RequestTimeout <= TimeSpan.Zero
            || options.InitialRetryDelay < TimeSpan.Zero
            || options.MaxRetryDelay < options.InitialRetryDelay
            || options.CircuitBreakerFailureThreshold <= 0
            || options.CircuitBreakerBreakDuration < TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail("Provider resilience durations and thresholds must be positive and internally consistent.");
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
