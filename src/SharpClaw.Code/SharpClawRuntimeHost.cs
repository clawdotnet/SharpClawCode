using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharpClaw.Code.Acp;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code;

/// <summary>
/// Wraps an embeddable SharpClaw runtime host and exposes typed runtime entry points.
/// </summary>
public sealed class SharpClawRuntimeHost : IAsyncDisposable
{
    private readonly IHost host;
    private readonly IConversationRuntime conversationRuntime;
    private readonly IRuntimeCommandService runtimeCommandService;
    private readonly IWorkspaceHttpServer workspaceHttpServer;
    private readonly AcpStdioHost acpStdioHost;
    private readonly IRuntimeHostContextAccessor hostContextAccessor;

    internal SharpClawRuntimeHost(IHost host)
    {
        this.host = host;
        conversationRuntime = host.Services.GetRequiredService<IConversationRuntime>();
        runtimeCommandService = host.Services.GetRequiredService<IRuntimeCommandService>();
        workspaceHttpServer = host.Services.GetRequiredService<IWorkspaceHttpServer>();
        acpStdioHost = host.Services.GetRequiredService<AcpStdioHost>();
        hostContextAccessor = host.Services.GetRequiredService<IRuntimeHostContextAccessor>();
    }

    /// <summary>
    /// Gets the root service provider for advanced host integrations.
    /// </summary>
    public IServiceProvider Services => host.Services;

    /// <summary>
    /// Starts hosted services registered with the embedded runtime.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
        => host.StartAsync(cancellationToken);

    /// <summary>
    /// Stops hosted services registered with the embedded runtime.
    /// </summary>
    public Task StopAsync(CancellationToken cancellationToken = default)
        => host.StopAsync(cancellationToken);

    /// <summary>
    /// Creates a durable session under the supplied host context.
    /// </summary>
    public Task<ConversationSession> CreateSessionAsync(
        string workspacePath,
        PermissionMode permissionMode,
        OutputFormat outputFormat,
        RuntimeHostContext? hostContext,
        CancellationToken cancellationToken = default)
        => ExecuteInHostContextAsync(hostContext, () => conversationRuntime.CreateSessionAsync(workspacePath, permissionMode, outputFormat, cancellationToken));

    /// <summary>
    /// Gets a session snapshot by identifier under the supplied host context.
    /// </summary>
    public Task<ConversationSession?> GetSessionAsync(
        string workspacePath,
        string sessionId,
        RuntimeHostContext? hostContext,
        CancellationToken cancellationToken = default)
        => ExecuteInHostContextAsync(hostContext, () => conversationRuntime.GetSessionAsync(workspacePath, sessionId, cancellationToken));

    /// <summary>
    /// Gets the latest session for a workspace under the supplied host context.
    /// </summary>
    public Task<ConversationSession?> GetLatestSessionAsync(
        string workspacePath,
        RuntimeHostContext? hostContext,
        CancellationToken cancellationToken = default)
        => ExecuteInHostContextAsync(hostContext, () => conversationRuntime.GetLatestSessionAsync(workspacePath, cancellationToken));

    /// <summary>
    /// Forks a session under the supplied host context.
    /// </summary>
    public Task<ConversationSession> ForkSessionAsync(
        string workspacePath,
        string? sourceSessionId,
        RuntimeHostContext? hostContext,
        CancellationToken cancellationToken = default)
        => ExecuteInHostContextAsync(hostContext, () => conversationRuntime.ForkSessionAsync(workspacePath, sourceSessionId, cancellationToken));

    /// <summary>
    /// Executes a prompt through the runtime command service.
    /// </summary>
    public Task<TurnExecutionResult> ExecutePromptAsync(
        string prompt,
        RuntimeCommandContext context,
        CancellationToken cancellationToken = default)
        => runtimeCommandService.ExecutePromptAsync(prompt, context, cancellationToken);

    /// <summary>
    /// Executes a prompt through the runtime command service without requiring callers to construct runtime-specific context types.
    /// </summary>
    public Task<TurnExecutionResult> ExecutePromptAsync(
        string prompt,
        string workspacePath,
        string? model,
        PermissionMode permissionMode,
        OutputFormat outputFormat,
        string? sessionId = null,
        RuntimeHostContext? hostContext = null,
        PrimaryMode? primaryMode = null,
        string? agentId = null,
        bool isInteractive = true,
        CancellationToken cancellationToken = default)
        => runtimeCommandService.ExecutePromptAsync(
            prompt,
            new RuntimeCommandContext(
                WorkingDirectory: workspacePath,
                Model: model,
                PermissionMode: permissionMode,
                OutputFormat: outputFormat,
                PrimaryMode: primaryMode,
                SessionId: sessionId,
                AgentId: agentId,
                IsInteractive: isInteractive,
                HostContext: hostContext),
            cancellationToken);

    /// <summary>
    /// Retrieves the runtime status report.
    /// </summary>
    public Task<CommandResult> GetStatusAsync(RuntimeCommandContext context, CancellationToken cancellationToken = default)
        => runtimeCommandService.GetStatusAsync(context, cancellationToken);

    /// <summary>
    /// Runs the runtime doctor checks.
    /// </summary>
    public Task<CommandResult> RunDoctorAsync(RuntimeCommandContext context, CancellationToken cancellationToken = default)
        => runtimeCommandService.RunDoctorAsync(context, cancellationToken);

    /// <summary>
    /// Lists sessions for the current workspace context.
    /// </summary>
    public Task<CommandResult> ListSessionsAsync(RuntimeCommandContext context, CancellationToken cancellationToken = default)
        => runtimeCommandService.ListSessionsAsync(context, cancellationToken);

    /// <summary>
    /// Starts the embedded HTTP server for the supplied runtime context.
    /// </summary>
    public Task RunHttpServerAsync(
        string workspaceRoot,
        string? hostName,
        int? port,
        RuntimeCommandContext context,
        CancellationToken cancellationToken = default)
        => workspaceHttpServer.RunAsync(workspaceRoot, hostName, port, context, cancellationToken);

    /// <summary>
    /// Runs the ACP stdio loop on the supplied streams.
    /// </summary>
    public Task RunAcpAsync(TextReader stdin, TextWriter stdout, CancellationToken cancellationToken = default)
        => acpStdioHost.RunAsync(stdin, stdout, cancellationToken);

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await host.StopAsync().ConfigureAwait(false);
        host.Dispose();
    }

    private async Task<T> ExecuteInHostContextAsync<T>(RuntimeHostContext? hostContext, Func<Task<T>> action)
    {
        using var scope = hostContextAccessor.BeginScope(hostContext);
        return await action().ConfigureAwait(false);
    }
}
