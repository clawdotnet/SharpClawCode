using Microsoft.Extensions.Hosting;
using SharpClaw.Code;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(_ => new SharpClawRuntimeHostBuilder(args).Build());
builder.Services.AddHostedService<EmbeddedRuntimeLifecycleService>();

var app = builder.Build();

app.MapPost("/chat", async (ChatRequest body, SharpClawRuntimeHost runtimeHost, CancellationToken ct) =>
{
    var workspacePath = Directory.GetCurrentDirectory();
    var hostContext = new RuntimeHostContext(
        HostId: "web-api-agent",
        TenantId: body.TenantId,
        IsEmbeddedHost: true);

    var sessionId = body.SessionId;
    if (string.IsNullOrWhiteSpace(sessionId))
    {
        var session = await runtimeHost.CreateSessionAsync(
            workspacePath,
            PermissionMode.ReadOnly,
            OutputFormat.Text,
            hostContext,
            ct);
        sessionId = session.Id;
    }

    var result = await runtimeHost.ExecutePromptAsync(
        body.Prompt,
        workspacePath,
        model: "default",
        permissionMode: PermissionMode.ReadOnly,
        outputFormat: OutputFormat.Text,
        sessionId: sessionId,
        hostContext: hostContext,
        cancellationToken: ct);

    return Results.Ok(new ChatResponse(result.FinalOutput ?? string.Empty, sessionId!));
});

app.Run();

sealed class EmbeddedRuntimeLifecycleService(SharpClawRuntimeHost runtimeHost) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
        => runtimeHost.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => runtimeHost.StopAsync(cancellationToken);
}

record ChatRequest(string Prompt, string? SessionId, string? TenantId = null);

record ChatResponse(string Output, string SessionId);
