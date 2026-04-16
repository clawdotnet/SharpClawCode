using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.MockProvider;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Verifies the embedded admin HTTP surface for provider, index, package, and event inspection.
/// </summary>
public sealed class WorkspaceHttpServerAdminTests
{
    /// <summary>
    /// Ensures the admin server exposes provider catalog, index, package, and recent-event payloads.
    /// </summary>
    [Fact]
    public async Task Admin_endpoints_should_expose_provider_index_package_and_event_data()
    {
        var workspaceRoot = CreateTemporaryWorkspace();
        await File.WriteAllTextAsync(Path.Combine(workspaceRoot, "README.md"), "Workspace admin search content.");

        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddDeterministicMockModelProvider();
        using var serviceProvider = services.BuildServiceProvider();

        var server = serviceProvider.GetRequiredService<IWorkspaceHttpServer>();
        var port = FindFreePort();
        using var serverCts = new CancellationTokenSource();
        var serverTask = server.RunAsync(
            workspaceRoot,
            "127.0.0.1",
            port,
            new SharpClaw.Code.Runtime.Abstractions.RuntimeCommandContext(
                WorkingDirectory: workspaceRoot,
                Model: "default",
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Json),
            serverCts.Token);

        try
        {
            using var httpClient = new HttpClient
            {
                BaseAddress = new Uri($"http://127.0.0.1:{port}/"),
                Timeout = TimeSpan.FromSeconds(10),
            };
            await WaitForServerAsync(httpClient, CancellationToken.None);

            var providersJson = await httpClient.GetStringAsync("v1/admin/providers");
            providersJson.Should().Contain("mock");

            using var refreshResponse = await httpClient.PostAsync("v1/admin/index/refresh", new StringContent(string.Empty), CancellationToken.None);
            refreshResponse.EnsureSuccessStatusCode();
            var refresh = JsonSerializer.Deserialize(
                await refreshResponse.Content.ReadAsStringAsync(),
                ProtocolJsonContext.Default.WorkspaceIndexRefreshResult);
            refresh.Should().NotBeNull();
            refresh!.IndexedFileCount.Should().BeGreaterThan(0);

            var installRequest = new ToolPackageInstallRequest(
                new ToolPackageManifest(
                    new ToolPackageReference("acme.echo", "1.0.0", "local", "echo-tool"),
                    "acme",
                    "Echo tools",
                    [new PackagedToolDescriptor("echo_tool", "Echoes content", """{"type":"object"}""")]),
                InstallSource: "unit-test",
                EnableAfterInstall: false);
            using var installResponse = await httpClient.PostAsync(
                "v1/admin/tool-packages/install",
                new StringContent(JsonSerializer.Serialize(installRequest, ProtocolJsonContext.Default.ToolPackageInstallRequest), Encoding.UTF8, "application/json"),
                CancellationToken.None);
            installResponse.EnsureSuccessStatusCode();
            var installed = JsonSerializer.Deserialize(
                await installResponse.Content.ReadAsStringAsync(),
                ProtocolJsonContext.Default.InstalledToolPackage);
            installed.Should().NotBeNull();
            installed!.Manifest.Package.PackageId.Should().Be("acme.echo");

            var packageListJson = await httpClient.GetStringAsync("v1/admin/tool-packages");
            packageListJson.Should().Contain("acme.echo");

            using var promptResponse = await httpClient.PostAsync(
                "v1/prompt",
                new StringContent("""{"prompt":"run the admin server flow","model":"default"}""", Encoding.UTF8, "application/json"),
                CancellationToken.None);
            promptResponse.EnsureSuccessStatusCode();
            var promptResult = JsonSerializer.Deserialize(
                await promptResponse.Content.ReadAsStringAsync(),
                ProtocolJsonContext.Default.TurnExecutionResult);
            promptResult.Should().NotBeNull();

            var events = JsonSerializer.Deserialize(
                await httpClient.GetStringAsync("v1/admin/events/recent"),
                ProtocolJsonContext.Default.ListRuntimeEventEnvelope);
            events.Should().NotBeNull();
            events!.Should().Contain(envelope => envelope.EventType == nameof(SharpClaw.Code.Protocol.Events.TurnCompletedEvent));
        }
        finally
        {
            serverCts.Cancel();
            await serverTask;
        }
    }

    private static async Task WaitForServerAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var attempts = 0;
        while (attempts++ < 20)
        {
            try
            {
                using var response = await httpClient.GetAsync("v1/status", cancellationToken).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        throw new TimeoutException("Embedded workspace HTTP server did not become ready.");
    }

    private static int FindFreePort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string CreateTemporaryWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "sharpclaw-admin-server", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
