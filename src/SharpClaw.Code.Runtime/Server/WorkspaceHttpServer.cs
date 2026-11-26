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
        if (!IsLoopbackHost(effectiveHost) && config.Document.Server?.ApprovalAuth is not { Mode: not ApprovalAuthMode.Disabled })
        {
            throw new InvalidOperationException(
                $"Refusing to bind the SharpClaw server to non-loopback host '{effectiveHost}' without approval auth configured. "
                + "Bind to 127.0.0.1/localhost or configure server.approvalAuth.");
        }

        var allowedHosts = BuildAllowedHosts(effectiveHost, config.Document.Server?.PublicBaseUrl);
        var allowedOrigins = BuildAllowedOrigins(effectiveHost, effectivePort, config.Document.Server?.PublicBaseUrl);

        var listener = new HttpListener();
        try
        {
            listener.Prefixes.Add(prefix);
            listener.Start();
            logger.LogInformation("SharpClaw server listening on {Prefix}", prefix);

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
                    () => HandleRequestAsync(httpContext, workspaceRoot, context, allowedHosts, allowedOrigins, cancellationToken),
                    CancellationToken.None);
            }
        }
        finally
        {
            CloseListener(listener);
        }
    }

    private static void CloseListener(HttpListener listener)
    {
        try
        {
            listener.Close();
        }
        catch (HttpListenerException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task HandleRequestAsync(
        HttpListenerContext httpContext,
        string workspaceRoot,
        RuntimeCommandContext defaultContext,
        IReadOnlySet<string> allowedHosts,
        IReadOnlySet<string> allowedOrigins,
        CancellationToken cancellationToken)
    {
        var request = httpContext.Request;
        var response = httpContext.Response;
        var requestSucceeded = false;
        var statusCode = 500;
        var path = request.Url?.AbsolutePath ?? "/";
        IDisposable? hostScope = null;
        IDisposable? approvalScope = null;

        try
        {
            // Block DNS-rebinding and cross-site browser requests: the Host must be one we bind/publish,
            // and any Origin a browser attaches must be that same host.
            if (!IsAllowedHostHeader(request, allowedHosts) || !IsAllowedOrigin(request, allowedOrigins))
            {
                statusCode = 403;
                await WriteJsonAsync(response, 403, new ErrorEnvelope("Request host or origin is not allowed."), cancellationToken).ConfigureAwait(false);
                requestSucceeded = true;
                return;
            }

            if (request.HasEntityBody
                && request.ContentLength64 != 0
                && !IsJsonContentType(request.ContentType))
            {
                statusCode = 415;
                await WriteJsonAsync(response, 415, new ErrorEnvelope("Request bodies must use Content-Type: application/json."), cancellationToken).ConfigureAwait(false);
                requestSucceeded = true;
                return;
            }

            var requestHostContext = ResolveRequestHostContext(request, defaultContext.HostContext, tenantOverride: null);
            var approvalAuthStatus = await approvalIdentityService.GetStatusAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
            var approvalPrincipal = await approvalIdentityService
                .ResolveAsync(workspaceRoot, CreateApprovalIdentityRequest(request), requestHostContext, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(approvalPrincipal?.TenantId))
            {
                // An authenticated principal is bound to its tenant; never let a header pick another one.
                requestHostContext = (requestHostContext ?? CreateDefaultHostContext()) with { TenantId = approvalPrincipal.TenantId, IsEmbeddedHost = true };
            }

            hostScope = hostContextAccessor.BeginScope(requestHostContext);
            approvalScope = approvalPrincipalAccessor.BeginScope(approvalPrincipal, approvalAuthStatus);
            var requestContext = defaultContext with { HostContext = requestHostContext };

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
                statusCode = await HandlePromptAsync(request, response, workspaceRoot, requestContext, approvalPrincipal, cancellationToken).ConfigureAwait(false);
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
                statusCode = await HandleUsageSummaryAsync(request, response, workspaceRoot, approvalPrincipal, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/admin/usage/detail")
            {
                statusCode = await HandleUsageDetailAsync(request, response, workspaceRoot, approvalPrincipal, cancellationToken).ConfigureAwait(false);
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
        catch (BadRequestException exception)
        {
            statusCode = exception.StatusCode;
            requestSucceeded = true;
            await TryWriteJsonAsync(response, exception.StatusCode, new ErrorEnvelope(exception.Message), cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            statusCode = 400;
            requestSucceeded = true;
            await TryWriteJsonAsync(response, 400, new ErrorEnvelope($"Malformed JSON request body: {exception.Message}"), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Embedded server request handling failed for {Method} {Path}.", request.HttpMethod, path);
            statusCode = 500;
            await TryWriteJsonAsync(response, 500, new ErrorEnvelope(exception.Message), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            approvalScope?.Dispose();
            hostScope?.Dispose();
            try
            {
                await DispatchServerHookAsync(workspaceRoot, request, path, statusCode, requestSucceeded).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Server request hook dispatch failed for {Method} {Path}.", request.HttpMethod, path);
            }

            try
            {
                response.Close();
            }
            catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException)
            {
                // Client disconnected; nothing more to do.
            }
        }
    }

    private async Task<int> HandlePromptAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string workspaceRoot,
        RuntimeCommandContext defaultContext,
        ApprovalPrincipal? approvalPrincipal,
        CancellationToken cancellationToken)
    {
        var payload = await ReadJsonBodyAsync(request, ProtocolJsonContext.Default.ServerPromptRequest, cancellationToken).ConfigureAwait(false)
            ?? throw new BadRequestException("Request body is required.");
        if (string.IsNullOrWhiteSpace(payload.Prompt))
        {
            throw new BadRequestException("The 'prompt' field is required.");
        }

        if (!string.IsNullOrWhiteSpace(approvalPrincipal?.TenantId)
            && !string.IsNullOrWhiteSpace(payload.TenantId)
            && !string.Equals(payload.TenantId, approvalPrincipal.TenantId, StringComparison.Ordinal))
        {
            throw new BadRequestException("The requested tenant does not match the authenticated principal.", 403);
        }

        var commandContext = new RuntimeCommandContext(
            WorkingDirectory: workspaceRoot,
            Model: payload.Model ?? defaultContext.Model,
            PermissionMode: ClampPermissionMode(payload.PermissionMode, defaultContext.PermissionMode),
            OutputFormat: payload.OutputFormat ?? OutputFormat.Json,
            PrimaryMode: payload.PrimaryMode ?? defaultContext.PrimaryMode,
            SessionId: payload.SessionId ?? defaultContext.SessionId,
            AgentId: payload.AgentId ?? defaultContext.AgentId,
            IsInteractive: false,
            HostContext: string.IsNullOrWhiteSpace(approvalPrincipal?.TenantId)
                ? ResolveRequestHostContext(request, defaultContext.HostContext, payload.TenantId)
                : defaultContext.HostContext);

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
        if (request.HasEntityBody && request.ContentLength64 != 0)
        {
            payload = await ReadJsonBodyAsync(request, ProtocolJsonContext.Default.AdminCreateSessionRequest, cancellationToken).ConfigureAwait(false);
        }

        var session = await conversationRuntime
            .CreateSessionAsync(
                workspaceRoot,
                ClampPermissionMode(payload?.PermissionMode, context.PermissionMode),
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
        var payload = await ReadJsonBodyAsync(request, ProtocolJsonContext.Default.WorkspaceSearchRequest, cancellationToken).ConfigureAwait(false)
            ?? throw new BadRequestException("Request body is required.");
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
        var payload = await ReadJsonBodyAsync(request, ProtocolJsonContext.Default.ToolPackageInstallRequest, cancellationToken).ConfigureAwait(false)
            ?? throw new BadRequestException("Request body is required.");
        var result = await toolPackageService.InstallAsync(workspaceRoot, payload, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(response, 200, result, cancellationToken).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleUsageSummaryAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string workspaceRoot,
        ApprovalPrincipal? approvalPrincipal,
        CancellationToken cancellationToken)
    {
        var query = ParseUsageQuery(request, workspaceRoot, approvalPrincipal);
        var report = await usageMeteringService.GetSummaryAsync(workspaceRoot, query, cancellationToken).ConfigureAwait(false);
        await WriteJsonAsync(response, 200, report, cancellationToken).ConfigureAwait(false);
        return 200;
    }

    private async Task<int> HandleUsageDetailAsync(
        HttpListenerRequest request,
        HttpListenerResponse response,
        string workspaceRoot,
        ApprovalPrincipal? approvalPrincipal,
        CancellationToken cancellationToken)
    {
        var query = ParseUsageQuery(request, workspaceRoot, approvalPrincipal);
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

    private static UsageMeteringQuery ParseUsageQuery(HttpListenerRequest request, string workspaceRoot, ApprovalPrincipal? approvalPrincipal)
        => new(
            FromUtc: TryParseDateTimeOffset(request.QueryString["fromUtc"] ?? request.QueryString["from"]),
            ToUtc: TryParseDateTimeOffset(request.QueryString["toUtc"] ?? request.QueryString["to"]),
            // Authenticated principals may only query their own tenant.
            TenantId: string.IsNullOrWhiteSpace(approvalPrincipal?.TenantId) ? request.QueryString["tenantId"] : approvalPrincipal.TenantId,
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
        // Storage root and session store are server configuration; HTTP callers must not be able to redirect
        // persistence to arbitrary directories, so request headers for them are ignored.
        var storageRoot = fallback?.StorageRoot;
        var sessionStoreKind = fallback?.SessionStoreKind ?? SessionStoreKind.FileSystem;
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
        => DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private const long MaxRequestBodyBytes = 8 * 1024 * 1024;

    private static async Task<T?> ReadJsonBodyAsync<T>(HttpListenerRequest request, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        if (request.ContentLength64 > MaxRequestBodyBytes)
        {
            throw new BadRequestException($"Request body exceeds the {MaxRequestBodyBytes} byte limit.", 413);
        }

        await using var body = request.InputStream;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxRequestBodyBytes)
            {
                throw new BadRequestException($"Request body exceeds the {MaxRequestBodyBytes} byte limit.", 413);
            }

            buffer.Write(chunk, 0, read);
        }

        if (buffer.Length == 0)
        {
            return default;
        }

        buffer.Position = 0;
        return await JsonSerializer.DeserializeAsync(buffer, typeInfo, cancellationToken).ConfigureAwait(false);
    }

    private static PermissionMode ClampPermissionMode(PermissionMode? requested, PermissionMode serverDefault)
        => requested is { } value && value < serverDefault ? value : serverDefault;

    private static RuntimeHostContext CreateDefaultHostContext()
        => new(HostId: "workspace-http-server", TenantId: null, StorageRoot: null, SessionStoreKind: SessionStoreKind.FileSystem, IsEmbeddedHost: false);

    private static bool IsJsonContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || (mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) && mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsLoopbackHost(string host)
    {
        var name = host.Trim('[', ']');
        return name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(name, out var address) && IPAddress.IsLoopback(address));
    }

    private static IReadOnlySet<string> BuildAllowedHosts(string bindHost, string? publicBaseUrl)
    {
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "localhost", "127.0.0.1", "::1" };
        var bindName = bindHost.Trim('[', ']');
        if (bindName is not ("*" or "+" or "0.0.0.0" or "::"))
        {
            hosts.Add(bindName);
        }

        if (!string.IsNullOrWhiteSpace(publicBaseUrl) && Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicUri))
        {
            hosts.Add(publicUri.Host.Trim('[', ']'));
        }

        return hosts;
    }

    private static bool IsAllowedHostHeader(HttpListenerRequest request, IReadOnlySet<string> allowedHosts)
    {
        var hostHeader = request.Headers["Host"];
        if (string.IsNullOrWhiteSpace(hostHeader) || !Uri.TryCreate($"http://{hostHeader}", UriKind.Absolute, out var hostUri))
        {
            return false;
        }

        return allowedHosts.Contains(hostUri.Host.Trim('[', ']')) || IsLoopbackHost(hostUri.Host);
    }

    private static IReadOnlySet<string> BuildAllowedOrigins(string bindHost, int port, string? publicBaseUrl)
    {
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bindName = bindHost.Trim('[', ']');
        if (bindName is not ("*" or "+" or "0.0.0.0" or "::") && !IsLoopbackHost(bindName)
            && Uri.TryCreate($"http://{bindHost}:{port}", UriKind.Absolute, out var bindUri))
        {
            origins.Add(NormalizeOrigin(bindUri));
        }

        if (!string.IsNullOrWhiteSpace(publicBaseUrl) && Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var publicUri))
        {
            origins.Add(NormalizeOrigin(publicUri));
        }

        return origins;
    }

    private static string NormalizeOrigin(Uri uri)
        => $"{uri.Scheme}://{uri.Host.Trim('[', ']')}:{uri.Port}".ToLowerInvariant();

    private static bool IsAllowedOrigin(HttpListenerRequest request, IReadOnlySet<string> allowedOrigins)
    {
        var origin = request.Headers["Origin"];
        if (string.IsNullOrWhiteSpace(origin))
        {
            // Non-browser clients (CLI, SDK, editors) do not send Origin.
            return true;
        }

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
            || (originUri.Scheme != Uri.UriSchemeHttp && originUri.Scheme != Uri.UriSchemeHttps))
        {
            return false;
        }

        if (IsLoopbackHost(originUri.Host))
        {
            // A loopback origin must be this server itself (plain HTTP, same port), not another local web app.
            return originUri.Scheme == Uri.UriSchemeHttp && originUri.Port == (request.Url?.Port ?? -1);
        }

        // Non-loopback origins must match a configured origin exactly: scheme, host, and port.
        return allowedOrigins.Contains(NormalizeOrigin(originUri));
    }

    private static async Task TryWriteJsonAsync(HttpListenerResponse response, int statusCode, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await WriteJsonAsync(response, statusCode, payload, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is HttpListenerException or ObjectDisposedException or InvalidOperationException or IOException or OperationCanceledException)
        {
            // Headers may already be sent (SSE) or the client disconnected.
        }
    }

    private sealed class BadRequestException(string message, int statusCode = 400) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }

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
