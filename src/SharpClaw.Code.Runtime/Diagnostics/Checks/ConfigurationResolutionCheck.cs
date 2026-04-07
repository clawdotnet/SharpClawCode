using Microsoft.Extensions.Configuration;
using SharpClaw.Code.Protocol.Operational;

namespace SharpClaw.Code.Runtime.Diagnostics.Checks;

/// <summary>
/// Summarizes presence of SharpClaw configuration keys (no secret values).
/// </summary>
public sealed class ConfigurationResolutionCheck(IConfiguration? configuration = null) : IOperationalCheck
{
    /// <inheritdoc />
    public string Id => "config.resolution";

    /// <inheritdoc />
    public Task<OperationalCheckItem> ExecuteAsync(OperationalDiagnosticsContext context, CancellationToken cancellationToken)
    {
        _ = context;
        _ = cancellationToken;
        if (configuration is null)
        {
            return Task.FromResult(new OperationalCheckItem(
                Id,
                OperationalCheckStatus.Skipped,
                "Host configuration is not available in this invocation.",
                null));
        }

        var keys = configuration.AsEnumerable()
            .Select(pair => pair.Key)
            .Where(key => key.StartsWith("SharpClaw", StringComparison.OrdinalIgnoreCase))
            .Take(25)
            .ToArray();

        var summary = keys.Length == 0
            ? "No SharpClaw:* configuration keys found."
            : $"{keys.Length} SharpClaw configuration key(s) resolved.";

        return Task.FromResult(new OperationalCheckItem(
            Id,
            OperationalCheckStatus.Ok,
            summary,
            keys.Length == 0 ? null : string.Join(", ", keys)));
    }
}
