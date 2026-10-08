using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;
using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Memory.Services;

/// <summary>
/// Owns a bounded semantic cache. One async gate protects loading, queries, refresh, and eviction,
/// so a workspace can never be disposed while in use. Cached source-only refresh does not run generators.
/// </summary>
internal sealed class DotNetSemanticWorkspaceCache(IFileSystem fileSystem, IPathService pathService, IDotNetWorkspaceTargetResolver targetResolver) : IDisposable
{
    private const int Capacity = 4;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, Snapshot> entries = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private bool disposed;

    internal async Task<Lease> AcquireAsync(DotNetWorkspaceRequest request, IDotNetWorkspaceEvaluationAuthorization? authorization, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            var target = await targetResolver.ResolveAsync(request, cancellationToken).ConfigureAwait(false);
            if (target.Status != DotNetWorkspaceStatus.Ready)
            {
                return new Lease(target, null, gate);
            }

            var key = string.Join('\0', target.WorkspaceRoot, target.TargetPath, request.Configuration, request.TargetFramework);
            entries.TryGetValue(key, out var snapshot);
            var fingerprint = await FingerprintAsync(target, snapshot, cancellationToken).ConfigureAwait(false);
            if (snapshot is not null && (!snapshot.Reusable || !string.Equals(snapshot.Fingerprint, fingerprint, StringComparison.Ordinal)))
            {
                entries.Remove(key);
                snapshot.Workspace.Dispose();
                snapshot = null;
            }

            if (snapshot is null)
            {
                if (authorization is null || !await authorization.AuthorizeAsync(request with { WorkspaceRoot = target.WorkspaceRoot, Target = target.TargetPath }, cancellationToken).ConfigureAwait(false))
                {
                    return new Lease(target with { Status = DotNetWorkspaceStatus.PermissionDenied, Message = "MSBuild project evaluation requires execution authorization; no valid approved snapshot is cached." }, null, gate);
                }

                if (entries.Count == Capacity)
                {
                    var oldest = entries.MinBy(pair => pair.Value.LastUsedAtUtc);
                    entries.Remove(oldest.Key);
                    oldest.Value.Workspace.Dispose();
                }

                var properties = new Dictionary<string, string> { ["Configuration"] = request.Configuration, ["RestoreDuringBuild"] = "false" };
                if (!string.IsNullOrWhiteSpace(request.TargetFramework)) properties["TargetFramework"] = request.TargetFramework;
                var workspace = MSBuildWorkspace.Create(properties);
                var messages = new ConcurrentQueue<string>();
                using var failedHandler = workspace.RegisterWorkspaceFailedHandler(args => messages.Enqueue(args.Diagnostic.ToString()));
                try
                {
                    // Roslyn 5.9's bundled out-of-process build host resolves the workspace SDK and owns Locator registration.
                    var solution = target.TargetPath!.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        ? (await workspace.OpenProjectAsync(target.TargetPath, cancellationToken: cancellationToken).ConfigureAwait(false)).Solution
                        : await workspace.OpenSolutionAsync(target.TargetPath, cancellationToken: cancellationToken).ConfigureAwait(false);
                    foreach (var project in solution.Projects.ToArray())
                    {
                        if (project.FilePath is null || !DotNetWorkspaceTargetResolver.IsWithin(target.WorkspaceRoot, pathService.GetCanonicalFullPath(project.FilePath)))
                        {
                            throw new InvalidOperationException("A referenced project is outside the workspace boundary.");
                        }
                        using var reader = XmlReader.Create(new StringReader(await fileSystem.ReadAllTextIfExistsAsync(project.FilePath, cancellationToken).ConfigureAwait(false) ?? ""), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
                        var projectXml = XDocument.Load(reader);
                        if (projectXml.Root?.Attribute("Sdk") is not null && !fileSystem.FileExists(Path.Combine(Path.GetDirectoryName(project.FilePath)!, "obj", "project.assets.json")))
                        {
                            messages.Enqueue("Restore required: project.assets.json is missing for " + project.FilePath);
                        }
                        // Compiler queries never execute project-supplied analyzers or source generators.
                        solution = solution.WithProjectAnalyzerReferences(project.Id, []);
                    }

                    var status = messages.Count == 0 ? DotNetWorkspaceStatus.Ready : ClassifyFailure(string.Join('\n', messages), partial: true);
                    if (!solution.Projects.Any() || status is DotNetWorkspaceStatus.RestoreRequired or DotNetWorkspaceStatus.SdkUnavailable)
                    {
                        workspace.Dispose();
                        return new Lease(target with { Status = status == DotNetWorkspaceStatus.Ready ? DotNetWorkspaceStatus.LoadFailed : status, Message = string.Join('\n', messages) }, null, gate);
                    }

                    snapshot = new Snapshot(workspace, solution, status, messages.ToArray(), fingerprint);
                    snapshot.Fingerprint = await FingerprintAsync(target, snapshot, cancellationToken).ConfigureAwait(false);
                    entries.Add(key, snapshot);
                }
                catch (OperationCanceledException)
                {
                    workspace.Dispose();
                    throw;
                }
                catch (Exception exception) when (exception is IOException or InvalidOperationException or ArgumentException or XmlException || exception.GetType().Name == "RemoteInvocationException")
                {
                    workspace.Dispose();
                    return new Lease(target with { Status = ClassifyFailure(exception.Message), Message = exception.Message }, null, gate);
                }
            }

            try
            {
                await RefreshSourcesAsync(target.WorkspaceRoot, snapshot, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or InvalidOperationException)
            {
                entries.Remove(key);
                snapshot.Workspace.Dispose();
                return new Lease(target with { Status = DotNetWorkspaceStatus.LoadFailed, Message = exception.Message }, null, gate);
            }
            snapshot.LastUsedAtUtc = DateTimeOffset.UtcNow;
            return new Lease(target with { Status = snapshot.Status }, snapshot, gate);
        }
        catch
        {
            gate.Release();
            throw;
        }
    }

    private async Task RefreshSourcesAsync(string root, Snapshot snapshot, CancellationToken cancellationToken)
    {
        foreach (var document in snapshot.Solution.Projects.SelectMany(project => project.Documents).ToArray())
        {
            if (document.FilePath is null) continue;
            var path = pathService.GetCanonicalFullPath(document.FilePath);
            // Approved MSBuild loads can include SDK/package bootstrap sources outside the workspace.
            // Never reread these in an unapproved cached query; metadata stamps invalidate and require a fresh load.
            if (!DotNetWorkspaceTargetResolver.IsWithin(root, path)) continue;
            var content = await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false);
            if (content is null) throw new IOException($"Source document '{DotNetWorkspaceTargetResolver.Relative(root, path)}' disappeared; reload is required.");
            var previous = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            if (!string.Equals(previous.ToString(), content, StringComparison.Ordinal))
            {
                snapshot.Solution = snapshot.Solution.WithDocumentText(document.Id, SourceText.From(content, previous.Encoding ?? Encoding.UTF8));
            }
        }
    }

