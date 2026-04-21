using FluentAssertions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Configuration;
using SharpClaw.Code.Runtime.Server;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>
/// Verifies trusted-header approval identity resolution and status reporting.
/// </summary>
public sealed class ConfiguredApprovalIdentityServiceTests : IDisposable
{
    private readonly string workspaceRoot = Path.Combine(Path.GetTempPath(), "sharpclaw-approval-auth", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Trusted_header_mode_should_map_subject_tenant_roles_and_scopes()
    {
        Directory.CreateDirectory(workspaceRoot);
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, "sharpclaw.jsonc"),
            """
            {
              "server": {
                "host": "127.0.0.1",
                "port": 7345,
                "approvalAuth": {
                  "mode": "trustedHeader",
                  "requireForAdmin": true,
                  "requireAuthenticatedApprovals": true
                }
              }
            }
            """);

        var service = new ConfiguredApprovalIdentityService(new SharpClawConfigService(new SharpClaw.Code.Infrastructure.Services.LocalFileSystem(), new SharpClaw.Code.Infrastructure.Services.PathService()));
        var status = await service.GetStatusAsync(workspaceRoot, CancellationToken.None);
        var principal = await service.ResolveAsync(
            workspaceRoot,
            new ApprovalIdentityRequest(
                AuthorizationHeader: null,
                Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["X-SharpClaw-User"] = "alice",
                    ["X-SharpClaw-Display-Name"] = "Alice Example",
                    ["X-SharpClaw-Tenant-Id"] = "tenant-a",
                    ["X-SharpClaw-Roles"] = "approver,admin",
                    ["X-SharpClaw-Scopes"] = "approvals:write approvals:read",
                }),
            new RuntimeHostContext("host-a", "tenant-a"),
            CancellationToken.None);

        status.Mode.Should().Be(ApprovalAuthMode.TrustedHeader);
        status.RequireForAdmin.Should().BeTrue();
        status.RequireAuthenticatedApprovals.Should().BeTrue();
        principal.Should().NotBeNull();
        principal!.SubjectId.Should().Be("alice");
        principal.DisplayName.Should().Be("Alice Example");
        principal.TenantId.Should().Be("tenant-a");
        principal.Roles.Should().BeEquivalentTo(["approver", "admin"]);
        principal.Scopes.Should().BeEquivalentTo(["approvals:write", "approvals:read"]);
        principal.AuthenticationType.Should().Be("trusted-header");
    }

    public void Dispose()
    {
        if (Directory.Exists(workspaceRoot))
        {
            Directory.Delete(workspaceRoot, recursive: true);
        }
    }
}
