using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure;
using SharpClaw.Code.Infrastructure.Abstractions;

namespace SharpClaw.Code.UnitTests.Infrastructure;

/// <summary>
/// Verifies the infrastructure service registrations and core seams.
/// </summary>
public sealed class InfrastructureRegistrationTests
{
    /// <summary>
    /// Ensures the infrastructure composition root registers the expected abstractions.
    /// </summary>
    [Fact]
    public void AddSharpClawInfrastructure_should_register_core_abstractions()
    {
        var services = new ServiceCollection();

        services.AddSharpClawInfrastructure();
        using var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<IFileSystem>().Should().NotBeNull();
        serviceProvider.GetRequiredService<ISystemClock>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IPathService>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IProcessRunner>().Should().NotBeNull();
        serviceProvider.GetRequiredService<IShellExecutor>().Should().NotBeNull();
    }

    /// <summary>
    /// Ensures the path service can normalize and combine paths consistently.
    /// </summary>
    [Fact]
    public void Path_service_should_normalize_and_combine_paths()
    {
        var services = new ServiceCollection();
        services.AddSharpClawInfrastructure();
        using var serviceProvider = services.BuildServiceProvider();
        var pathService = serviceProvider.GetRequiredService<IPathService>();

        var combined = pathService.Combine("/tmp", "sharpclaw", "sessions");
        var normalized = pathService.GetFullPath(".");

        combined.Should().Be(Path.Combine("/tmp", "sharpclaw", "sessions"));
        normalized.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// Ensures canonical path resolution follows existing symlinks/junctions.
    /// </summary>
    [Fact]
    public void Path_service_should_canonicalize_symlinked_paths()
    {
        var services = new ServiceCollection();
        services.AddSharpClawInfrastructure();
        using var serviceProvider = services.BuildServiceProvider();
        var pathService = serviceProvider.GetRequiredService<IPathService>();
        var workspace = Path.Combine(Path.GetTempPath(), "sharpclaw-path-tests", Guid.NewGuid().ToString("N"));
        var outside = Path.Combine(Path.GetTempPath(), "sharpclaw-path-targets", Guid.NewGuid().ToString("N"));
        var link = Path.Combine(workspace, "linked");
        Directory.CreateDirectory(workspace);
        Directory.CreateDirectory(outside);

        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }
        catch (PlatformNotSupportedException)
        {
            return;
        }

        var canonical = pathService.GetCanonicalFullPath(Path.Combine(link, "file.txt"));
        var expected = pathService.Combine(pathService.GetCanonicalFullPath(outside), "file.txt");

        canonical.Should().Be(expected);
    }
}
