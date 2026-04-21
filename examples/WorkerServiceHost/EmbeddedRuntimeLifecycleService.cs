using Microsoft.Extensions.Hosting;
using SharpClaw.Code;

namespace WorkerServiceHost;

sealed class EmbeddedRuntimeLifecycleService(SharpClawRuntimeHost runtimeHost) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => runtimeHost.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => runtimeHost.StopAsync(cancellationToken);
}
