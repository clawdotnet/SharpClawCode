using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SharpClaw.Code.MockProvider;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Verifies OIDC-backed admin auth and approval enforcement for the embedded HTTP server.
/// </summary>
public sealed class ApprovalAuthIntegrationTests
{
    /// <summary>
    /// Ensures the embedded HTTP server enforces OIDC identity for admin routes and approval-gated prompt references.
    /// </summary>
    [Fact]
    public async Task Embedded_http_server_should_enforce_oidc_admin_and_prompt_approval_auth()
    {
        await using var authority = new TestOidcAuthority();
        var workspaceRoot = CreateTemporaryWorkspace();
        var outsideFile = Path.Combine(Path.GetTempPath(), "sharpclaw-approval-auth-targets", Guid.NewGuid().ToString("N"), "secret.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(outsideFile)!);
        await File.WriteAllTextAsync(outsideFile, "secret outside the workspace");
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, "sharpclaw.jsonc"),
            $$"""
            {
              "server": {
                "host": "127.0.0.1",
                "port": 7345,
                "approvalAuth": {
                  "mode": "oidc",
                  "authority": "{{authority.AuthorityUrl}}",
                  "audience": "{{authority.Audience}}",
                  "requireForAdmin": true,
                  "requireAuthenticatedApprovals": true
                }
              }
            }
            """);

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
            new RuntimeCommandContext(
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

            using var unauthenticatedAdminResponse = await httpClient.GetAsync("v1/admin/providers", CancellationToken.None);
            unauthenticatedAdminResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

            using var authenticatedAdminRequest = new HttpRequestMessage(HttpMethod.Get, "v1/admin/providers");
            authenticatedAdminRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authority.CreateToken("alice", "tenant-a"));
            using var authenticatedAdminResponse = await httpClient.SendAsync(authenticatedAdminRequest, CancellationToken.None);
            authenticatedAdminResponse.EnsureSuccessStatusCode();

            using var missingApprovalIdentityRequest = BuildPromptRequest(outsideFile, tenantId: "tenant-a");
            using var missingApprovalIdentityResponse = await httpClient.SendAsync(missingApprovalIdentityRequest, CancellationToken.None);
            missingApprovalIdentityResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await ReadErrorAsync(missingApprovalIdentityResponse)).Should().Contain("Authenticated approval is required");

            using var approvedPromptRequest = BuildPromptRequest(outsideFile, tenantId: "tenant-a");
            approvedPromptRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authority.CreateToken("alice", "tenant-a"));
            using var approvedPromptResponse = await httpClient.SendAsync(approvedPromptRequest, CancellationToken.None);
            approvedPromptResponse.EnsureSuccessStatusCode();

            using var wrongTenantPromptRequest = BuildPromptRequest(outsideFile, tenantId: "tenant-b");
            wrongTenantPromptRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", authority.CreateToken("alice", "tenant-a"));
            using var wrongTenantPromptResponse = await httpClient.SendAsync(wrongTenantPromptRequest, CancellationToken.None);
            wrongTenantPromptResponse.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
            (await ReadErrorAsync(wrongTenantPromptResponse)).Should().Contain("does not match runtime tenant");
        }
        finally
        {
            serverCts.Cancel();
            await serverTask;
        }
    }

    private static HttpRequestMessage BuildPromptRequest(string outsideFile, string tenantId)
    {
        var payload = new ServerPromptRequest(
            Prompt: $"inspect @{outsideFile}",
            SessionId: null,
            Model: "default",
            PermissionMode: PermissionMode.WorkspaceWrite,
            OutputFormat: OutputFormat.Json,
            PrimaryMode: PrimaryMode.Build,
            AgentId: null,
            TenantId: tenantId);

        return new HttpRequestMessage(HttpMethod.Post, "v1/prompt")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, ProtocolJsonContext.Default.ServerPromptRequest),
                Encoding.UTF8,
                "application/json"),
        };
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(content);
        return document.RootElement.TryGetProperty("error", out var error)
            ? error.GetString() ?? string.Empty
            : content;
    }

    private static async Task WaitForServerAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 30; attempt++)
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
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string CreateTemporaryWorkspace()
    {
        var path = Path.Combine(Path.GetTempPath(), "sharpclaw-approval-auth-server", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestOidcAuthority : IAsyncDisposable
    {
        private readonly HttpListener listener = new();
        private readonly CancellationTokenSource cancellationTokenSource = new();
        private readonly RSA rsa = RSA.Create(2048);
        private readonly Task serverTask;
        private readonly RsaSecurityKey signingKey;

        public TestOidcAuthority()
        {
            var port = FindFreePort();
            AuthorityUrl = $"http://127.0.0.1:{port}";
            Audience = "sharpclaw-tests";
            signingKey = new RsaSecurityKey(rsa) { KeyId = Guid.NewGuid().ToString("N") };

            listener.Prefixes.Add($"{AuthorityUrl}/");
            listener.Start();
            serverTask = Task.Run(() => RunAsync(cancellationTokenSource.Token));
        }

        public string AuthorityUrl { get; }

        public string Audience { get; }

        public string CreateToken(string subjectId, string tenantId)
        {
            var claims = new List<Claim>
            {
                new("sub", subjectId),
                new("name", $"User {subjectId}"),
                new("tid", tenantId),
                new("scope", "approvals:write approvals:read"),
                new("role", "approver"),
            };

            var descriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Audience = Audience,
                Issuer = AuthorityUrl,
                Expires = DateTime.UtcNow.AddMinutes(10),
                NotBefore = DateTime.UtcNow.AddMinutes(-1),
                SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
            };

            return new JsonWebTokenHandler().CreateToken(descriptor);
        }

        public async ValueTask DisposeAsync()
        {
            cancellationTokenSource.Cancel();
            if (listener.IsListening)
            {
                listener.Stop();
            }

            try
            {
                await serverTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (HttpListenerException)
            {
            }

            listener.Close();
            rsa.Dispose();
            cancellationTokenSource.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                _ = Task.Run(() => HandleAsync(context, cancellationToken), CancellationToken.None);
            }
        }

        private async Task HandleAsync(HttpListenerContext context, CancellationToken cancellationToken)
        {
            try
            {
                var path = context.Request.Url?.AbsolutePath ?? "/";
                object payload = path switch
                {
                    "/.well-known/openid-configuration" => new
                    {
                        issuer = AuthorityUrl,
                        jwks_uri = $"{AuthorityUrl}/.well-known/jwks.json",
                    },
                    "/.well-known/jwks.json" => new
                    {
                        keys = new[]
                        {
                            new
                            {
                                kty = "RSA",
                                use = "sig",
                                kid = signingKey.KeyId,
                                alg = "RS256",
                                n = Base64UrlEncoder.Encode(rsa.ExportParameters(false).Modulus!),
                                e = Base64UrlEncoder.Encode(rsa.ExportParameters(false).Exponent!),
                            }
                        }
                    },
                    _ => new { error = "not-found" }
                };

                var statusCode = path is "/.well-known/openid-configuration" or "/.well-known/jwks.json" ? 200 : 404;
                context.Response.StatusCode = statusCode;
                context.Response.ContentType = "application/json";
                await using var writer = new StreamWriter(context.Response.OutputStream, new UTF8Encoding(false), leaveOpen: true);
                await writer.WriteAsync(JsonSerializer.Serialize(payload).AsMemory(), cancellationToken).ConfigureAwait(false);
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                context.Response.Close();
            }
        }
    }
}
