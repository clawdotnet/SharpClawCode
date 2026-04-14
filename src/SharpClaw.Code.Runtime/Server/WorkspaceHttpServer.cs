using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.Server;

/// <summary>
/// Hosts a minimal JSON and SSE HTTP surface over the existing runtime command services.
/// </summary>
public sealed class WorkspaceHttpServer(
    IRuntimeCommandService runtimeCommandService,
    IShareSessionService shareSessionService,
    ISharpClawConfigService sharpClawConfigService,
    IHookDispatcher hookDispatcher,
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

        try
        {
            if (request.HttpMethod == "GET" && path == "/v1/status")
            {
                var result = await runtimeCommandService.GetStatusAsync(defaultContext, cancellationToken).ConfigureAwait(false);
                statusCode = await WriteCommandResultAsync(response, result, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/doctor")
            {
                var result = await runtimeCommandService.RunDoctorAsync(defaultContext, cancellationToken).ConfigureAwait(false);
                statusCode = await WriteCommandResultAsync(response, result, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path == "/v1/sessions")
            {
                var result = await runtimeCommandService.ListSessionsAsync(defaultContext, cancellationToken).ConfigureAwait(false);
                statusCode = await WriteCommandResultAsync(response, result, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "GET" && path.StartsWith("/v1/sessions/", StringComparison.Ordinal))
            {
                var sessionId = Uri.UnescapeDataString(path["/v1/sessions/".Length..]);
                var result = await runtimeCommandService.InspectSessionAsync(sessionId, defaultContext, cancellationToken).ConfigureAwait(false);
                statusCode = await WriteCommandResultAsync(response, result, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "POST" && path == "/v1/prompt")
            {
                statusCode = await HandlePromptAsync(request, response, workspaceRoot, defaultContext, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "POST" && path.StartsWith("/v1/share/", StringComparison.Ordinal))
            {
                var sessionId = Uri.UnescapeDataString(path["/v1/share/".Length..]);
                var result = await runtimeCommandService.ShareSessionAsync(sessionId, defaultContext, cancellationToken).ConfigureAwait(false);
                statusCode = await WriteCommandResultAsync(response, result, cancellationToken).ConfigureAwait(false);
            }
            else if (request.HttpMethod == "DELETE" && path.StartsWith("/v1/share/", StringComparison.Ordinal))
            {
                var sessionId = Uri.UnescapeDataString(path["/v1/share/".Length..]);
                var result = await runtimeCommandService.UnshareSessionAsync(sessionId, defaultContext, cancellationToken).ConfigureAwait(false);
                statusCode = await WriteCommandResultAsync(response, result, cancellationToken).ConfigureAwait(false);
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
            IsInteractive: false);

        var result = await runtimeCommandService.ExecutePromptAsync(payload.Prompt, commandContext, cancellationToken).ConfigureAwait(false);
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
                        JsonSerializer.Serialize(runtimeEvent, runtimeEvent.GetType(), ProtocolJsonContext.Default))
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

    private static readonly JsonSerializerOptions ServerJsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

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
                DateTimeOffset.UtcNow));
        return hookDispatcher.DispatchAsync(workspaceRoot, HookTriggerKind.ServerRequestCompleted, payload, CancellationToken.None);
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
