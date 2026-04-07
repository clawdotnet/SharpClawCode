using Microsoft.Extensions.Options;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers;

/// <summary>
/// Resolves provider and model aliases into normalized provider requests.
/// </summary>
public sealed class ProviderRequestPreflight(IOptions<ProviderCatalogOptions> options) : IProviderRequestPreflight
{
    /// <inheritdoc />
    public ProviderRequest Prepare(ProviderRequest request)
    {
        var providerName = request.ProviderName?.Trim() ?? string.Empty;
        var model = request.Model.Trim();
        var catalog = options.Value;

        if (catalog.ModelAliases.TryGetValue(model, out var alias))
        {
            providerName = string.IsNullOrWhiteSpace(providerName) ? alias.ProviderName : providerName;
            model = alias.ModelId;
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

        return request with
        {
            ProviderName = providerName,
            Model = model,
        };
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
}
