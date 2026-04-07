using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using SharpClaw.Code.Mcp.Abstractions;
using SharpClaw.Code.Mcp.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Mcp.Services;

/// <summary>
/// Starts MCP sessions using the official <see cref="ModelContextProtocol"/> client (stdio subprocess, HTTP streamable, or legacy SSE), tracks live sessions, and disposes clients on stop.
/// </summary>
/// <param name="loggerFactory">Logger factory for SDK clients; null uses <see cref="NullLoggerFactory"/>.</param>
public sealed class SdkMcpProcessSupervisor(ILoggerFactory? loggerFactory = null) : IMcpProcessSupervisor
{
    private static long _nextSessionId;
    private static readonly ConcurrentDictionary<long, McpClient> Sessions = new();
    private readonly ILoggerFactory resolvedLoggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    private readonly ILogger logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<SdkMcpProcessSupervisor>();

    /// <inheritdoc />
    public async Task<McpProcessStartResult> StartAsync(
        McpServerDefinition definition,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);

        var kindNorm = definition.TransportKind.Trim();
        if (kindNorm.Length == 0)
        {
            return UnsupportedTransportResult("(empty)");
        }

        var clientOptions = new McpClientOptions
        {
            ClientInfo = new Implementation
            {
                Name = "SharpClaw.Code",
                Version = typeof(SdkMcpProcessSupervisor).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            },
            InitializationTimeout = TimeSpan.FromSeconds(30),
        };

        McpClient client;
        try
        {
            if (kindNorm.Equals("stdio", StringComparison.OrdinalIgnoreCase))
            {
                client = await CreateStdioClientAsync(definition, workingDirectory, clientOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (TryGetHttpTransportMode(kindNorm, out var httpMode))
            {
                if (!Uri.TryCreate(definition.Endpoint, UriKind.Absolute, out var endpointUri)
                    || endpointUri.Scheme is not ("http" or "https"))
                {
                    return new McpProcessStartResult(
                        false,
                        null,
                        false,
                        "HTTP and SSE MCP definitions require an absolute http(s) URL in the endpoint (for example https://host/mcp).",
                        0,
                        0,
                        0,
                        0);
                }

                client = await CreateHttpClientAsync(endpointUri, httpMode, clientOptions, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                return UnsupportedTransportResult(definition.TransportKind);
            }
        }
        catch (Exception ex) when (ex is IOException or McpException or InvalidOperationException or TaskCanceledException or HttpRequestException or ArgumentException)
        {
            logger.LogWarning(ex, "MCP server '{ServerId}' failed to start.", definition.Id);
            return new McpProcessStartResult(
                false,
                null,
                false,
                ex.Message,
                0,
                0,
                0,
                0);
        }

        var (toolCount, toolsOk) = await TryCountListedAsync(
                client.ListToolsAsync(cancellationToken: cancellationToken),
                "tools",
                definition.Id)
            .ConfigureAwait(false);
        var (promptCount, promptsOk) = await TryCountListedAsync(
                client.ListPromptsAsync(cancellationToken: cancellationToken),
                "prompts",
                definition.Id)
            .ConfigureAwait(false);
        var (resourceCount, resourcesOk) = await TryCountListedAsync(
                client.ListResourcesAsync(cancellationToken: cancellationToken),
                "resources",
                definition.Id)
            .ConfigureAwait(false);

        if (!toolsOk || !promptsOk || !resourcesOk)
        {
            await client.DisposeAsync().ConfigureAwait(false);
            return new McpProcessStartResult(
                true,
                null,
                false,
                "MCP capability enumeration failed (tools, prompts, or resources). See logs for details.",
                0,
                0,
                0,
                0,
                McpFailureKind.Capabilities);
        }

        var sessionId = Interlocked.Increment(ref _nextSessionId);
        if (!Sessions.TryAdd(sessionId, client))
        {
            await client.DisposeAsync().ConfigureAwait(false);
            return new McpProcessStartResult(
                false,
                null,
                false,
                "Failed to register MCP SDK session handle.",
                0,
                0,
                0,
                0);
        }

        return new McpProcessStartResult(
            true,
            null,
            true,
            null,
            sessionId,
            toolCount,
            promptCount,
            resourceCount);
    }

    /// <inheritdoc />
    public async Task StopAsync(McpProcessStopRequest request, CancellationToken cancellationToken)
    {
        if (request.SessionHandle > 0 && Sessions.TryRemove(request.SessionHandle, out var tracked))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await tracked.DisposeAsync().ConfigureAwait(false);
        }

        if (request.Pid is int pid)
        {
            cancellationToken.ThrowIfCancellationRequested();
            KillProcessTreeByPid(pid);
        }
    }

    private async Task<McpClient> CreateStdioClientAsync(
        McpServerDefinition definition,
        string workingDirectory,
        McpClientOptions clientOptions,
        CancellationToken cancellationToken)
    {
        var transportOptions = new StdioClientTransportOptions
        {
            Command = definition.Endpoint,
            WorkingDirectory = workingDirectory,
            Name = definition.Id,
            Arguments = definition.Arguments is { Length: > 0 } ? definition.Arguments.ToList() : null,
        };

        if (definition.Environment is not null)
        {
            transportOptions.EnvironmentVariables = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var pair in definition.Environment)
            {
                transportOptions.EnvironmentVariables[pair.Key] = pair.Value;
            }
        }

        var transport = new StdioClientTransport(transportOptions, resolvedLoggerFactory);
        return await McpClient.CreateAsync(transport, clientOptions, resolvedLoggerFactory, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<McpClient> CreateHttpClientAsync(
        Uri endpoint,
        HttpTransportMode mode,
        McpClientOptions clientOptions,
        CancellationToken cancellationToken)
    {
        var transportOptions = new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            TransportMode = mode,
            ConnectionTimeout = TimeSpan.FromSeconds(30),
        };

        var transport = new HttpClientTransport(transportOptions, resolvedLoggerFactory);
        return await McpClient.CreateAsync(transport, clientOptions, resolvedLoggerFactory, cancellationToken)
            .ConfigureAwait(false);
    }

    private static bool TryGetHttpTransportMode(string normalizedKind, out HttpTransportMode mode)
    {
        if (normalizedKind.Equals("http", StringComparison.OrdinalIgnoreCase)
            || normalizedKind.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            mode = HttpTransportMode.AutoDetect;
            return true;
        }

        if (normalizedKind.Equals("streamable-http", StringComparison.OrdinalIgnoreCase)
            || normalizedKind.Equals("streamablehttp", StringComparison.OrdinalIgnoreCase))
        {
            mode = HttpTransportMode.StreamableHttp;
            return true;
        }

        if (normalizedKind.Equals("sse", StringComparison.OrdinalIgnoreCase))
        {
            mode = HttpTransportMode.Sse;
            return true;
        }

        mode = default;
        return false;
    }

    private static McpProcessStartResult UnsupportedTransportResult(string kind) =>
        new(
            false,
            null,
            false,
            $"Transport '{kind}' is not supported. Use stdio, http, https, streamable-http, or sse.",
            0,
            0,
            0,
            0);

    private async Task<int> CountListedAsync<T>(ValueTask<IList<T>> listTask, string capability, string serverId)
    {
        try
        {
            var list = await listTask.ConfigureAwait(false);
            return list.Count;
        }
        catch (Exception ex) when (ex is McpException or IOException or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "MCP server '{ServerId}' failed to list {Capability}.", serverId, capability);
            return 0;
        }
    }

    private static void KillProcessTreeByPid(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch (ArgumentException)
        {
        }
    }
}
