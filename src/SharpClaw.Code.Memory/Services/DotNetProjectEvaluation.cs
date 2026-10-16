using System.Text.Json;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Infrastructure.Models;

namespace SharpClaw.Code.Memory.Services;

/// <summary>Reads evaluated SDK properties only during an authorized load; never invokes restore or build targets.</summary>
internal sealed record DotNetProjectEvaluation(string AssetsPath, bool? IsTestProject, string[] TargetFrameworks)
{
    internal static async Task<DotNetProjectEvaluation> ReadAsync(IProcessRunner processes, string projectPath, string configuration, string? framework, CancellationToken cancellationToken)
    {
        var arguments = new List<string>
        {
            "msbuild", projectPath, "-nologo", "-nodeReuse:false",
            "-getProperty:ProjectAssetsFile,IsTestProject,TargetFramework,TargetFrameworks", "-property:Configuration=" + configuration,
        };
        if (!string.IsNullOrWhiteSpace(framework)) arguments.Add("-property:TargetFramework=" + framework);
        var result = await processes.RunAsync(new ProcessRunRequest("dotnet", arguments.ToArray(), Path.GetDirectoryName(projectPath)!, null), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) throw new InvalidOperationException(result.StandardOutput + result.StandardError);
        using var json = JsonDocument.Parse(result.StandardOutput);
        var properties = json.RootElement.GetProperty("Properties");
        string Value(string name) => properties.GetProperty(name).GetString() ?? "";
        var frameworks = (Value("TargetFrameworks") + ";" + Value("TargetFramework")).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToArray();
        var assetsPath = Value("ProjectAssetsFile");
        if (assetsPath.Length != 0) assetsPath = Path.GetFullPath(assetsPath, Path.GetDirectoryName(projectPath)!);
        return new(assetsPath, bool.TryParse(Value("IsTestProject"), out var isTest) ? isTest : null, frameworks);
    }

    internal static async Task<string[]> TrustedAnalyzerRootsAsync(IProcessRunner processes, CancellationToken cancellationToken)
    {
        // The dotnet installation selects trusted SDK code, never an MSBuild property controlled by the project.
        var result = await processes.RunAsync(new ProcessRunRequest("dotnet", ["--list-sdks"], AppContext.BaseDirectory, null), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) return [];
        return result.StandardOutput.Split('\n').Where(line => line.Contains('[') && line.TrimEnd().EndsWith(']'))
            .Select(line => Path.GetDirectoryName(line[(line.IndexOf('[') + 1)..line.LastIndexOf(']')])!)
            .Where(root => !string.IsNullOrWhiteSpace(root)).Distinct().ToArray();
    }

    internal static bool IsTrustedAnalyzer(string? analyzerPath, string[] roots, IPathService paths)
    {
        if (analyzerPath is null) return false;
        var path = paths.GetCanonicalFullPath(analyzerPath);
        return roots.Any(root =>
            DotNetWorkspaceTargetResolver.IsWithin(paths.GetCanonicalFullPath(Path.Combine(root, "packs")), path)
                && path.Replace('\\', '/').Contains("/analyzers/", StringComparison.Ordinal)
            || DotNetWorkspaceTargetResolver.IsWithin(paths.GetCanonicalFullPath(Path.Combine(root, "sdk")), path)
                && path.Replace('\\', '/').Contains("/Sdks/Microsoft.NET.Sdk/analyzers/", StringComparison.Ordinal));
    }
}
