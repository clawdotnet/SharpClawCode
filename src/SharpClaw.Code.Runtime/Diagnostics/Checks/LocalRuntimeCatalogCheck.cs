using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Protocol.Operational;

namespace SharpClaw.Code.Runtime.Diagnostics.Checks;

/// <summary>
/// Reports local runtime profile health, discovered model counts, and embedding availability.
/// </summary>
public sealed class LocalRuntimeCatalogCheck(IProviderCatalogService providerCatalogService) : IOperationalCheck
{
    /// <inheritdoc />
    public string Id => "provider.local-runtimes";

    /// <inheritdoc />
    public async Task<OperationalCheckItem> ExecuteAsync(OperationalDiagnosticsContext context, CancellationToken cancellationToken)
    {
        _ = context;

        var profiles = (await providerCatalogService.ListAsync(cancellationToken).ConfigureAwait(false))
            .SelectMany(static entry => entry.LocalRuntimeProfiles ?? [])
            .ToArray();

        if (profiles.Length == 0)
        {
            return new OperationalCheckItem(
                Id,
                OperationalCheckStatus.Ok,
                "No local runtime profiles are configured.",
                null);
        }

        var status = profiles.All(static profile => profile.IsHealthy)
            ? OperationalCheckStatus.Ok
            : OperationalCheckStatus.Warn;
        var detail = string.Join(
            "; ",
            profiles.Select(profile =>
                $"{profile.Name} ({profile.Kind}): {(profile.IsHealthy ? "healthy" : "unhealthy")}, "
                + $"{profile.AvailableModels.Length} model(s), "
                + $"embedding default {(string.IsNullOrWhiteSpace(profile.DefaultEmbeddingModel) ? "not configured" : profile.DefaultEmbeddingModel)}"));

        return new OperationalCheckItem(
            Id,
            status,
            "Local runtime profile probe complete.",
            detail);
    }
}
