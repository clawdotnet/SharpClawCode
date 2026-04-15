using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;
using SharpClaw.Code.Runtime.Diagnostics;

namespace SharpClaw.Code.UnitTests.Runtime;

/// <summary>
/// Verifies workspace diagnostics caching remains bounded as new workspaces are inspected.
/// </summary>
public sealed class WorkspaceDiagnosticsServiceTests
{
    [Fact]
    public async Task BuildSnapshotAsync_evicts_oldest_entries_when_cache_exceeds_limit()
    {
        ClearCache();
        var workspaces = Enumerable.Range(0, 60)
            .Select(_ => Path.Combine(Path.GetTempPath(), "sharpclaw-diagnostics-tests", Guid.NewGuid().ToString("N")))
            .ToArray();

        try
        {
            var clock = new MutableClock(DateTimeOffset.Parse("2026-04-14T20:00:00Z"));
            var service = new WorkspaceDiagnosticsService(
                new FixedConfigService(),
                new NoOpProcessRunner(),
                clock,
                NullLogger<WorkspaceDiagnosticsService>.Instance);

            foreach (var workspace in workspaces)
            {
                Directory.CreateDirectory(workspace);
                await service.BuildSnapshotAsync(workspace, CancellationToken.None);
                clock.Advance(TimeSpan.FromMilliseconds(100));
            }

            var cacheEntries = GetCacheEntries();
            cacheEntries.Should().HaveCount(50);
            cacheEntries.Keys.Should().NotContain(workspaces[0]);
            cacheEntries.Keys.Should().Contain(workspaces[^1]);
        }
        finally
        {
            foreach (var workspace in workspaces)
            {
                if (Directory.Exists(workspace))
                {
                    Directory.Delete(workspace, recursive: true);
                }
            }

            ClearCache();
        }
    }

    private static Dictionary<string, WorkspaceDiagnosticsSnapshot> GetCacheEntries()
    {
        var field = typeof(WorkspaceDiagnosticsService)
            .GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Workspace diagnostics cache field was not found.");
        return ((IEnumerable<KeyValuePair<string, WorkspaceDiagnosticsSnapshot>>)field.GetValue(null)!)
            .ToDictionary(static entry => entry.Key, static entry => entry.Value);
    }

    private static void ClearCache()
    {
        var field = typeof(WorkspaceDiagnosticsService)
            .GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Workspace diagnostics cache field was not found.");
        var cache = field.GetValue(null) ?? throw new InvalidOperationException("Workspace diagnostics cache instance was null.");
        cache.GetType().GetMethod("Clear")!.Invoke(cache, null);
    }

    private sealed class MutableClock(DateTimeOffset utcNow) : ISystemClock
    {
        public DateTimeOffset UtcNow { get; private set; } = utcNow;

        public void Advance(TimeSpan delta) => UtcNow += delta;
    }

    private sealed class FixedConfigService : ISharpClawConfigService
    {
        public Task<SharpClawConfigSnapshot> GetConfigAsync(string workspaceRoot, CancellationToken cancellationToken)
            => Task.FromResult(
                new SharpClawConfigSnapshot(
                    workspaceRoot,
                    null,
                    null,
                    new SharpClawConfigDocument(ShareMode.Manual, null, null, null, null, null, null)));
    }

    private sealed class NoOpProcessRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
        {
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty, now, now));
        }
    }
}
