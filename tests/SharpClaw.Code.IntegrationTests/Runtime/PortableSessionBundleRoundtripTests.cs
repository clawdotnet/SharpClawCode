using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>
/// Validates portable bundle export and import across workspaces.
/// </summary>
public sealed class PortableSessionBundleRoundtripTests
{
    /// <summary>
    /// Export from one workspace and import into another should produce a readable session snapshot.
    /// </summary>
    [Fact]
    public async Task Export_then_import_should_recover_session_in_new_workspace()
    {
        var sourceWorkspace = CreateTemporaryWorkspace();
        var targetWorkspace = CreateTemporaryWorkspace();
        using var serviceProvider = CreateServiceProvider();

        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();
        var bundleSvc = serviceProvider.GetRequiredService<IPortableSessionBundleService>();
        var sessionStore = serviceProvider.GetRequiredService<ISessionStore>();

        var run = await runtime.RunPromptAsync(
            new RunPromptRequest(
                "bundle me",
                SessionId: null,
                WorkingDirectory: sourceWorkspace,
                PermissionMode: PermissionMode.WorkspaceWrite,
                OutputFormat: OutputFormat.Text,
                Metadata: null),
            CancellationToken.None);

        var zipPath = await bundleSvc.CreateBundleZipAsync(sourceWorkspace, run.Session.Id, null, CancellationToken.None);
        File.Exists(zipPath).Should().BeTrue();

        var imported = await bundleSvc.ImportBundleZipAsync(targetWorkspace, zipPath, replaceExisting: false, CancellationToken.None);

        imported.SessionId.Should().Be(run.Session.Id);
        var copy = await sessionStore.GetByIdAsync(targetWorkspace, run.Session.Id, CancellationToken.None);
        copy.Should().NotBeNull();
        copy!.Id.Should().Be(run.Session.Id);
    }

    /// <summary>
    /// Importing the same session id twice without replace should fail.
    /// </summary>
    [Fact]
    public async Task Import_without_replace_when_colliding_should_fail()
    {
        var workspace = CreateTemporaryWorkspace();
        using var serviceProvider = CreateServiceProvider();
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();
        var bundleSvc = serviceProvider.GetRequiredService<IPortableSessionBundleService>();

        var run = await runtime.RunPromptAsync(
            new RunPromptRequest("one", null, workspace, PermissionMode.WorkspaceWrite, OutputFormat.Text, null),
            CancellationToken.None);
        var zip = await bundleSvc.CreateBundleZipAsync(workspace, run.Session.Id, null, CancellationToken.None);

        await bundleSvc
            .Invoking(b => b.ImportBundleZipAsync(workspace, zip, replaceExisting: false, CancellationToken.None))
            .Should()
            .ThrowAsync<InvalidOperationException>();
    }

    /// <summary>
    /// Replace should allow re-import over an existing session directory.
    /// </summary>
    [Fact]
    public async Task Import_with_replace_should_overwrite_session()
    {
        var workspace = CreateTemporaryWorkspace();
        using var serviceProvider = CreateServiceProvider();
        var runtime = serviceProvider.GetRequiredService<IConversationRuntime>();
        var bundleSvc = serviceProvider.GetRequiredService<IPortableSessionBundleService>();
        var store = serviceProvider.GetRequiredService<ISessionStore>();

        var run = await runtime.RunPromptAsync(
            new RunPromptRequest("seed", null, workspace, PermissionMode.WorkspaceWrite, OutputFormat.Text, null),
            CancellationToken.None);
        var zip = await bundleSvc.CreateBundleZipAsync(workspace, run.Session.Id, null, CancellationToken.None);

        await runtime.RunPromptAsync(
            new RunPromptRequest("mutate", run.Session.Id, workspace, PermissionMode.WorkspaceWrite, OutputFormat.Text, null),
            CancellationToken.None);

        await bundleSvc.ImportBundleZipAsync(workspace, zip, replaceExisting: true, CancellationToken.None);

        var session = await store.GetByIdAsync(workspace, run.Session.Id, CancellationToken.None);
        session.Should().NotBeNull();
    }

    private static ServiceProvider CreateServiceProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static string CreateTemporaryWorkspace()
    {
        var workspacePath = Path.Combine(Path.GetTempPath(), "sharpclaw-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workspacePath);
        return workspacePath;
    }
}
