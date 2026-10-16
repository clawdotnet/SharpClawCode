using FluentAssertions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Services;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Verification;

namespace SharpClaw.Code.UnitTests.Verification;

public sealed class VerificationDiagnosticsCacheTests
{
    [Fact]
    public void Tests_only_or_prerequisite_reports_do_not_erase_build_errors_and_out_of_order_reports_are_ignored()
    {
        var now = DateTimeOffset.UtcNow;
        var root = Path.GetFullPath("workspace");
        var cache = new VerificationDiagnosticsCache(new PathService(), new FixedClock(now));
        var build = new VerificationBuildReport(VerificationStatus.Failed, [], [new("File.cs", 1, 1, "CS1002", "error", "; expected", "Project")]);
        var broken = new VerificationRunReport(root, "Project", VerificationScope.Build, VerificationStatus.Failed, VerificationFailureReason.BuildFailed, now, now, 0, [], build, null, null, "broken");
        cache.Store(broken);
        cache.Store(broken with { Scope = VerificationScope.Tests, Build = null, CompletedAtUtc = now.AddSeconds(1) });
        cache.Store(broken with { Reason = VerificationFailureReason.LoadFailed, Build = null, CompletedAtUtc = now.AddSeconds(2) });
        cache.Store(broken with { CompletedAtUtc = now.AddSeconds(-1), Build = new(VerificationStatus.Passed, [], []) });
        cache.Get(root).Should().Be(broken);
        var fixedBuild = broken with { CompletedAtUtc = now.AddSeconds(3), Build = new(VerificationStatus.Passed, [], []) };
        cache.Store(fixedBuild);
        cache.Get(root).Should().Be(fixedBuild);
    }

    private sealed class FixedClock(DateTimeOffset now) : ISystemClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
