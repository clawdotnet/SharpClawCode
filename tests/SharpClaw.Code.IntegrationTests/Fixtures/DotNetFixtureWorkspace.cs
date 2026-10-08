using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Memory.Services;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.IntegrationTests.Fixtures;

/// <summary>Copies the checked-in fixture and explicitly provisions assets using a local empty package feed.</summary>
internal sealed class DotNetFixtureWorkspace : IDisposable
{
    internal string Root { get; } = Path.Combine(Path.GetTempPath(), "sharpclaw-dotnet-fixture-" + Guid.NewGuid().ToString("N"));
    internal DotNetWorkspaceSemanticService Service { get; }
    internal RecordingAuthorization Authorization { get; } = new();

    private DotNetFixtureWorkspace()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "DotNetFixture");
        foreach (var path in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var destination = Path.Combine(Root, Path.GetRelativePath(source, path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(path, destination);
        }
        var fileSystem = new LocalFileSystem();
        var pathService = new PathService();
        Service = new DotNetWorkspaceSemanticService(fileSystem, pathService, new DotNetWorkspaceTargetResolver(fileSystem, pathService));
    }

    internal static async Task<DotNetFixtureWorkspace> CreateAsync(bool includeTests = false)
    {
        var fixture = new DotNetFixtureWorkspace();
        try
        {
            if (includeTests)
            {
                var solution = Path.Combine(fixture.Root, "Fixture.slnx");
                await File.WriteAllTextAsync(solution, (await File.ReadAllTextAsync(solution)).Replace("</Solution>", "  <Project Path=\"Fixture.Tests/Fixture.Tests.csproj\" />\n</Solution>", StringComparison.Ordinal));
            }
            var feed = Path.Combine(fixture.Root, "empty-feed");
            Directory.CreateDirectory(feed);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var result = await new ProcessRunner(new SystemClock()).RunAsync(new ProcessRunRequest("dotnet", ["restore", "Fixture.slnx", "--source", feed, "-p:NuGetAudit=false", "--disable-build-servers", "-m:1", "-nodeReuse:false"], fixture.Root, null), timeout.Token);
            if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardOutput + result.StandardError);
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    public void Dispose()
    {
        Service.Dispose();
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }

    internal sealed class RecordingAuthorization : IDotNetWorkspaceEvaluationAuthorization
    {
        internal int Calls { get; private set; }
        internal bool Allowed { get; set; } = true;
        public Task<bool> AuthorizeAsync(DotNetWorkspaceRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.FromResult(Allowed);
        }
    }
}
