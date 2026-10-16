using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Tools.Execution;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>Applies a compiler-calculated multi-file rename with content preconditions and recoverable rollback.</summary>
public sealed class CSharpRenameSymbolTool(IDotNetWorkspaceSemanticService semantics, IFileSystem fileSystem, IPathService paths, IPermissionPolicyEngine policy, IRuntimeEventPublisher? publisher = null, IRuntimeHostContextAccessor? host = null) : SharpClawToolBase
{
    /// <summary>The stable tool name.</summary>
    public const string ToolName = "csharp_rename_symbol";
    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(ToolName, "Rename one exact source symbol, recording all changed files for undo. Cold MSBuild loading requires execution permission.", ApprovalScope.FileSystemWrite, true, true, nameof(CSharpToolArguments), "Exact symbol selectors plus newName and optional target/configuration/framework.", ["dotnet", "semantic", "mutation"], """{"type":"object","properties":{"name":{"type":"string"},"newName":{"type":"string"},"container":{"type":"string"},"kind":{"type":"string"},"project":{"type":"string"},"path":{"type":"string"},"signature":{"type":"string"},"line":{"type":"integer","minimum":1},"column":{"type":"integer","minimum":1},"target":{"type":"string"},"configuration":{"type":"string"},"targetFramework":{"type":"string"}},"required":["name","newName"],"additionalProperties":false}""");

    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments(request, ProtocolJsonContext.Default.CSharpToolArguments);
        var resolver = new WorkspacePathResolver(paths);
        await using var writeLock = await fileSystem.AcquireExclusiveFileLockAsync(resolver.ResolvePath(context, ".sharpclaw/locks/semantic-write.lock"), cancellationToken).ConfigureAwait(false);
        var plan = await semantics.PlanRenameAsync(arguments.Workspace(context.WorkspaceRoot), arguments, new DotNetEvaluationAuthorization(policy, context, request, publisher, host), cancellationToken).ConfigureAwait(false);
        if (plan.Errors.Length != 0) return Result(new(false, plan.Resolution, plan.NewName, [], plan.Errors, []));
        // Validate every precondition before writing the first byte.
        foreach (var change in plan.Changes) await ValidateAsync(change, cancellationToken).ConfigureAwait(false);
        var attempted = new List<CSharpRenameChange>();
        try
        {
            foreach (var change in plan.Changes)
            {
                await ValidateAsync(change, cancellationToken).ConfigureAwait(false);
                attempted.Add(change);
                await fileSystem.WriteAllTextAsync(resolver.ResolvePath(context, change.Path), change.ContentAfter, cancellationToken).ConfigureAwait(false);
            }
            foreach (var change in attempted) Record(change, change.ContentAfter);
            return Result(new(true, plan.Resolution, plan.NewName, attempted.Select(change => change.Path).ToArray(), [], []));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var errors = new List<string> { failure.Message };
            var residual = new List<string>();
            foreach (var change in attempted.AsEnumerable().Reverse())
            {
                try
                {
                    var path = resolver.ResolvePath(context, change.Path);
                    var actual = await fileSystem.ReadAllTextIfExistsAsync(path, cleanup.Token).ConfigureAwait(false);
                    if (actual == change.ContentBefore) continue;
                    if (actual != change.ContentAfter) throw new IOException("Rollback precondition changed; file requires recovery: " + change.Path);
                    await fileSystem.WriteAllTextAsync(path, change.ContentBefore, cleanup.Token).ConfigureAwait(false);
                }
                catch (Exception rollback) when (rollback is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException)
                {
                    errors.Add(rollback.Message);
                    residual.Add(change.Path);
                    // Inspect independently even if the cleanup budget expired; record known after-state if inaccessible.
                    using var inspect = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    string? actual = change.ContentAfter;
                    try { actual = await fileSystem.ReadAllTextIfExistsAsync(resolver.ResolvePath(context, change.Path), inspect.Token).ConfigureAwait(false); }
                    catch (Exception read) when (read is IOException or UnauthorizedAccessException or OperationCanceledException) { errors.Add("Residual state inspection failed: " + read.Message); }
                    Record(change, actual);
                }
            }
            if (failure is OperationCanceledException) throw;
            return Result(new(false, plan.Resolution, plan.NewName, [], errors.ToArray(), residual.ToArray()));
        }

        async Task ValidateAsync(CSharpRenameChange change, CancellationToken token)
        {
            var current = await fileSystem.ReadAllTextIfExistsAsync(resolver.ResolvePath(context, change.Path), token).ConfigureAwait(false);
            if (current != change.ContentBefore) throw new InvalidOperationException("Concurrent file change detected: " + change.Path);
        }
        void Record(CSharpRenameChange change, string? after) => context.MutationRecorder?.Record(new("op-" + Guid.NewGuid().ToString("N"), after is null ? FileMutationKind.Delete : FileMutationKind.Replace, ToolName, change.Path, change.ContentBefore, after ?? change.ContentBefore));
        ToolResult Result(CSharpRenameResult result) => CreateTypedResult(context, request, result, ProtocolJsonContext.Default.CSharpRenameResult, result.Succeeded, result.Succeeded ? "Renamed " + attemptedPaths(result) : string.Join("; ", result.Errors));
        static string attemptedPaths(CSharpRenameResult result) => string.Join(", ", result.ChangedPaths);
    }
}
