using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Protocol.Operational;

namespace SharpClaw.Code.Runtime.Diagnostics.Checks;

/// <summary>
/// Reports authentication status for each registered model provider.
/// </summary>
public sealed class ProviderAuthenticationCheck(IEnumerable<IModelProvider> modelProviders) : IOperationalCheck
{
    private readonly IModelProvider[] providers = modelProviders.ToArray();

    /// <inheritdoc />
    public string Id => "provider.auth";

    /// <inheritdoc />
    public async Task<OperationalCheckItem> ExecuteAsync(OperationalDiagnosticsContext context, CancellationToken cancellationToken)
    {
        _ = context;
        if (providers.Length == 0)
        {
            return new OperationalCheckItem(
                Id,
                OperationalCheckStatus.Warn,
                "No model providers are registered.",
                null);
        }

        var lines = new List<string>();
        OperationalCheckStatus worst = OperationalCheckStatus.Ok;
        foreach (var provider in providers.OrderBy(p => p.ProviderName, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var auth = await provider.GetAuthStatusAsync(cancellationToken).ConfigureAwait(false);
                lines.Add($"{provider.ProviderName}: {(auth.IsAuthenticated ? "authenticated" : "not authenticated")}");
                if (!auth.IsAuthenticated)
                {
                    worst = OperationalCheckStatus.Warn;
                }
            }
            catch (Exception exception)
            {
                lines.Add($"{provider.ProviderName}: error ({exception.Message})");
                worst = OperationalCheckStatus.Error;
            }
        }

        return new OperationalCheckItem(
            Id,
            worst,
            "Provider authentication probe complete.",
            string.Join("; ", lines));
    }
}
