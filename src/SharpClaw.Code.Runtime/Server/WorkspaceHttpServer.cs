using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Memory.Abstractions;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Tools.Abstractions;

namespace SharpClaw.Code.Runtime.Server;

/// <summary>
/// Hosts a minimal JSON and SSE HTTP surface over the existing runtime command services.
/// </summary>
public sealed class WorkspaceHttpServer(
    IRuntimeCommandService runtimeCommandService,
    IConversationRuntime conversationRuntime,
    IShareSessionService shareSessionService,
    ISharpClawConfigService sharpClawConfigService,
    IHookDispatcher hookDispatcher,
    IProviderCatalogService providerCatalogService,
    IWorkspaceIndexService workspaceIndexService,
    IWorkspaceSearchService workspaceSearchService,
    IPersistentMemoryStore persistentMemoryStore,
    IRuntimeEventStream runtimeEventStream,
    IUsageMeteringService usageMeteringService,
    IToolPackageService toolPackageService,
    IApprovalIdentityService approvalIdentityService,
    IApprovalPrincipalAccessor approvalPrincipalAccessor,
    IRuntimeHostContextAccessor hostContextAccessor,
    ILogger<WorkspaceHttpServer> logger) : IWorkspaceHttpServer
{
    /// <inheritdoc />
    public async Task RunAsync(
        string workspaceRoot,
        string? host,
        int? port,
        RuntimeCommandContext context,
        CancellationToken cancellationToken)
    {
        var config = await sharpClawConfigService.GetConfigAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var effectiveHost = string.IsNullOrWhiteSpace(host) ? config.Document.Server?.Host ?? "127.0.0.1" : host!;
        var effectivePort = port is > 0 ? port.Value : config.Document.Server?.Port ?? 7345;
        var prefix = $"http://{effectiveHost}:{effectivePort}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        logger.LogInformation("SharpClaw server listening on {Prefix}", prefix);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext httpContext;
                try
                {
                    httpContext = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _ = Task.Run(
                    () => HandleRequestAsync(httpContext, workspaceRoot, context, cancellationToken),
                    CancellationToken.None);
            }
        }
        finally
        {
            if (listener.IsListening)
            {
                listener.Stop();
            }
        }
    }

    private async Task HandleRequestAsync(
        HttpListenerContext httpContext,
        string workspaceRoot,
        RuntimeCommandContext defaultContext,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Request;
        var response = httpContext.Response;
        var requestSucceeded = false;
        var statusCode = 500;
        var path = request.Url?.AbsolutePath ?? "/";
        var requestHostContext = ResolveRequestHostContext(request, defaultContext.HostContext, tenantOverride: null);
        var approvalAuthStatus = await approvalIdentityService.GetStatusAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var approvalPrincipal = await approvalIdentityService
            .ResolveAsync(workspaceRoot, CreateApprovalIdentityRequest(request), requestHostContext, cancellationToken)
            .ConfigureAwait(false);
        using var hostScope = hostContextAccessor.BeginScope(requestHostContext);
        using var approvalScope = approvalPrincipalAccessor.BeginScope(approvalPrincipal, approvalAuthStatus);
        var requestContext = defaultContext with { HostContext = requestHostContext };

        try
        {
            if (IsAdminPath(path)
                && approvalAuthStatus.RequireForAdmin
                && approvalPrincipal is null)
            {
                statusCode = 401;
                await WriteJsonAsync(response, 401, new ErrorEnvelope("Admin authentication is required for this endpoint."), cancellationToken).ConfigureAwait(false);
                requestSucceeded = true;
                return;
            }

            if (request.HttpMethod == "GET" && path == "/v1/status")
            {
                var result = await runtimeCommandService.GetStatusAsync(requestContext, cancellationToken).ConfigureAwait(false);
                statusCode = await WriteCommandResultAsync(response, result, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/doctor")
            {
                var result = await runtimeCommandService.RunDoctorAsync(requestContext, cancellationToken).ConfigureAwait(false);
                statusCode = await WriteCommandResultAsync(response, result, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/sessions")
            {
                var result = await runtimeCommandService.ListSessionsAsync(requestContext, cancellationToken).ConfigureAwait(false);
                statusCode = await WriteCommandResultAsync(response, result, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path.StartsWith("/v1/sessions/", StringComparison.Ordinal))
            {
                var sessionId = Uri.UnescapeDataString(path["/v1/sessions/".Length..]);
                var result = await runtimeCommandService.InspectSessionAsync(sessionId, requestContext, cancellationToken).ConfigureAwait(false);
                statusCode = await WriteCommandResultAsync(response, result, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "POST" && path == "/v1/prompt")
            {
                statusCode = await HandlePromptAsync(request, response, workspaceRoot, requestContext, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "POST" && path.StartsWith("/v1/share/", StringComparison.Ordinal))
            {
                var sessionId = Uri.UnescapeDataString(path["/v1/share/".Length..]);
                var result = await runtimeCommandService.ShareSessionAsync(sessionId, requestContext, cancellationToken).ConfigureAwait(false);
                statusCode = await WriteCommandResultAsync(response, result, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "DELETE" && path.StartsWith("/v1/share/", StringComparison.Ordinal))
            {
                var sessionId = Uri.UnescapeDataString(path["/v1/share/".Length..]);
                var result = await runtimeCommandService.UnshareSessionAsync(sessionId, requestContext, cancellationToken).ConfigureAwait(false);
                statusCode = await WriteCommandResultAsync(response, result, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/admin/providers")
            {
                statusCode = 200;
                var catalog = await providerCatalogService.ListAsync(cancellationToken).ConfigureAwait(false);
                await WriteJsonAsync(response, 200, catalog, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/admin/auth/status")
            {
                statusCode = 200;
                await WriteJsonAsync(response, 200, approvalAuthStatus, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "POST" && path == "/v1/admin/sessions")
            {
                statusCode = await HandleAdminCreateSessionAsync(request, response, workspaceRoot, requestContext, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "POST" && path.StartsWith("/v1/admin/sessions/", StringComparison.Ordinal) && path.EndsWith("/fork", StringComparison.Ordinal))
            {
                var sessionId = Uri.UnescapeDataString(path["/v1/admin/sessions/".Length..^"/fork".Length]);
                statusCode = await HandleAdminForkSessionAsync(response, workspaceRoot, sessionId, requestContext, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/admin/index/status")
            {
                statusCode = 200;
                var status = await workspaceIndexService.GetStatusAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
                await WriteJsonAsync(response, 200, status, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "POST" && path == "/v1/admin/index/refresh")
            {
                statusCode = 200;
                var refresh = await workspaceIndexService.RefreshAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
                await WriteJsonAsync(response, 200, refresh, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "POST" && path == "/v1/admin/search")
            {
                statusCode = await HandleWorkspaceSearchAsync(request, response, workspaceRoot, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/admin/memory")
            {
                statusCode = await HandleMemoryListAsync(request, response, workspaceRoot, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/admin/events/recent")
            {
                statusCode = await HandleRecentEventsAsync(response, workspaceRoot, requestHostContext, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/admin/events/stream")
            {
                statusCode = await HandleEventStreamAsync(response, workspaceRoot, requestHostContext, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/admin/tool-packages")
            {
                statusCode = 200;
                var packages = await toolPackageService.ListInstalledAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
                await WriteJsonAsync(response, 200, packages, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "POST" && path == "/v1/admin/tool-packages/install")
            {
                statusCode = await HandleToolPackageInstallAsync(request, response, workspaceRoot, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/admin/usage/summary")
            {
                statusCode = await HandleUsageSummaryAsync(request, response, workspaceRoot, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/admin/usage/detail")
            {
                statusCode = await HandleUsageDetailAsync(request, response, workspaceRoot, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path.StartsWith("/s/", StringComparison.Ordinal))
            {
                var shareId = Uri.UnescapeDataString(path["/s/".Length..]);
                var snapshot = await shareSessionService.GetSharedSnapshotAsync(workspaceRoot, shareId, cancellationToken).ConfigureAwait(false);
                if (snapshot is null)
                {
                    statusCode = 404;
                    await WriteJsonAsync(response, 404, new ErrorEnvelope("Share not found."), cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    statusCode = 200;
                    await WriteJsonAsync(response, 200, snapshot, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                statusCode = 404;
                await WriteJsonAsync(response, 404, new ErrorEnvelope("Not found."), cancellationToken).ConfigureAwait(false);
            }

            requestSucceeded = statusCode < 500;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Embedded server request handling failed for {Method} {Path}.", request.HttpMethod, path);
            statusCode = 500;
            await WriteJsonAsync(response, 500, new ErrorEnvelope(exception.Message), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await DispatchServerHookAsync(workspaceRoot, request, path, statusCode, requestSucceeded).ConfigureAwait(false);
            response.Close();
        }
    }

    private async Task<int> HandlePromptAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string workspaceRoot,
        RuntimeCommandContext defaultContext,
        CancellationToken cancellationToken)
    {
        await using var body = request.InputStream;
        var payload = await JsonSerializer.DeserializeAsync(body, ProtocolJsonContext.Default.ServerPromptRequest, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Request body is required.");
        if (string.IsNullOrWhiteSpace(payload.Prompt))
        {
            throw new InvalidOperationException("The 'prompt' field is required.");
        }

        var commandContext = new RuntimeCommandContext(
            WorkingDirectory: workspaceRoot,
            Model: payload.Model ?? defaultContext.Model,
            PermissionMode: payload.PermissionMode ?? defaultContext.PermissionMode,
            OutputFormat: payload.OutputFormat ?? OutputFormat.Json,
            PrimaryMode: payload.PrimaryMode ?? defaultContext.PrimaryMode,
            SessionId: payload.SessionId ?? defaultContext.SessionId,
            AgentId: payload.AgentId ?? defaultContext.AgentId,
            IsInteractive: false,
            HostContext: ResolveRequestHostContext(request, defaultContext.HostContext, payload.TenantId));

        TurnExecutionResult result;
        using (hostContextAccessor.BeginScope(commandContext.HostContext))
        {
            result = await runtimeCommandService.ExecutePromptAsync(payload.Prompt, commandContext, cancellationToken).ConfigureAwait(false);
        }
        var accept = request.Headers["Accept"];
        var wantsSse = (accept?.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) ?? false)
            || string.Equals(request.QueryString["stream"], "true", StringComparison.OrdinalIgnoreCase);

        if (wantsSse)
        {
            response.StatusCode = 200;
            response.ContentType = "text/event-stream";
            response.ContentEncoding = Encoding.UTF8;
            await using var writer = new StreamWriter(response.OutputStream, new UTF8Encoding(false), leaveOpen: true);
            foreach (var runtimeEvent in result.Events)
            {
                await WriteSseAsync(
                        writer,
                        "runtime-event",
                        JsonSerializer.Serialize(runtimeEvent, runtimeEvent.GetType(), ServerJsonOptions))
                    .ConfigureAwait(false);
            }

            await WriteSseAsync(
                    writer,
                    "result",
                    JsonSerializer.Serialize(result, ProtocolJsonContext.Default.TurnExecutionResult))
                .ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            return 200;
        }

        await WriteJsonAsync(response, 200, result, cancellationToken).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleAdminCreateSessionAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string workspaceRoot,
        RuntimeCommandContext context,
        CancellationToken cancellationToken)
    {
        AdminCreateSessionRequest? payload = null;
        if (request.HasEntityBody)
        {
            await using var body = request.InputStream;
            payload = await JsonSerializer.DeserializeAsync(body, ProtocolJsonContext.Default.AdminCreateSessionRequest, cancellationToken).ConfigureAwait(false);
        }

        var session = await conversationRuntime
            .CreateSessionAsync(
                workspaceRoot,
                payload?.PermissionMode ?? context.PermissionMode,
                payload?.OutputFormat ?? context.OutputFormat,
                cancellationToken)
            .ConfigureAwait(false);
        await WriteJsonAsync(response, 200, session, cancellationToken).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleAdminForkSessionAsync(
        HttpListenerResponse response,
        string workspaceRoot,
        string sessionId,
        RuntimeCommandContext context,
        CancellationToken cancellationToken)
    {
        using var hostScope = hostContextAccessor.BeginScope(context.HostContext);
        var session = await conversationRuntime.ForkSessionAsync(workspaceRoot, sessionId, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(response, 200, session, cancellationToken).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleWorkspaceSearchAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        await using var body = request.InputStream;
        var payload = await JsonSerializer.DeserializeAsync(body, ProtocolJsonContext.Default.WorkspaceSearchRequest, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Request body is required.");
        var result = await workspaceSearchService.SearchAsync(workspaceRoot, payload, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(response, 200, result, cancellationToken).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleMemoryListAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var scope = TryParseEnum<MemoryScope>(request.QueryString["scope"]);
        var query = request.QueryString["query"] ?? request.QueryString["q"];
        var limit = ParseInt(request.QueryString["limit"], 50, 1, 500);
        var entries = await persistentMemoryStore
            .ListAsync(workspaceRoot, scope, query, limit, cancellationToken)
            .ConfigureAwait(false);
        await WriteJsonAsync(response, 200, entries, cancellationToken).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleRecentEventsAsync(
        HttpListenerResponse response,
        string workspaceRoot,
        RuntimeHostContext? hostContext,
        CancellationToken cancellationToken)
    {
        var events = FilterEnvelopes(runtimeEventStream.GetRecentEnvelopesSnapshot(), workspaceRoot, hostContext);
        await WriteJsonAsync(response, 200, events, cancellationToken).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleEventStreamAsync(
        HttpListenerResponse response,
        string workspaceRoot,
        RuntimeHostContext? hostContext,
        CancellationToken cancellationToken)
    {
        response.StatusCode = 200;
        response.ContentType = "text/event-stream";
        response.ContentEncoding = Encoding.UTF8;
        await using var writer = new StreamWriter(response.OutputStream, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteLineAsync("retry: 1000").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        await foreach (var envelope in runtimeEventStream.StreamAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!ShouldIncludeEnvelope(envelope, workspaceRoot, hostContext))
            {
                continue;
            }

            await WriteSseAsync(
                    writer,
                    "runtime-envelope",
                    JsonSerializer.Serialize(envelope, ProtocolJsonContext.Default.RuntimeEventEnvelope))
                .ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        return 200;
    }

    private async Task<int> HandleToolPackageInstallAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        await using var body = request.InputStream;
        var payload = await JsonSerializer.DeserializeAsync(body, ProtocolJsonContext.Default.ToolPackageInstallRequest, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Request body is required.");
        var result = await toolPackageService.InstallAsync(workspaceRoot, payload, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(response, 200, result, cancellationToken).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleUsageSummaryAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var query = ParseUsageQuery(request, workspaceRoot);
        var report = await usageMeteringService.GetSummaryAsync(workspaceRoot, query, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(response, 200, report, cancellationToken).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleUsageDetailAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var query = ParseUsageQuery(request, workspaceRoot);
        var limit = ParseInt(request.QueryString["limit"], 100, 1, 1000);
        var report = await usageMeteringService.GetDetailAsync(workspaceRoot, query, limit, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(response, 200, report, cancellationToken).ConfigureAwait(false);
        return 200;
    }

    private static async Task WriteSseAsync(StreamWriter writer, string eventName, string payload)
    {
        await writer.WriteLineAsync($"event: {eventName}").ConfigureAwait(false);
        foreach (var line in payload.Split('\n'))
        {
            await writer.WriteLineAsync($"data: {line.TrimEnd('\r')}").ConfigureAwait(false);
        }

        await writer.WriteLineAsync().ConfigureAwait(false);
    }

    private static async Task<int> WriteCommandResultAsync(HttpListenerResponse response, CommandResult result, CancellationToken cancellationToken)
    {
        var parsedData = TryParseData(result.DataJson);
        var envelope = new ServerCommandEnvelope(
            result.Succeeded,
            result.ExitCode,
            result.Message,
            parsedData,
            result.DataJson is not null && parsedData is null ? result.DataJson : null);
        var statusCode = result.Succeeded ? 200 : 400;
        await WriteJsonAsync(response, statusCode, envelope, cancellationToken).ConfigureAwait(false);
        return statusCode;
    }

    private static JsonElement? TryParseData(string? dataJson)
    {
        if (string.IsNullOrWhiteSpace(dataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(dataJson);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static readonly JsonSerializerOptions ServerJsonOptions = CreateServerJsonOptions();

    private static async Task WriteJsonAsync(HttpListenerResponse response, int statusCode, object payload, CancellationToken cancellationToken)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json";
        response.ContentEncoding = Encoding.UTF8;
        var json = JsonSerializer.Serialize(payload, payload.GetType(), ServerJsonOptions);
        await using var writer = new StreamWriter(response.OutputStream, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private Task DispatchServerHookAsync(string workspaceRoot, HttpListenerRequest request, string path, int statusCode, bool succeeded)
    {
        var payload = JsonSerializer.Serialize(
            new ServerRequestCompletedPayload(
                request.HttpMethod,
                path,
                statusCode,
                succeeded,
                DateTimeOffset.UtcNow),
            ServerJsonOptions);
        return hookDispatcher.DispatchAsync(workspaceRoot, HookTriggerKind.ServerRequestCompleted, payload, CancellationToken.None);
    }

    private static JsonSerializerOptions CreateServerJsonOptions()
    {
        var options = new JsonSerializerOptions(ProtocolJsonContext.Default.Options)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                ProtocolJsonContext.Default,
                new DefaultJsonTypeInfoResolver()),
        };
        return options;
    }

    private static ApprovalIdentityRequest CreateApprovalIdentityRequest(HttpListenerRequest request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in request.Headers.AllKeys.Where(static key => !string.IsNullOrWhiteSpace(key)))
        {
            headers[key!] = request.Headers[key!]!;
        }

        return new ApprovalIdentityRequest(request.Headers["Authorization"], headers);
    }

    private static UsageMeteringQuery ParseUsageQuery(HttpListenerRequest request, string workspaceRoot)
        => new(
            FromUtc: TryParseDateTimeOffset(request.QueryString["fromUtc"] ?? request.QueryString["from"]),
            ToUtc: TryParseDateTimeOffset(request.QueryString["toUtc"] ?? request.QueryString["to"]),
            TenantId: request.QueryString["tenantId"],
            HostId: request.QueryString["hostId"],
            WorkspaceRoot: workspaceRoot,
            SessionId: request.QueryString["sessionId"]);

    private static RuntimeHostContext? ResolveRequestHostContext(
        HttpListenerRequest request,
        RuntimeHostContext? fallback,
        string? tenantOverride)
    {
        var hostId = HeaderOrDefault(request, "X-SharpClaw-Host-Id", fallback?.HostId);
        var tenantId = string.IsNullOrWhiteSpace(tenantOverride)
            ? HeaderOrDefault(request, "X-SharpClaw-Tenant-Id", fallback?.TenantId)
            : tenantOverride;
        var storageRoot = HeaderOrDefault(request, "X-SharpClaw-Storage-Root", fallback?.StorageRoot);
        var sessionStoreKind = TryParseEnum<SessionStoreKind>(request.Headers["X-SharpClaw-Session-Store"]) ?? fallback?.SessionStoreKind ?? SessionStoreKind.FileSystem;
        var isEmbeddedHost = fallback?.IsEmbeddedHost ?? false;

        if (string.IsNullOrWhiteSpace(hostId)
            && string.IsNullOrWhiteSpace(tenantId)
            && string.IsNullOrWhiteSpace(storageRoot)
            && fallback is null)
        {
            return null;
        }

        return new RuntimeHostContext(
            HostId: string.IsNullOrWhiteSpace(hostId) ? "workspace-http-server" : hostId!,
            TenantId: string.IsNullOrWhiteSpace(tenantId) ? null : tenantId,
            StorageRoot: string.IsNullOrWhiteSpace(storageRoot) ? null : storageRoot,
            SessionStoreKind: sessionStoreKind,
            IsEmbeddedHost: isEmbeddedHost || !string.IsNullOrWhiteSpace(storageRoot) || !string.IsNullOrWhiteSpace(tenantId));
    }

    private static bool IsAdminPath(string path)
        => path.StartsWith("/v1/admin/", StringComparison.Ordinal);

    private static IReadOnlyList<RuntimeEventEnvelope> FilterEnvelopes(
        IEnumerable<RuntimeEventEnvelope> source,
        string workspaceRoot,
        RuntimeHostContext? hostContext)
        => source.Where(envelope => ShouldIncludeEnvelope(envelope, workspaceRoot, hostContext)).ToArray();

    private static bool ShouldIncludeEnvelope(
        RuntimeEventEnvelope envelope,
        string workspaceRoot,
        RuntimeHostContext? hostContext)
    {
        var workspaceMatches = string.IsNullOrWhiteSpace(envelope.WorkspacePath)
            || string.Equals(envelope.WorkspacePath, workspaceRoot, StringComparison.Ordinal);
        if (!workspaceMatches)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(hostContext?.TenantId))
        {
            return true;
        }

        return string.Equals(envelope.TenantId, hostContext.TenantId, StringComparison.Ordinal);
    }

    private static string? HeaderOrDefault(HttpListenerRequest request, string headerName, string? fallback)
        => string.IsNullOrWhiteSpace(request.Headers[headerName]) ? fallback : request.Headers[headerName];

    private static TEnum? TryParseEnum<TEnum>(string? value)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : null;

    private static int ParseInt(string? value, int fallback, int min, int max)
        => int.TryParse(value, out var parsed) ? Math.Clamp(parsed, min, max) : fallback;

    private static DateTimeOffset? TryParseDateTimeOffset(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    private sealed record ServerCommandEnvelope(
        bool Succeeded,
        int ExitCode,
        string Message,
        JsonElement? Data,
        string? DataRaw);

    private sealed record ErrorEnvelope(string Error);

    private sealed record ServerRequestCompletedPayload(
        string Method,
        string Path,
        int StatusCode,
        bool Succeeded,
        DateTimeOffset CompletedAtUtc);
}
