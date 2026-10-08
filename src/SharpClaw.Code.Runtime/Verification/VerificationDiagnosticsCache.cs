using System.Collections.Concurrent;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Models;
namespace SharpClaw.Code.Runtime.Verification;
/// <summary>Retains up to fifty recent authorized reports, independently of prompt snapshot caching.</summary>
public sealed class VerificationDiagnosticsCache(IPathService paths, ISystemClock clock) : IVerificationDiagnosticsCache
{
    private readonly ConcurrentDictionary<string, VerificationRunReport> reports = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    /// <inheritdoc />
    public void Store(VerificationRunReport report)
    {
        reports[paths.GetCanonicalFullPath(report.WorkspaceRoot)] = report;
        foreach (var key in reports.OrderBy(pair => pair.Value.CompletedAtUtc).Take(Math.Max(0, reports.Count - 50)).Select(pair => pair.Key)) reports.TryRemove(key, out _);
    }
    /// <inheritdoc />
    public VerificationRunReport? Get(string workspaceRoot) => reports.TryGetValue(paths.GetCanonicalFullPath(workspaceRoot), out var report) && clock.UtcNow - report.CompletedAtUtc < TimeSpan.FromMinutes(5) ? report : null;
}
