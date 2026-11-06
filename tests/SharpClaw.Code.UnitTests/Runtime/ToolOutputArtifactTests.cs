using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Agents.Abstractions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Configuration;
using SharpClaw.Code.Sessions.Abstractions;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.UnitTests.Runtime;

public sealed class ToolOutputArtifactTests : IDisposable
{
    private readonly string workspace = Path.Combine(Path.GetTempPath(), "sharpclaw-artifacts-" + Guid.NewGuid().ToString("N"));
    private ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.Configure<ToolOutputOptions>(options => options.MaxCharacters = 2048);
        return services.BuildServiceProvider();
    }
    private ToolExecutionContext Context(string session = "session") => new(session, "turn", workspace, workspace, PermissionMode.ReadOnly, OutputFormat.Text, null, IsInteractive: false);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Full_output_survives_restart_and_preview_preserves_tool_identity_and_error(bool isError)
    {
        Directory.CreateDirectory(workspace);
        var content = "start" + new string('x', 10_000) + "最後😀end";
        var block = new ContentBlock(ContentBlockKind.ToolResult, content, "call", null, null, isError);
        string artifactId;
        using (var services = Services())
        {
            var preview = await services.GetRequiredService<IToolResultContextFormatter>().PrepareAsync(block, "read_file", Context(), CancellationToken.None);
            preview.Text!.Length.Should().BeLessThanOrEqualTo(2048);
            preview.Text.Should().StartWith("start").And.EndWith("最後😀end");
            preview.ToolUseId.Should().Be("call");
            preview.IsError.Should().Be(isError);
            block.Text.Should().Be(content);
            artifactId = preview.Text.Split("artifactId=\"")[1].Split('"')[0];
        }
        using var restarted = Services();
        (await restarted.GetRequiredService<IToolOutputArtifactStore>().GetAsync(workspace, "session", artifactId, CancellationToken.None))!.Content.Should().Be(content);
        var executor = restarted.GetRequiredService<IToolExecutor>();
        var result = await executor.ExecuteAsync("read_tool_output", JsonSerializer.Serialize(new { artifactId, offset = 10000, limit = 100 }), Context(), CancellationToken.None);
        result.Result.Succeeded.Should().BeTrue(result.Result.ErrorMessage);
        result.Result.Output.Should().Contain("最後😀end");
        var denied = await executor.ExecuteAsync("read_tool_output", JsonSerializer.Serialize(new { artifactId }), Context() with { AllowedTools = ["read_file"] }, CancellationToken.None);
        denied.PermissionDecision.IsAllowed.Should().BeFalse();
    }

    [Fact]
    public async Task Artifacts_are_partitioned_by_session_and_tenant_and_reject_path_identifiers()
    {
        Directory.CreateDirectory(workspace);
        using var services = Services();
        var store = services.GetRequiredService<IToolOutputArtifactStore>();
        var hosts = services.GetRequiredService<IRuntimeHostContextAccessor>();
        var tenant = new RuntimeHostContext("host", "tenant-a", Path.Combine(workspace, "storage"), SessionStoreKind.FileSystem, true);
        ToolOutputArtifact artifact;
        using (hosts.BeginScope(tenant))
        {
            artifact = await store.SaveAsync(workspace, "session", "turn", "bash", "private output", CancellationToken.None);
            (await store.GetAsync(workspace, "other-session", artifact.Id, CancellationToken.None)).Should().BeNull();
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync(workspace, "session", "../session.json", CancellationToken.None));
            await Assert.ThrowsAsync<ArgumentException>(() => store.GetAsync(workspace, "../session", artifact.Id, CancellationToken.None));
        }
        using (hosts.BeginScope(tenant with { TenantId = "tenant-b" }))
            (await store.GetAsync(workspace, "session", artifact.Id, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Retrieval_respects_preview_budget_even_with_JSON_escaping()
    {
        Directory.CreateDirectory(workspace);
        using var services = Services();
        var artifact = await services.GetRequiredService<IToolOutputArtifactStore>().SaveAsync(workspace, "session", "turn", "read_file", new string('\u0001', 10_000), CancellationToken.None);
        var result = await services.GetRequiredService<IToolExecutor>().ExecuteAsync("read_tool_output", JsonSerializer.Serialize(new { artifactId = artifact.Id }), Context() with { OutputFormat = OutputFormat.Json }, CancellationToken.None);
        result.Result.Succeeded.Should().BeTrue(result.Result.ErrorMessage);
        result.Result.Output!.Length.Should().BeLessThanOrEqualTo(2048);
        var block = new ContentBlock(ContentBlockKind.ToolResult, result.Result.Output, "call", null, null, null);
        (await services.GetRequiredService<IToolResultContextFormatter>().PrepareAsync(block, "read_tool_output", Context(), CancellationToken.None)).Should().BeSameAs(block);
    }

    [Fact]
    public async Task Paginated_retrieval_preserves_surrogate_pairs_and_reports_the_next_offset()
    {
        Directory.CreateDirectory(workspace);
        using var services = Services();
        var artifact = await services.GetRequiredService<IToolOutputArtifactStore>().SaveAsync(workspace, "session", "turn", "read_file", "a😀b最後", CancellationToken.None);
        var executor = services.GetRequiredService<IToolExecutor>();
        var output = new System.Text.StringBuilder();
        int? offset = 0;
        while (offset is not null)
        {
            var result = await executor.ExecuteAsync("read_tool_output", JsonSerializer.Serialize(new { artifactId = artifact.Id, offset, limit = 1 }), Context(), CancellationToken.None);
            result.Result.Succeeded.Should().BeTrue(result.Result.ErrorMessage);
            var page = JsonSerializer.Deserialize<ToolOutputArtifactPage>(result.Result.StructuredOutputJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            output.Append(page.Content);
            offset = page.NextOffset;
        }
        output.ToString().Should().Be(artifact.Content);
    }

    [Fact]
    public async Task Short_output_creates_no_artifact_and_cancelled_write_leaves_no_partial_file()
    {
        Directory.CreateDirectory(workspace);
        using var services = Services();
        var block = new ContentBlock(ContentBlockKind.ToolResult, "small", "call", null, null, null);
        (await services.GetRequiredService<IToolResultContextFormatter>().PrepareAsync(block, "read_file", Context(), CancellationToken.None)).Should().BeSameAs(block);
        Directory.GetFiles(workspace, "*", SearchOption.AllDirectories).Should().BeEmpty();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => services.GetRequiredService<IToolOutputArtifactStore>().SaveAsync(workspace, "session", "turn", "read_file", "large", cancellation.Token));
        Directory.GetFiles(workspace, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    public void Dispose() { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
}
