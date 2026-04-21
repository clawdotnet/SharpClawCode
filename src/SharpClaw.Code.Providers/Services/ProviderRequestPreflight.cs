using Microsoft.Extensions.Options;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers;

/// <summary>
/// Resolves provider and model aliases into normalized provider requests.
/// </summary>
public sealed class ProviderRequestPreflight(
    IOptions<ProviderCatalogOptions> options,
    IOptions<AnthropicProviderOptions>? anthropicOptions = null,
    IOptions<OpenAiCompatibleProviderOptions>? openAiCompatibleOptions = null) : IProviderRequestPreflight
{
    private readonly AnthropicProviderOptions anthropicOptions = anthropicOptions?.Value ?? new AnthropicProviderOptions();
    private readonly OpenAiCompatibleProviderOptions openAiCompatibleOptions = openAiCompatibleOptions?.Value ?? new OpenAiCompatibleProviderOptions();

    /// <inheritdoc />
    public ProviderRequest Prepare(ProviderRequest request)
    {
        var providerName = request.ProviderName?.Trim() ?? string.Empty;
        var model = request.Model?.Trim() ?? string.Empty;
        var catalog = options.Value;

        if (catalog.ModelAliases.TryGetValue(model, out var alias))
        {
            providerName = string.IsNullOrWhiteSpace(providerName) ? alias.ProviderName : providerName;
            model = alias.ModelId;
        }
        else if (TryResolveLocalRuntimeQualifiedModel(model, out var runtimeProfile, out var runtimeModel))
        {
            providerName = openAiCompatibleOptions.ProviderName;
            model = runtimeModel;
        }
        else if (TryParseQualifiedModel(model, out var parsedProviderName, out var parsedModel))
        {
            providerName = string.IsNullOrWhiteSpace(providerName) ? parsedProviderName : providerName;
            model = parsedModel;
        }
        else if (string.IsNullOrWhiteSpace(providerName))
        {
            providerName = catalog.DefaultProvider;
        }

        if (Internal.ProviderHttpHelpers.IsDefaultModelAlias(model) || string.IsNullOrWhiteSpace(model))
        {
            model = ResolveDefaultModel(providerName);
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException(
                $"No default model configured for provider '{providerName}'. Specify a model explicitly.");
        }

        var metadata = request.Metadata is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(request.Metadata, StringComparer.Ordinal);
        if (TryResolveLocalRuntimeQualifiedModel(request.Model?.Trim() ?? string.Empty, out var selectedRuntimeProfile, out _))
        {
            metadata[OpenAiCompatibleProvider.RuntimeProfileMetadataKey] = selectedRuntimeProfile;
        }

        return request with
        {
            ProviderName = providerName,
            Model = model,
            Metadata = metadata,
        };
    }

    private string ResolveDefaultModel(string providerName)
    {
        if (providerName.Equals(anthropicOptions.ProviderName, StringComparison.OrdinalIgnoreCase)
            || providerName.Equals("anthropic", StringComparison.OrdinalIgnoreCase))
        {
            return anthropicOptions.DefaultModel;
        }

        if (providerName.Equals(openAiCompatibleOptions.ProviderName, StringComparison.OrdinalIgnoreCase)
            || providerName.Equals("openai-compatible", StringComparison.OrdinalIgnoreCase))
        {
            return openAiCompatibleOptions.DefaultModel;
        }

        return string.Empty;
    }

    private static bool TryParseQualifiedModel(string model, out string providerName, out string providerModel)
    {
        var separatorIndex = model.IndexOfAny(['/', ':']);
        if (separatorIndex <= 0 || separatorIndex == model.Length - 1)
        {
            providerName = string.Empty;
            providerModel = string.Empty;
            return false;
        }

        providerName = model[..separatorIndex];
        providerModel = model[(separatorIndex + 1)..];
        return true;
    }

    private bool TryResolveLocalRuntimeQualifiedModel(string model, out string runtimeProfile, out string runtimeModel)
    {
        runtimeProfile = string.Empty;
        runtimeModel = string.Empty;
        if (!TryParseQualifiedModel(model, out var providerSegment, out var providerModel))
        {
            return false;
        }

        if (!openAiCompatibleOptions.LocalRuntimes.ContainsKey(providerSegment))
        {
            return false;
        }

        runtimeProfile = providerSegment;
        runtimeModel = providerModel;
        return true;
    }
}
