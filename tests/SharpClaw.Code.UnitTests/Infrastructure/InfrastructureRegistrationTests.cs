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

        combined.Should().EndWith("/tmp/sharpclaw/sessions");
        normalized.Should().NotBeNullOrWhiteSpace();
    }
}