    private async Task<string> FingerprintAsync(DotNetWorkspaceTarget target, Snapshot? snapshot, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var inputs = EnumerateInputs(target.WorkspaceRoot).ToHashSet(StringComparer.Ordinal);
        for (var directory = new DirectoryInfo(target.WorkspaceRoot); directory is not null; directory = directory.Parent)
        {
            foreach (var name in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.Config", "nuget.config" })
            {
                var path = Path.Combine(directory.FullName, name);
                if (fileSystem.FileExists(path)) inputs.Add(path);
            }
        }
        if (snapshot is not null)
        {
            foreach (var project in snapshot.Solution.Projects)
            {
                var assets = Path.Combine(Path.GetDirectoryName(project.FilePath!)!, "obj", "project.assets.json");
                if (fileSystem.FileExists(assets)) inputs.Add(assets);
            }
        }
        if (snapshot is not null)
        {
            var externalInputs = snapshot.Solution.Projects.SelectMany(project => project.Documents.Select(document => document.FilePath)
                .Concat(project.MetadataReferences.OfType<PortableExecutableReference>().Select(reference => reference.FilePath)))
                .OfType<string>().Where(path => !DotNetWorkspaceTargetResolver.IsWithin(target.WorkspaceRoot, pathService.GetCanonicalFullPath(path))).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            foreach (var path in externalInputs)
            {
                var info = new FileInfo(path);
                hash.AppendData(Encoding.UTF8.GetBytes(path + "\0" + (info.Exists ? info.Length + ":" + info.LastWriteTimeUtc.Ticks : "missing") + "\0"));
            }
        }
        // Explicit imported build files can live outside the workspace. Track literal imports recursively;
        // property-driven/wildcard imports cannot be resolved safely without evaluation, so never reuse that snapshot.
        var importQueue = new Queue<string>(inputs.Where(input => Path.GetExtension(input).ToLowerInvariant() is ".csproj" or ".props" or ".targets"));
        var visitedImports = new HashSet<string>(StringComparer.Ordinal);
        while (importQueue.TryDequeue(out var buildFile))
        {
            if (!visitedImports.Add(buildFile)) continue;
            if (visitedImports.Count > 1024) { if (snapshot is not null) snapshot.Reusable = false; break; }
            var buildText = await fileSystem.ReadAllTextIfExistsAsync(buildFile, cancellationToken).ConfigureAwait(false);
            if (buildText is null) continue;
            using var reader = XmlReader.Create(new StringReader(buildText), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            XDocument xml;
            try { xml = XDocument.Load(reader); }
            catch (XmlException) { if (snapshot is not null) snapshot.Reusable = false; continue; }
            foreach (var import in xml.Descendants().Where(element => element.Name.LocalName == "Import"))
            {
                var imported = ((string?)import.Attribute("Project"))?.Replace("$(MSBuildThisFileDirectory)", Path.GetDirectoryName(buildFile) + Path.DirectorySeparatorChar, StringComparison.Ordinal);
                if (string.IsNullOrWhiteSpace(imported)) continue;
                if (imported.Contains("$(", StringComparison.Ordinal) || imported.Contains('*') || imported.Contains('?'))
                { if (snapshot is not null) snapshot.Reusable = false; continue; }
                var importPath = pathService.GetCanonicalFullPath(Path.Combine(Path.GetDirectoryName(buildFile)!, imported.Replace('\\', Path.DirectorySeparatorChar)));
                if (inputs.Add(importPath)) importQueue.Enqueue(importPath);
            }
        }
        foreach (var input in inputs.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(Encoding.UTF8.GetBytes(input + "\0"));
            // A changed compile-item path needs MSBuild reevaluation; content-only changes are refreshed without evaluation.
            if (!input.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                hash.AppendData(Encoding.UTF8.GetBytes(await fileSystem.ReadAllTextIfExistsAsync(input, cancellationToken).ConfigureAwait(false) ?? "<missing>"));
            }
            hash.AppendData([0]);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private IEnumerable<string> EnumerateInputs(string root)
    {
        var pending = new Stack<string>();
        var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        pending.Push(root);
        while (pending.TryPop(out var current))
        {
            current = pathService.GetCanonicalFullPath(current);
            if (!DotNetWorkspaceTargetResolver.IsWithin(root, current) || !visited.Add(current)) continue;
            foreach (var directory in fileSystem.EnumerateDirectories(current))
            {
                if (Path.GetFileName(directory).ToLowerInvariant() is not ("bin" or "obj" or ".git" or ".sharpclaw" or "node_modules")) pending.Push(directory);
            }
            foreach (var file in fileSystem.EnumerateFiles(current, "*"))
            {
                if (Path.GetExtension(file).ToLowerInvariant() is ".cs" or ".csproj" or ".sln" or ".slnx" or ".props" or ".targets" or ".editorconfig"
                    || Path.GetFileName(file).ToLowerInvariant() is "global.json" or "packages.lock.json" or "nuget.config") yield return file;
            }
        }
    }

    private static DotNetWorkspaceStatus ClassifyFailure(string message, bool partial = false)
    {
        if (message.Contains("project.assets.json", StringComparison.OrdinalIgnoreCase) || message.Contains("NETSDK1004", StringComparison.Ordinal)) return DotNetWorkspaceStatus.RestoreRequired;
        if (message.Contains("SDK", StringComparison.OrdinalIgnoreCase) && (message.Contains("not found", StringComparison.OrdinalIgnoreCase) || message.Contains("could not", StringComparison.OrdinalIgnoreCase) || message.Contains("not installed", StringComparison.OrdinalIgnoreCase))) return DotNetWorkspaceStatus.SdkUnavailable;
        return partial ? DotNetWorkspaceStatus.Partial : DotNetWorkspaceStatus.LoadFailed;
    }

    public void Dispose()
    {
        gate.Wait();
        try
        {
            if (disposed) return;
            disposed = true;
            foreach (var snapshot in entries.Values) snapshot.Workspace.Dispose();
            entries.Clear();
        }
        finally { gate.Release(); }
    }

    internal sealed class Snapshot(MSBuildWorkspace workspace, Solution solution, DotNetWorkspaceStatus status, string[] messages, string fingerprint)
    {
        internal MSBuildWorkspace Workspace { get; } = workspace;
        internal Solution Solution { get; set; } = solution;
        internal DotNetWorkspaceStatus Status { get; } = status;
        internal string[] Messages { get; } = messages;
        internal string Fingerprint { get; set; } = fingerprint;
        internal bool Reusable { get; set; } = true;
        internal DateTimeOffset LastUsedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    }

    internal sealed class Lease(DotNetWorkspaceTarget target, Snapshot? snapshot, SemaphoreSlim gate) : IDisposable
    {
        internal DotNetWorkspaceTarget Target { get; } = target;
        internal Snapshot? Snapshot { get; } = snapshot;
        private bool disposed;
        public void Dispose() { if (!disposed) { disposed = true; gate.Release(); } }
    }
}
