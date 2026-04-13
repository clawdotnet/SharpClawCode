using FluentAssertions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Configuration;

namespace SharpClaw.Code.UnitTests.Runtime;

public sealed class SharpClawConfigServiceTests : IDisposable
{
    private readonly string? originalHome = Environment.GetEnvironmentVariable("HOME");
    private readonly string? originalUserProfile = Environment.GetEnvironmentVariable("USERPROFILE");
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"sharpclaw-config-{Guid.NewGuid():N}");

    [Fact]
    public async Task GetConfigAsync_merges_user_and_workspace_documents_by_precedence()
    {
        Directory.CreateDirectory(tempRoot);
        var home = Path.Combine(tempRoot, "home");
        var workspace = Path.Combine(tempRoot, "workspace");
        Directory.CreateDirectory(Path.Combine(home, ".config", "sharpclaw"));
        Directory.CreateDirectory(workspace);

        Environment.SetEnvironmentVariable("HOME", home);
        Environment.SetEnvironmentVariable("USERPROFILE", home);

        await File.WriteAllTextAsync(
            Path.Combine(home, ".config", "sharpclaw", "config.jsonc"),
            """
            {
              // user defaults
              "shareMode": "Auto",
              "defaultAgentId": "user-agent",
              "agents": [
                {
                  "id": "shared-agent",
                  "name": "User Shared",
                  "description": "from user",
                  "baseAgentId": "primary-coding-agent",
                  "model": "gpt-user"
                }
              ],
              "connectLinks": [
                { "target": "anthropic", "displayName": "Anthropic", "url": "https://example.test/anthropic" }
              ]
            }
            """);

        await File.WriteAllTextAsync(
            Path.Combine(workspace, "sharpclaw.jsonc"),
            """
            {
              "shareMode": "Disabled",
              "defaultAgentId": "workspace-agent",
              "agents": [
                {
                  "id": "shared-agent",
                  "name": "Workspace Shared",
                  "description": "from workspace",
                  "baseAgentId": "primary-coding-agent",
                  "model": "gpt-workspace"
                },
                {
                  "id": "workspace-only",
                  "name": "Workspace Only",
                  "description": "workspace only",
                  "baseAgentId": "primary-coding-agent"
                }
              ]
            }
            """);

        var service = new SharpClawConfigService(new LocalFileSystem(), new PathService());

        var snapshot = await service.GetConfigAsync(workspace, CancellationToken.None);

        snapshot.Document.ShareMode.Should().Be(ShareMode.Disabled);
        snapshot.Document.DefaultAgentId.Should().Be("workspace-agent");
        snapshot.Document.ConnectLinks.Should().ContainSingle(link => link.Target == "anthropic");
        snapshot.Document.Agents.Should().NotBeNull();
        snapshot.Document.Agents!.Should().HaveCount(2);
        snapshot.Document.Agents.Should().Contain(agent => agent.Id == "workspace-only");
        snapshot.Document.Agents.Should().Contain(agent => agent.Id == "shared-agent" && agent.Model == "gpt-workspace" && agent.Name == "Workspace Shared");
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("HOME", originalHome);
        Environment.SetEnvironmentVariable("USERPROFILE", originalUserProfile);
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
