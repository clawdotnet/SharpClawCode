using System.CommandLine;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Commands.Options;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Commands;

/// <summary>
/// Hosts a local IDE/editor bridge: JSON lines over TCP, Unix domain socket, or a Windows named pipe.
/// </summary>
public sealed class BridgeCommandHandler(
    IEditorContextBuffer editorContextBuffer,
    IPathService pathService,
    IFileSystem fileSystem,
    ILogger<BridgeCommandHandler> logger)
    : ICommandHandler
{
    private const int DefaultBridgePort = 17337;
    private readonly object clientTasksLock = new();
    private readonly HashSet<Task> clientTasks = [];

    /// <inheritdoc />
    public string Name => "bridge";

    /// <inheritdoc />
    public string Description =>
        "Runs a local IDE bridge (JSON lines): TCP loopback, Unix socket, or Windows named pipe.";

    /// <inheritdoc />
    public Command BuildCommand(GlobalCliOptions globalOptions)
    {
        var cmd = new Command(Name, Description);
        var listen = new Command("listen", "Listens for editor context JSON (one EditorContextPayload per line).");
        var port = new Option<int>("--port")
        {
            Description = "TCP port on 127.0.0.1 (default transport when --unix-socket and --pipe are omitted). Defaults to SHARPCLAW_BRIDGE_PORT or 17337.",
            DefaultValueFactory = _ => ResolveDefaultPort(),
        };
        var unixSocket = new Option<string?>("--unix-socket")
        {
            Description = "Unix domain socket path (stream). Overrides TCP when set.",
        };
        var pipeName = new Option<string?>("--pipe")
        {
            Description = "Windows named pipe name (e.g. SharpClawBridge). Overrides TCP when set; Windows only.",
        };
        listen.Options.Add(port);
        listen.Options.Add(unixSocket);
        listen.Options.Add(pipeName);
        listen.SetAction(async (parseResult, cancellationToken) =>
        {
            var p = parseResult.GetValue(port);
            var udsPath = parseResult.GetValue(unixSocket);
            var pipe = parseResult.GetValue(pipeName);

            if (!string.IsNullOrWhiteSpace(udsPath) && !string.IsNullOrWhiteSpace(pipe))
            {
                logger.LogError("Specify either --unix-socket or --pipe, not both.");
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(pipe) && !OperatingSystem.IsWindows())
            {
                logger.LogError("Named pipe bridge is only supported on Windows; use --unix-socket on this platform.");
                return 1;
            }

            if (!string.IsNullOrWhiteSpace(udsPath))
            {
                return await RunUnixSocketListenerAsync(pathService.GetFullPath(udsPath), cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!string.IsNullOrWhiteSpace(pipe))
            {
                return await RunNamedPipeListenerAsync(pipe.Trim(), cancellationToken).ConfigureAwait(false);
            }

            return await RunTcpListenerAsync(p, cancellationToken).ConfigureAwait(false);
        });
        cmd.Subcommands.Add(listen);
        return cmd;
    }

    private static int ResolveDefaultPort()
    {
        var raw = Environment.GetEnvironmentVariable("SHARPCLAW_BRIDGE_PORT");
        return int.TryParse(raw, out var parsed) && parsed is > 0 and < 65536
            ? parsed
            : DefaultBridgePort;
    }

    private void TrackClientTask(Func<Task> work, CancellationToken cancellationToken)
    {
        var task = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Bridge client handler faulted.");
            }
        }, cancellationToken);

        lock (clientTasksLock)
        {
            clientTasks.Add(task);
        }

        _ = task.ContinueWith(t =>
        {
            lock (clientTasksLock)
            {
                clientTasks.Remove(t);
            }
        }, TaskScheduler.Default);
    }

    private async Task DrainClientTasksAsync()
    {
        Task[] snapshot;
        lock (clientTasksLock)
        {
            snapshot = clientTasks.ToArray();
        }

        if (snapshot.Length > 0)
        {
            try
            {
                await Task.WhenAll(snapshot).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Aggregate/Task.WhenAll may wrap faults; individual handler faults already logged in TrackClientTask.
            }
        }
    }

    private async Task<int> RunTcpListenerAsync(int port, CancellationToken cancellationToken)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        logger.LogInformation(
            "SharpClaw bridge listening on 127.0.0.1:{Port} (TCP). Send JSON lines (EditorContextPayload).",
            port);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                TrackClientTask(() => HandleTcpClientAsync(client, cancellationToken), cancellationToken);
            }
        }
        finally
        {
            listener.Stop();
            await DrainClientTasksAsync().ConfigureAwait(false);
        }

        return 0;
    }

    private async Task HandleTcpClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = client.GetStream();
            await HandleJsonLinesAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            client.Dispose();
        }
    }

    private async Task<int> RunUnixSocketListenerAsync(string socketPath, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(socketPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            fileSystem.CreateDirectory(directory);
        }

        if (fileSystem.FileExists(socketPath))
        {
            fileSystem.TryDeleteFile(socketPath);
        }

        using var listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listenSocket.Bind(new UnixDomainSocketEndPoint(socketPath));
        listenSocket.Listen(128);
        logger.LogInformation(
            "SharpClaw bridge listening on unix socket {Path}. Send JSON lines (EditorContextPayload).",
            socketPath);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var client = await listenSocket.AcceptAsync(cancellationToken).ConfigureAwait(false);
                TrackClientTask(() => HandleUnixClientAsync(client, cancellationToken), cancellationToken);
            }
        }
        finally
        {
            listenSocket.Dispose();
            if (fileSystem.FileExists(socketPath))
            {
                fileSystem.TryDeleteFile(socketPath);
            }

            await DrainClientTasksAsync().ConfigureAwait(false);
        }

        return 0;
    }

    private async Task HandleUnixClientAsync(Socket client, CancellationToken cancellationToken)
    {
        await using var stream = new NetworkStream(client, ownsSocket: true);
        await HandleJsonLinesAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private async Task<int> RunNamedPipeListenerAsync(string pipeName, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "SharpClaw bridge listening on named pipe '{Pipe}'. Send JSON lines (EditorContextPayload).",
            pipeName);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // One server instance per connected client; new instances are created after accept.
                var server = new NamedPipeServerStream(
                    pipeName,
                    PipeDirection.In,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                var captured = server;
                TrackClientTask(
                    () => HandleNamedPipeClientAsync(captured, cancellationToken),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            await DrainClientTasksAsync().ConfigureAwait(false);
        }

        return 0;
    }

    private async Task HandleNamedPipeClientAsync(Stream stream, CancellationToken cancellationToken)
    {
        try
        {
            await HandleJsonLinesAsync(stream, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await stream.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task HandleJsonLinesAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                var payload = JsonSerializer.Deserialize(line, ProtocolJsonContext.Default.EditorContextPayload);
                if (payload is null)
                {
                    logger.LogWarning("Bridge received payload that did not deserialize.");
                    continue;
                }

                var workspaceFull = pathService.GetFullPath(payload.WorkspaceRoot);
                editorContextBuffer.Publish(payload with { WorkspaceRoot = workspaceFull });
                logger.LogInformation("Bridge ingested editor context for workspace {Workspace}.", workspaceFull);
            }
            catch (JsonException ex)
            {
                logger.LogWarning(ex, "Bridge received invalid JSON.");
            }
        }
    }
}
