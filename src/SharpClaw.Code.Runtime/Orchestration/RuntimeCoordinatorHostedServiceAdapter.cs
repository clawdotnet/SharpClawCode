using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharpClaw.Code.Runtime.Orchestration;

/// <summary>
/// Adapts the placeholder runtime lifecycle into a hosted service registration.
/// </summary>
internal sealed class RuntimeCoordinatorHostedServiceAdapter(ILogger<RuntimeCoordinatorHostedServiceAdapter> logger) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("SharpClaw runtime hosted service starting.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("SharpClaw runtime hosted service stopping.");
        return Task.CompletedTask;
    }
}
