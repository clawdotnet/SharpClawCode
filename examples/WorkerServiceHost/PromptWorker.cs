using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SharpClaw.Code;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace WorkerServiceHost;

sealed class PromptWorker(
    IConfiguration configuration,
    ILogger<PromptWorker> logger,
    SharpClawRuntimeHost runtimeHost,
    IHostApplicationLifetime hostApplicationLifetime) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuredWorkspacePath = configuration["Worker:WorkspacePath"];
        var workspacePath = string.IsNullOrWhiteSpace(configuredWorkspacePath)
            ? Directory.GetCurrentDirectory()
            : configuredWorkspacePath;
        var prompt = configuration["Worker:Prompt"] ?? "Summarize the current workspace and highlight any obvious risks.";
        var configuredModel = configuration["Worker:Model"];
        var model = string.IsNullOrWhiteSpace(configuredModel) ? "default" : configuredModel;
        var tenantId = string.IsNullOrWhiteSpace(configuration["Worker:TenantId"])
            ? null
            : configuration["Worker:TenantId"];
        var hostContext = new RuntimeHostContext(
            HostId: "worker-service-host",
            TenantId: tenantId,
            IsEmbeddedHost: true);

        try
        {
            var session = await runtimeHost.CreateSessionAsync(
                workspacePath,
                PermissionMode.ReadOnly,
                OutputFormat.Text,
                hostContext,
                stoppingToken);

            var result = await runtimeHost.ExecutePromptAsync(
                prompt,
                workspacePath,
                model,
                PermissionMode.ReadOnly,
                OutputFormat.Text,
                sessionId: session.Id,
                hostContext: hostContext,
                cancellationToken: stoppingToken);

            logger.LogInformation("Worker prompt completed for session {SessionId}.", session.Id);
            logger.LogInformation("{Output}", result.FinalOutput ?? "(no output)");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Worker prompt execution failed.");
        }
        finally
        {
            hostApplicationLifetime.StopApplication();
        }
    }
}
