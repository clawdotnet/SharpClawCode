using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.MockProvider;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Verifies the embeddable host SDK and tenant-aware SQLite session isolation.
/// </summary>
public sealed class EmbeddedRuntimeHostTests
{
    /// <summary>
    /// Ensures embedded hosts can partition durable SQLite session state by tenant.
    /// </summary>
    [Fact]
    public async Task Embedded_host_should_isolate_tenants_and_use_sqlite_session_store()
    {
        var workspaceRoot = CreateTemporaryDirectory("sharpclaw-embedded-workspace");
        var storageRoot = CreateTemporaryDirectory("sharpclaw-embedded-storage");

        await using var host = new SharpClawRuntimeHostBuilder()
            .Configure(builder => builder.Services.AddDeterministicMockModelProvider())
            .Build();
        await host.StartAsync();

        var tenantA = new RuntimeHostContext("embedded-host", "tenant-a", storageRoot, SessionStoreKind.Sqlite, true);
        var tenantB = new RuntimeHostContext("embedded-host", "tenant-b", storageRoot, SessionStoreKind.Sqlite, true);
        var contextA = new SharpClaw.Code.Runtime.Abstractions.RuntimeCommandContext(
            WorkingDirectory: workspaceRoot,
            Model: "default",
            PermissionMode: PermissionMode.WorkspaceWrite,
            OutputFormat: OutputFormat.Json,
            HostContext: tenantA);
        var contextB = contextA with { HostContext = tenantB };

        var first = await host.ExecutePromptAsync("inspect tenant A", contextA, CancellationToken.None);
        var second = await host.ExecutePromptAsync("inspect tenant B", contextB, CancellationToken.None);

        first.Session.Id.Should().NotBe(second.Session.Id);
        (await host.GetLatestSessionAsync(workspaceRoot, tenantA, CancellationToken.None)).Should().NotBeNull();
        (await host.GetLatestSessionAsync(workspaceRoot, tenantB, CancellationToken.None)).Should().NotBeNull();
        (await host.GetSessionAsync(workspaceRoot, first.Session.Id, tenantB, CancellationToken.None)).Should().BeNull();

        var storagePathResolver = host.Services.GetRequiredService<IRuntimeStoragePathResolver>();
        var hostContextAccessor = host.Services.GetRequiredService<IRuntimeHostContextAccessor>();

        string tenantADbPath;
        using (hostContextAccessor.BeginScope(tenantA))
        {
            tenantADbPath = storagePathResolver.GetSessionStoreDatabasePath(workspaceRoot);
        }

        string tenantBDbPath;
        using (hostContextAccessor.BeginScope(tenantB))
        {
            tenantBDbPath = storagePathResolver.GetSessionStoreDatabasePath(workspaceRoot);
        }

        File.Exists(tenantADbPath).Should().BeTrue();
        File.Exists(tenantBDbPath).Should().BeTrue();
        tenantADbPath.Should().NotBe(tenantBDbPath);
        tenantADbPath.Should().Contain("tenant-a");
        tenantBDbPath.Should().Contain("tenant-b");
    }

    private static string CreateTemporaryDirectory(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
