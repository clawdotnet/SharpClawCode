using System.Text.Json;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Providers;

/// <summary>
/// Builds the surfaced provider and local runtime catalog, including health probes and discovered models.
/// </summary>
public sealed class ProviderCatalogService(
    IEnumerable<IModelProvider> modelProviders,
    IAuthFlowService authFlowService,
    IOptions<ProviderCatalogOptions> catalogOptions,
    IOptions<AnthropicProviderOptions> anthropicOptions,
    IOptions<OpenAiCompatibleProviderOptions> openAiOptions) : IProviderCatalogService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<ProviderModelCatalogEntry>> ListAsync(CancellationToken cancellationToken)
    {
        var aliasesByProvider = catalogOptions.Value.ModelAliases
            .GroupBy(static pair => pair.Value.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(pair => pair.Key).OrderBy(static alias => alias, StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);

        var results = new List<ProviderModelCatalogEntry>();
        foreach (var provider in modelProviders.OrderBy(static provider => provider.ProviderName, StringComparer.OrdinalIgnoreCase))
        {
            var auth = await authFlowService.GetStatusAsync(provider.ProviderName, cancellationToken).ConfigureAwait(false);
            var defaultModel = ResolveDefaultModel(provider.ProviderName);
            var supportsToolCalls = !string.Equals(provider.ProviderName, anthropicOptions.Value.ProviderName, StringComparison.OrdinalIgnoreCase)
                ? openAiOptions.Value.SupportsToolCalls
                : true;
            var supportsEmbeddings = string.Equals(provider.ProviderName, openAiOptions.Value.ProviderName, StringComparison.OrdinalIgnoreCase)
                && (openAiOptions.Value.SupportsEmbeddings || !string.IsNullOrWhiteSpace(openAiOptions.Value.DefaultEmbeddingModel));
            var localProfiles = string.Equals(provider.ProviderName, openAiOptions.Value.ProviderName, StringComparison.OrdinalIgnoreCase)
                ? await BuildLocalRuntimeProfilesAsync(cancellationToken).ConfigureAwait(false)
                : [];
            var availableModels = localProfiles
                .SelectMany(static profile => profile.AvailableModels)
                .GroupBy(static model => model.Id, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .ToArray();

            results.Add(new ProviderModelCatalogEntry(
                ProviderName: provider.ProviderName,
                DefaultModel: defaultModel,
                Aliases: aliasesByProvider.TryGetValue(provider.ProviderName, out var aliases) ? aliases : [],
                AuthStatus: auth,
                SupportsToolCalls: supportsToolCalls,
                SupportsEmbeddings: supportsEmbeddings,
                AvailableModels: availableModels,
                LocalRuntimeProfiles: localProfiles));
        }

        return results;
    }

    private async Task<LocalRuntimeProfileSummary[]> BuildLocalRuntimeProfilesAsync(CancellationToken cancellationToken)
    {
        if (openAiOptions.Value.LocalRuntimes.Count == 0)
        {
            return [];
        }

        var results = new List<LocalRuntimeProfileSummary>();
        foreach (var pair in openAiOptions.Value.LocalRuntimes.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            var (isHealthy, detail, models) = await ProbeRuntimeAsync(pair.Value, cancellationToken).ConfigureAwait(false);
            results.Add(new LocalRuntimeProfileSummary(
                Name: pair.Key,
                Kind: pair.Value.Kind,
                BaseUrl: pair.Value.BaseUrl,
                DefaultChatModel: pair.Value.DefaultChatModel,
                DefaultEmbeddingModel: pair.Value.DefaultEmbeddingModel,
                AuthMode: pair.Value.AuthMode,
                IsHealthy: isHealthy,
                HealthDetail: detail,
                AvailableModels: models));
        }

        return results.ToArray();
    }

    private async Task<(bool IsHealthy, string? Detail, ProviderDiscoveredModel[] Models)> ProbeRuntimeAsync(
        LocalRuntimeProfileOptions profile,
        CancellationToken cancellationToken)
    {
        var routes = profile.Kind == LocalRuntimeKind.Ollama
            ? new[] { "models", CreateAbsoluteRoute(profile.BaseUrl, "api/tags") }
            : new[] { "models" };

        try
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri(Internal.ProviderHttpHelpers.NormalizeBaseUrl(profile.BaseUrl) ?? profile.BaseUrl),
                Timeout = TimeSpan.FromSeconds(5),
            };

            if (!string.IsNullOrWhiteSpace(profile.ApiKey))
            {
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", profile.ApiKey);
            }

            foreach (var route in routes)
            {
                using var response = await httpClient.GetAsync(route, cancellationToken).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
                var models = ParseModels(document.RootElement, profile);
                return (true, $"{models.Length} model(s) discovered.", models);
            }

            return (false, "Runtime probe failed.", []);
        }
        catch (Exception exception)
        {
            return (false, exception.Message, []);
        }
    }

    private static ProviderDiscoveredModel[] ParseModels(JsonElement root, LocalRuntimeProfileOptions profile)
    {
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            return data.EnumerateArray()
                .Select(element => BuildModel(element.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "unknown" : "unknown", profile))
                .ToArray();
        }

        if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            return models.EnumerateArray()
                .Select(element =>
                {
                    var id = element.TryGetProperty("name", out var nameProp)
                        ? nameProp.GetString() ?? "unknown"
                        : element.TryGetProperty("model", out var modelProp)
                            ? modelProp.GetString() ?? "unknown"
                            : "unknown";
                    return BuildModel(id, profile);
                })
                .ToArray();
        }

        return [];
    }

    private static ProviderDiscoveredModel BuildModel(string id, LocalRuntimeProfileOptions profile)
        => new(
            Id: id,
            DisplayName: id,
            SupportsTools: profile.SupportsToolCalls,
            SupportsEmbeddings: profile.SupportsEmbeddings
                || string.Equals(id, profile.DefaultEmbeddingModel, StringComparison.OrdinalIgnoreCase));

    private static string CreateAbsoluteRoute(string baseUrl, string route)
    {
        var uri = new Uri(baseUrl, UriKind.Absolute);
        var builder = new UriBuilder(uri)
        {
            Path = route.TrimStart('/')
        };
        return builder.Uri.ToString();
    }

    private string ResolveDefaultModel(string providerName)
        => string.Equals(providerName, anthropicOptions.Value.ProviderName, StringComparison.OrdinalIgnoreCase)
            ? anthropicOptions.Value.DefaultModel
            : string.Equals(providerName, openAiOptions.Value.ProviderName, StringComparison.OrdinalIgnoreCase)
                ? openAiOptions.Value.DefaultModel
                : "default";
}
