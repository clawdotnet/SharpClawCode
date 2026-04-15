using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Runtime.Abstractions;

namespace SharpClaw.Code.Runtime.Diagnostics;

/// <summary>
/// Produces a cached workspace diagnostics snapshot using configured LSP metadata plus .NET build diagnostics when available.
/// </summary>
public sealed partial class WorkspaceDiagnosticsService(
    ISharpClawConfigService configService,
    IProcessRunner processRunner,
    ISystemClock systemClock,
    ILogger<WorkspaceDiagnosticsService> logger) : IWorkspaceDiagnosticsService
{
    private static readonly ConcurrentDictionary<string, WorkspaceDiagnosticsSnapshot> Cache = new(StringComparer.Ordinal);
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(15);
    private const int MaxCacheEntries = 50;

    /// <inheritdoc />
    public async Task<WorkspaceDiagnosticsSnapshot> BuildSnapshotAsync(string workspaceRoot, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        if (Cache.TryGetValue(workspaceRoot, out var cached)
            && systemClock.UtcNow - cached.GeneratedAtUtc < CacheLifetime)
        {
            return cached;
        }

        var config = await configService.GetConfigAsync(workspaceRoot, cancellationToken).ConfigureAwait(false);
        var configuredServers = (IReadOnlyList<ConfiguredLspServerDefinition>)(config.Document.LspServers ?? []);
        var diagnostics = new List<WorkspaceDiagnosticItem>();

        var buildTarget = FindBuildTarget(workspaceRoot);
        if (!string.IsNullOrWhiteSpace(buildTarget))
        {
            try
            {
                var result = await processRunner.RunAsync(
                    new ProcessRunRequest(
                        "dotnet",
                        ["build", buildTarget, "--nologo", "--no-restore", "-consolelogger:NoSummary"],
                        workspaceRoot,
                        null),
                    cancellationToken).ConfigureAwait(false);

                diagnostics.AddRange(ParseDotnetDiagnostics(result.StandardOutput, "dotnet-build"));
                diagnostics.AddRange(ParseDotnetDiagnostics(result.StandardError, "dotnet-build"));
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                logger.LogDebug(ex, "Skipping build-backed diagnostics for workspace {WorkspaceRoot}.", workspaceRoot);
            }
        }

        var snapshot = new WorkspaceDiagnosticsSnapshot(workspaceRoot, systemClock.UtcNow, configuredServers, diagnostics);
        Cache[workspaceRoot] = snapshot;
        EvictCacheEntries();
        return snapshot;
    }

    private static string? FindBuildTarget(string workspaceRoot)
    {
        var solution = Directory.EnumerateFiles(workspaceRoot, "*.sln", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(solution))
        {
            return Path.GetFileName(solution);
        }

        var project = Directory.EnumerateFiles(workspaceRoot, "*.csproj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        return string.IsNullOrWhiteSpace(project) ? null : Path.GetFileName(project);
    }

    private static IEnumerable<WorkspaceDiagnosticItem> ParseDotnetDiagnostics(string text, string source)
    {
        foreach (Match match in DotnetDiagnosticRegex().Matches(text ?? string.Empty))
        {
            yield return new WorkspaceDiagnosticItem(
                string.Equals(match.Groups["severity"].Value, "warning", StringComparison.OrdinalIgnoreCase)
                    ? WorkspaceDiagnosticSeverity.Warning
                    : WorkspaceDiagnosticSeverity.Error,
                NullIfEmpty(match.Groups["code"].Value),
                match.Groups["message"].Value.Trim(),
                NullIfEmpty(match.Groups["path"].Value),
                ParseNullableInt(match.Groups["line"].Value),
                ParseNullableInt(match.Groups["column"].Value),
                source);
        }
    }

    private static int? ParseNullableInt(string value)
        => int.TryParse(value, out var parsed) ? parsed : null;

    private static string? NullIfEmpty(string value)
        => string.IsNullOrWhiteSpace(value) ? null : value;

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

    [GeneratedRegex(@"^(?<path>.*?)(?:\((?<line>\d+),(?<column>\d+)\))?:\s*(?<severity>error|warning)\s*(?<code>[A-Z]{2,}\d+)?\s*:?\s*(?<message>.+)$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex DotnetDiagnosticRegex();
}
