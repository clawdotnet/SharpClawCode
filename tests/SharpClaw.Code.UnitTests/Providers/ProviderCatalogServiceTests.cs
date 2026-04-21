using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using SharpClaw.Code.Providers;
using SharpClaw.Code.Providers.Abstractions;
using SharpClaw.Code.Providers.Configuration;
using SharpClaw.Code.Providers.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.UnitTests.Providers;

/// <summary>
/// Covers provider catalog discovery for local runtime profiles.
/// </summary>
public sealed class ProviderCatalogServiceTests
{
    [Fact]
    public async Task ListAsync_should_surface_local_runtime_profiles_and_discovered_models()
    {
        await using var server = await LocalJsonServer.StartAsync("""
            {"data":[{"id":"qwen2.5-coder"},{"id":"nomic-embed-text"}]}
            """);

        var openAiOptions = new OpenAiCompatibleProviderOptions
        {
            ProviderName = "openai-compatible",
            DefaultModel = "gpt-4.1-mini",
            DefaultEmbeddingModel = "text-embedding-3-small",
            SupportsEmbeddings = true,
        };
        openAiOptions.LocalRuntimes["ollama"] = new LocalRuntimeProfileOptions
        {
            Kind = LocalRuntimeKind.Ollama,
            BaseUrl = $"{server.BaseUrl}v1/",
            DefaultChatModel = "qwen2.5-coder",
            DefaultEmbeddingModel = "nomic-embed-text",
            AuthMode = ProviderAuthMode.Optional,
            SupportsToolCalls = true,
            SupportsEmbeddings = true,
        };

        var service = new ProviderCatalogService(
            [new StubModelProvider("openai-compatible")],
            new StubAuthFlowService(),
            Options.Create(new ProviderCatalogOptions()),
            Options.Create(new AnthropicProviderOptions()),
            Options.Create(openAiOptions));

        var entries = await service.ListAsync(CancellationToken.None);

        entries.Should().ContainSingle();
        var entry = entries[0];
        entry.ProviderName.Should().Be("openai-compatible");
        entry.SupportsEmbeddings.Should().BeTrue();
        entry.AvailableModels.Should().Contain(model => model.Id == "qwen2.5-coder" && model.SupportsTools);
        entry.AvailableModels.Should().Contain(model => model.Id == "nomic-embed-text" && model.SupportsEmbeddings);
        entry.LocalRuntimeProfiles.Should().ContainSingle(profile =>
            profile.Name == "ollama"
            && profile.AuthMode == ProviderAuthMode.Optional
            && profile.IsHealthy
            && profile.AvailableModels.Length == 2);
    }

    private sealed class StubAuthFlowService : IAuthFlowService
    {
        public Task<AuthStatus> GetStatusAsync(string providerName, CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus(null, false, providerName, null, null, []));
    }

    private sealed class StubModelProvider(string providerName) : IModelProvider
    {
        public string ProviderName => providerName;

        public Task<AuthStatus> GetAuthStatusAsync(CancellationToken cancellationToken)
            => Task.FromResult(new AuthStatus(null, false, providerName, null, null, []));

        public Task<ProviderStreamHandle> StartStreamAsync(ProviderRequest request, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class LocalJsonServer(TcpListener listener, Task serverTask) : IAsyncDisposable
    {
        public string BaseUrl => $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";

        public static Task<LocalJsonServer> StartAsync(string jsonPayload)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            var payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);
            var serverTask = Task.Run(async () =>
            {
                using var client = await listener.AcceptTcpClientAsync();
                await using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

                while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
                {
                }

                var headers = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n"
                    + "Content-Type: application/json\r\n"
                    + $"Content-Length: {payloadBytes.Length}\r\n"
                    + "Connection: close\r\n\r\n");
                await stream.WriteAsync(headers);
                await stream.WriteAsync(payloadBytes);
                await stream.FlushAsync();
            });

            return Task.FromResult(new LocalJsonServer(listener, serverTask));
        }

        public async ValueTask DisposeAsync()
        {
            listener.Stop();
            try
            {
                await serverTask.ConfigureAwait(false);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }
}
