using System.Collections.Concurrent;
using SharpClaw.Code.Runtime.Verification;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.Diagnostics;

/// <summary>
/// Produces a cached workspace diagnostics snapshot using configured LSP metadata plus previously authorized .NET verification diagnostics. Reading snapshots never starts a process.
/// </summary>
public sealed partial class WorkspaceDiagnosticsService(
    ISharpClawConfigService configService,
    IProcessRunner processRunner,
    ISystemClock systemClock,
    ILogger<WorkspaceDiagnosticsService> logger,
    IVerificationDiagnosticsCache? verificationCache = null) : IWorkspaceDiagnosticsService
{
    private static readonly ConcurrentDictionary<string, WorkspaceDiagnosticsSnapshot> Cache = new(StringComparer.Ordinal);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(15);
    private const int MaxCacheEntries = 50;

    /// <inheritdoc />
    public async Task<WorkspaceDiagnosticsSnapshot> BuildSnapshotAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        _ = processRunner; // Retained constructor compatibility; execution belongs exclusively to verification.
        _ = logger;
        var verified = verificationCache?.Get(workspaceRoot);
        if (Cache.TryGetValue(workspaceRoot, out var cached)
            && systemClock.UtcNow - cached.GeneratedAtUtc < CacheLifetime
            && (verified is null || verified.CompletedAtUtc <= cached.GeneratedAtUtc))
        {
            return cached;
        }

        var config = await configService.GetConfigAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var configuredServers = (IReadOnlyList<ConfiguredLspServerDefinition>)(config.Document.LspServers ?? []);
        var diagnostics = new List<WorkspaceDiagnosticItem>();

        diagnostics.AddRange((verified?.Build?.Diagnostics ?? []).Select(item => new WorkspaceDiagnosticItem(
            item.Severity == "warning" ? WorkspaceDiagnosticSeverity.Warning : WorkspaceDiagnosticSeverity.Error,
            item.Code, item.Message, item.Path, item.Line, item.Column, "dotnet-build")));

        var snapshot = new WorkspaceDiagnosticsSnapshot(workspaceRoot, systemClock.UtcNow, configuredServers, diagnostics);
        Cache[workspaceRoot] = snapshot;
        EvictCacheEntries();
        return snapshot;
    }

    private void EvictCacheEntries()
    {
        var now = systemClock.UtcNow;
        foreach (var key in Cache.Keys)
        {
            if (Cache.TryGetValue(key, out var entry) && now - entry.GeneratedAtUtc > CacheLifetime)
            {
                Cache.TryRemove(key, out _);
            }
        }

        if (Cache.Count <= MaxCacheEntries)
        {
            return;
        }

        var overflowKeys = Cache
            .OrderBy(static pair => pair.Value.GeneratedAtUtc)
            .ThenBy(static pair => pair.Key, StringComparer.Ordinal)
            .Take(Cache.Count - MaxCacheEntries)
            .Select(static pair => pair.Key)
            .ToArray();

        foreach (var key in overflowKeys)
        {
            Cache.TryRemove(key, out _);
        }
    }

}
