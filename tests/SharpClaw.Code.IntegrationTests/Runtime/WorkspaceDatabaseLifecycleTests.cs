using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Telemetry.Abstractions;

namespace SharpClaw.Code.IntegrationTests.Runtime;

/// <summary>Verifies database handles follow the host lifecycle, including Windows file sharing rules.</summary>
public sealed class WorkspaceDatabaseLifecycleTests
{
    /// <summary>A disposed host releases its knowledge and metering databases for exclusive access and workspace removal.</summary>
    [Fact]
    public async Task Host_disposal_releases_knowledge_and_usage_database_handles()
    {
        var root = Path.Combine(Path.GetTempPath(), "sharpclaw-db-lifecycle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "Source.cs"), "public class Source { }");
            using (var provider = new ServiceCollection().AddSharpClawRuntime().BuildServiceProvider())
            {
                await provider.GetRequiredService<IWorkspaceIndexService>().RefreshAsync(root, CancellationToken.None);
                await provider.GetRequiredService<IUsageMeteringStore>().GetSummaryAsync(root, new(), CancellationToken.None);
            }
            var databases = Directory.GetFiles(root, "*.db", SearchOption.AllDirectories);
            databases.Should().HaveCount(2);
            foreach (var database in databases)
            {
                using var exclusive = new FileStream(database, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
                exclusive.Length.Should().BeGreaterThan(0);
            }
            Directory.Delete(root, recursive: true);
            Directory.Exists(root).Should().BeFalse();
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }
}
