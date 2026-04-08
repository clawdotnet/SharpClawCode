using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Mcp.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Events;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Telemetry;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.Mcp.Services;

/// <summary>
/// Implements a pragmatic process-hosted MCP lifecycle manager.
/// </summary>
public sealed class ProcessMcpServerHost(
    IMcpRegistry registry,
    IMcpProcessSupervisor processSupervisor,
    ISystemClock systemClock,
    IRuntimeEventPublisher? runtimeEventPublisher = null,
    ILogger<ProcessMcpServerHost>? logger = null) : IMcpServerHost
{
    private readonly ILogger<ProcessMcpServerHost> logger = logger ?? NullLogger<ProcessMcpServerHost>.Instance;
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> ServerLocks = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public async Task<McpServerStatus> StartAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken)
        => await ExecuteWithServerLockAsync(workspaceRoot, serverId, ct => StartCoreAsync(workspaceRoot, serverId, ct), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<McpServerStatus> StopAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken)
        => await ExecuteWithServerLockAsync(workspaceRoot, serverId, ct => StopCoreAsync(workspaceRoot, serverId, ct), cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<McpServerStatus> RestartAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken)
        => await ExecuteWithServerLockAsync(
            workspaceRoot,
            serverId,
            async ct =>
            {
                await StopCoreAsync(workspaceRoot, serverId, ct).ConfigureAwait(false);
                return await StartCoreAsync(workspaceRoot, serverId, ct).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);

    private async Task<McpServerStatus> StartCoreAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken)
    {
        var server = await GetRequiredServerAsync(workspaceRoot, serverId, cancellationToken).ConfigureAwait(false);
        if (server.Status.State is McpLifecycleState.Starting or McpLifecycleState.Connecting or McpLifecycleState.Ready or McpLifecycleState.Degraded)
        {
            logger.LogInformation(
                "MCP server {ServerId} is already active with state {State}; skipping duplicate start.",
                serverId,
                server.Status.State);
            return server.Status;
        }

        var startingStatus = new McpServerStatus(
            ServerId: serverId,
            State: McpLifecycleState.Starting,
            UpdatedAtUtc: systemClock.UtcNow,
            StatusMessage: "Starting MCP server.",
            ToolCount: server.Status.ToolCount,
            IsHealthy: false,
            FailureKind: McpFailureKind.None,
            HandshakeSucceeded: false,
            SessionHandle: null,
            PromptCount: server.Status.PromptCount,
            ResourceCount: server.Status.ResourceCount);

        await PersistAndEmitAsync(workspaceRoot, server.Status, startingStatus, cancellationToken).ConfigureAwait(false);
        var processResult = await processSupervisor.StartAsync(server.Definition, workspaceRoot, cancellationToken).ConfigureAwait(false);

        var finalStatus = processResult.Started
            ? processResult.HandshakeSucceeded
                ? startingStatus with
                {
                    State = McpLifecycleState.Ready,
                    UpdatedAtUtc = systemClock.UtcNow,
                    StatusMessage = "MCP server session established (official MCP SDK).",
                    IsHealthy = true,
                    Pid = processResult.Pid,
                    HandshakeSucceeded = true,
                    SessionHandle = processResult.SessionHandle == 0 ? null : processResult.SessionHandle,
                    ToolCount = processResult.ToolCount,
                    PromptCount = processResult.PromptCount,
                    ResourceCount = processResult.ResourceCount
                }
                : startingStatus with
                {
                    State = McpLifecycleState.Faulted,
                    UpdatedAtUtc = systemClock.UtcNow,
                    StatusMessage = processResult.FailureReason,
                    FailureKind = processResult.FailureKind ?? McpFailureKind.Handshake,
                    Pid = processResult.Pid,
                    HandshakeSucceeded = false
                }
            : startingStatus with
            {
                State = McpLifecycleState.Faulted,
                UpdatedAtUtc = systemClock.UtcNow,
                StatusMessage = processResult.FailureReason,
                FailureKind = processResult.FailureKind ?? McpFailureKind.Startup,
                Pid = null,
                HandshakeSucceeded = false
            };

        await PersistAndEmitAsync(workspaceRoot, startingStatus, finalStatus, cancellationToken).ConfigureAwait(false);
        return finalStatus;
    }

    private async Task<McpServerStatus> StopCoreAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken)
    {
        var server = await GetRequiredServerAsync(workspaceRoot, serverId, cancellationToken).ConfigureAwait(false);
        if (server.Status.State is McpLifecycleState.Stopped or McpLifecycleState.Disabled)
        {
            return server.Status;
        }

        try
        {
            await processSupervisor
                .StopAsync(
                    new McpProcessStopRequest(server.Status.Pid, server.Status.SessionHandle ?? 0),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "MCP supervisor stop failed for server {ServerId}.", serverId);
            var faulted = server.Status with
            {
                State = McpLifecycleState.Faulted,
                UpdatedAtUtc = systemClock.UtcNow,
                StatusMessage = $"MCP stop failed: {ex.Message}",
                IsHealthy = false,
                FailureKind = McpFailureKind.Runtime,
            };

            await PersistAndEmitAsync(workspaceRoot, server.Status, faulted, cancellationToken).ConfigureAwait(false);
            return faulted;
        }

        var stoppedStatus = server.Status with
        {
            State = McpLifecycleState.Stopped,
            UpdatedAtUtc = systemClock.UtcNow,
            StatusMessage = "MCP server is stopped.",
            IsHealthy = false,
            Pid = null,
            FailureKind = McpFailureKind.None,
            HandshakeSucceeded = false,
            SessionHandle = null,
            ToolCount = 0,
            PromptCount = 0,
            ResourceCount = 0
        };

        await PersistAndEmitAsync(workspaceRoot, server.Status, stoppedStatus, cancellationToken).ConfigureAwait(false);
        return stoppedStatus;
    }

    /// <inheritdoc />
    public async Task<McpServerStatus?> GetStatusAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken)
        => (await registry.GetAsync(workspaceRoot, serverId, cancellationToken).ConfigureAwait(false))?.Status;

    private async Task<RegisteredMcpServer> GetRequiredServerAsync(string workspaceRoot, string serverId, CancellationToken cancellationToken)
        => await registry.GetAsync(workspaceRoot, serverId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"MCP server '{serverId}' is not registered.");

    private async Task PersistAndEmitAsync(
        string workspaceRoot,
        McpServerStatus previousStatus,
        McpServerStatus currentStatus,
        CancellationToken cancellationToken)
    {
        await registry.UpdateStatusAsync(workspaceRoot, currentStatus, cancellationToken).ConfigureAwait(false);
        var runtimeEvent = new McpStateChangedEvent(
            EventId: $"event-{Guid.NewGuid():N}",
            SessionId: "system",
            TurnId: null,
            OccurredAtUtc: systemClock.UtcNow,
            ServerId: currentStatus.ServerId,
            PreviousState: previousStatus.State,
            CurrentState: currentStatus.State,
            Message: currentStatus.StatusMessage);

        logger.LogInformation(
            "MCP server {ServerId} transitioned from {PreviousState} to {CurrentState}. {@RuntimeEvent}",
            runtimeEvent.ServerId,
            runtimeEvent.PreviousState,
            runtimeEvent.CurrentState,
            runtimeEvent);

        if (runtimeEventPublisher is not null)
        {
            await runtimeEventPublisher
                .PublishAsync(
                    runtimeEvent,
                    new RuntimeEventPublishOptions(workspaceRoot, SessionId: "system", PersistToSessionStore: false),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static string CreateServerLockKey(string workspaceRoot, string serverId)
        => $"{workspaceRoot}\u0000{serverId}";

    private static SemaphoreSlim GetServerLock(string workspaceRoot, string serverId)
        => ServerLocks.GetOrAdd(CreateServerLockKey(workspaceRoot, serverId), static _ => new SemaphoreSlim(1, 1));

    private static async Task<T> ExecuteWithServerLockAsync<T>(
        string workspaceRoot,
        string serverId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);
        ArgumentNullException.ThrowIfNull(action);

        var gate = GetServerLock(workspaceRoot, serverId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }
}
