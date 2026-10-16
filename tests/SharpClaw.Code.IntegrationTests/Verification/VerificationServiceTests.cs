using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.IntegrationTests.Fixtures;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime;
using SharpClaw.Code.Runtime.Verification;

namespace SharpClaw.Code.IntegrationTests.Verification;

/// <summary>Exercises offline build/test verification without providers or implicit restore.</summary>
public sealed class VerificationServiceTests
{
    /// <summary>A real fixture builds and emits parsed TRX results, including a test-sensitive failure.</summary>
    [Fact]
    public async Task Real_all_verification_builds_tests_and_reports_test_failure()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync(includeTests: true);
        using var provider = Provider(fixture);
        var service = provider.GetRequiredService<IVerificationService>();
        var result = await service.VerifyAsync(new(fixture.Root, VerificationScope.All), CancellationToken.None);
        result.Status.Should().Be(VerificationStatus.Passed, result.Summary);
        result.Tests!.Total.Should().Be(1);
        result.Tests.Passed.Should().Be(1);
        result.Build!.Steps.Should().OnlyContain(step => step.Status == VerificationStatus.Passed);
        var path = Path.Combine(fixture.Root, "Library/Invoice.cs");
        await File.WriteAllTextAsync(path, (await File.ReadAllTextAsync(path)).Replace("\"invoice\"", "\"wrong\"", StringComparison.Ordinal));
        var failed = await service.VerifyAsync(new(fixture.Root, VerificationScope.Affected, ChangedPaths: ["Library/Invoice.cs"]), CancellationToken.None);
        failed.Reason.Should().Be(VerificationFailureReason.TestFailed, failed.Summary);
        failed.Tests!.Cases.Should().Contain(item => item.Outcome == "Failed" && item.Message!.Contains("invoice", StringComparison.Ordinal));
        failed.AffectedProjects.Should().Contain("Fixture.Tests/Fixture.Tests.csproj");
    }

    /// <summary>Compile failures have structured locations and missing outputs/assets remain explicit prerequisites.</summary>
    [Fact]
    public async Task Compile_and_prerequisite_failures_are_structured()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync(includeTests: true);
        using var provider = Provider(fixture);
        var service = provider.GetRequiredService<IVerificationService>();
        var testOnly = await service.VerifyAsync(new(fixture.Root, VerificationScope.Tests), CancellationToken.None);
        testOnly.Reason.Should().Be(VerificationFailureReason.TestOutputUnavailable);
        var path = Path.Combine(fixture.Root, "Library/Invoice.cs");
        await File.AppendAllTextAsync(path, "\nthis is invalid C#;\n");
        var failed = await service.VerifyAsync(new(fixture.Root, VerificationScope.All), CancellationToken.None);
        failed.Reason.Should().Be(VerificationFailureReason.BuildFailed);
        failed.Build!.Diagnostics.Should().Contain(item => item.Path == "Library/Invoice.cs" && item.Line > 0);
        failed.Tests.Should().BeNull();
        File.Delete(Path.Combine(fixture.Root, "Library/obj/project.assets.json"));
        var missing = await service.VerifyAsync(new(fixture.Root, VerificationScope.Build), CancellationToken.None);
        missing.Reason.Should().Be(VerificationFailureReason.RestoreRequired);
        File.Exists(Path.Combine(fixture.Root, "Library/obj/project.assets.json")).Should().BeFalse();
    }

    /// <summary>Process exit, missing TRX and timeouts cannot masquerade as success.</summary>
    [Fact]
    public async Task Process_failure_missing_reports_timeout_and_no_target_are_distinct()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync(includeTests: true);
        var runner = new ControlledRunner();
        using var provider = Provider(fixture, runner);
        var service = provider.GetRequiredService<IVerificationService>();
        var result = await service.VerifyAsync(new(fixture.Root, VerificationScope.All), CancellationToken.None);
        result.Reason.Should().Be(VerificationFailureReason.TestResultsMissing);
        runner.Requests.Should().OnlyContain(request => request.Arguments.Contains("--no-restore"));
        runner.Fail = true;
        (await service.VerifyAsync(new(fixture.Root, VerificationScope.Build), CancellationToken.None)).Reason.Should().Be(VerificationFailureReason.BuildFailed);
        runner.Fail = false;
        runner.Wait = true;
        (await service.VerifyAsync(new(fixture.Root, VerificationScope.Build, TimeoutSeconds: 1), CancellationToken.None)).Reason.Should().Be(VerificationFailureReason.Timeout);
        var empty = Path.Combine(fixture.Root, "empty");
        Directory.CreateDirectory(empty);
        (await service.VerifyAsync(new(empty), CancellationToken.None)).Status.Should().Be(VerificationStatus.Skipped);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = () => service.VerifyAsync(new(fixture.Root), cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>Warnings and incidental paths cannot suppress compilation repair by impersonating missing prerequisites.</summary>
    [Theory]
    [InlineData("/dotnet/sdk/10.0.100/Microsoft.Common.targets: warning NU1603: Package was not found.\nFile.cs(1,1): error CS1002: ; expected", VerificationFailureReason.BuildFailed)]
    [InlineData("warning NU1603: project.assets.json package was not found.\nFile.cs(1,1): error CS1002: ; expected", VerificationFailureReason.BuildFailed)]
    [InlineData("error NETSDK1004: Assets file is missing.", VerificationFailureReason.RestoreRequired)]
    [InlineData("error NETSDK1045: Current SDK does not support the target framework.", VerificationFailureReason.SdkUnavailable)]
    [InlineData("MSBUILD : error MSB4236: The SDK specified could not be found.", VerificationFailureReason.SdkUnavailable)]
    public async Task Failure_classification_uses_error_codes(string output, VerificationFailureReason expected)
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        using var provider = Provider(fixture, new ControlledRunner { Fail = true, Output = output });
        (await provider.GetRequiredService<IVerificationService>().VerifyAsync(new(fixture.Root, VerificationScope.Build), CancellationToken.None)).Reason.Should().Be(expected);
    }

    /// <summary>Multi-targeted projects are invoked once and all TRX files are counted exactly once.</summary>
    [Fact]
    public async Task Multi_targeted_test_project_runs_once_per_project_path()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync(includeTests: true);
        var project = Path.Combine(fixture.Root, "Fixture.Tests", "Fixture.Tests.csproj");
        await File.WriteAllTextAsync(project, (await File.ReadAllTextAsync(project)).Replace("<TargetFramework>net10.0</TargetFramework>", "<TargetFrameworks>net10.0;net10.0-windows</TargetFrameworks>"));
        await fixture.RestoreAsync();
        using var provider = Provider(fixture);
        var result = await provider.GetRequiredService<IVerificationService>().VerifyAsync(new(fixture.Root, VerificationScope.All), CancellationToken.None);
        result.Status.Should().Be(VerificationStatus.Passed, result.Summary);
        result.Tests!.Steps.Should().ContainSingle();
        result.Tests.Total.Should().Be(2);
        result.AffectedProjects.Should().OnlyHaveUniqueItems();
    }

    /// <summary>Workspace builds lasting more than ten seconds serialize instead of failing with an IO exception.</summary>
    [Fact]
    public async Task Workspace_lock_waits_beyond_filesystem_retry_window_and_is_bounded_by_request_timeout()
    {
        using var fixture = await DotNetFixtureWorkspace.CreateAsync();
        using var provider = Provider(fixture, new ControlledRunner());
        var service = provider.GetRequiredService<IVerificationService>();
        var handle = await new LocalFileSystem().AcquireExclusiveFileLockAsync(Path.Combine(fixture.Root, ".sharpclaw", "locks", "verification.lock"));
        try
        {
            (await service.VerifyAsync(new(fixture.Root, VerificationScope.Build, TimeoutSeconds: 1), CancellationToken.None)).Reason.Should().Be(VerificationFailureReason.Timeout);
            var waiting = service.VerifyAsync(new(fixture.Root, VerificationScope.Build, TimeoutSeconds: 30), CancellationToken.None);
            await Task.Delay(TimeSpan.FromSeconds(11));
            waiting.IsCompleted.Should().BeFalse();
            await handle.DisposeAsync();
            handle = null!;
            (await waiting).Status.Should().Be(VerificationStatus.Passed);
        }
        finally { if (handle is not null) await handle.DisposeAsync(); }
    }
    private static ServiceProvider Provider(DotNetFixtureWorkspace fixture, IProcessRunner? runner = null)
    {
        var services = new ServiceCollection();
        services.AddSharpClawRuntime();
        services.AddSingleton<IDotNetWorkspaceSemanticService>(fixture.Service);
        if (runner is not null) services.AddSingleton(runner);
        return services.BuildServiceProvider();
    }
    private sealed class ControlledRunner : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = [];
        internal bool Fail { get; set; }
        internal bool Wait { get; set; }
        internal string Output { get; set; } = "";
        public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (Wait) await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new(Fail ? 1 : 0, Output, "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        }
    }
}
