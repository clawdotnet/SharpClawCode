using FluentAssertions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Services;

namespace SharpClaw.Code.UnitTests.Plugins;

public sealed class PluginManifestImportServiceTests : IDisposable
{
    private readonly string tempRoot = Path.Combine(Path.GetTempPath(), $"sharpclaw-plugin-import-{Guid.NewGuid():N}");

    [Fact]
    public async Task ImportAsync_should_adapt_deterministic_external_manifest()
    {
        Directory.CreateDirectory(tempRoot);
        var manifestPath = Path.Combine(tempRoot, "external-plugin.json");
        await File.WriteAllTextAsync(
            manifestPath,
            """
            {
              "name": "Repo Review",
              "version": "1.2.3",
              "description": "Reviews the current repository.",
              "command": "python",
              "args": ["main.py"],
              "tools": [
                {
                  "name": "repo_review",
                  "description": "Review a repository",
                  "requiresApproval": true
                }
              ],
              "extraField": "preserve me"
            }
            """);

        IPluginManifestImportService service = new PluginManifestImportService(new LocalFileSystem());

        var (request, result) = await service.ImportAsync(manifestPath, "external", CancellationToken.None);

        request.Manifest.Name.Should().Be("Repo Review");
        request.Manifest.EntryPoint.Should().Be("python");
        request.Manifest.Arguments.Should().ContainSingle().Which.Should().Be("main.py");
        request.Manifest.Tools.Should().ContainSingle().Which.Name.Should().Be("repo_review");
        result.Warnings.Should().Contain(warning => warning.Contains("extraField", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(tempRoot))
        {
            Directory.Delete(tempRoot, true);
        }
    }
}
