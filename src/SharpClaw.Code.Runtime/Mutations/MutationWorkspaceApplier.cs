using SharpClaw.Code.Infrastructure.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
using SharpClaw.Code.Tools.Utilities;

namespace SharpClaw.Code.Runtime.Mutations;

/// <summary>
/// Applies forward/inverse <see cref="FileMutationOperation"/> records against a workspace root.
/// </summary>
public sealed class MutationWorkspaceApplier(IFileSystem fileSystem, IPathService pathService)
{
    /// <summary>
    /// Applies the inverse of a mutation, verifying the workspace matches <see cref="FileMutationOperation.ContentAfter"/> when required.
    /// </summary>
    public async Task ApplyInverseAsync(string workspaceRoot, FileMutationOperation operation, CancellationToken cancellationToken)
    {
        var resolver = new WorkspacePathResolver(pathService);
        var root = pathService.GetFullPath(workspaceRoot);
        var context = CreateBypassContext(root);
        var fullPath = resolver.ResolvePath(context, operation.RelativePath);

        var current = await fileSystem.ReadAllTextIfExistsAsync(fullPath, cancellationToken).ConfigureAwait(false);
        switch (operation.Kind)
        {
            case FileMutationKind.Create:
                if (!string.Equals(current, operation.ContentAfter, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot undo create for '{operation.RelativePath}': file content does not match recorded after-state.");
                }

                fileSystem.TryDeleteFile(fullPath);
                return;

            case FileMutationKind.Replace:
                if (!string.Equals(current, operation.ContentAfter, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot undo replace for '{operation.RelativePath}': file content does not match recorded after-state.");
                }

                if (operation.ContentBefore is null)
                {
                    fileSystem.TryDeleteFile(fullPath);
                    return;
                }

                await fileSystem.WriteAllTextAsync(fullPath, operation.ContentBefore, cancellationToken).ConfigureAwait(false);
                return;

            case FileMutationKind.Delete:
                if (current is not null)
                {
                    throw new InvalidOperationException($"Cannot undo delete for '{operation.RelativePath}': file already exists.");
                }

                await fileSystem.WriteAllTextAsync(fullPath, operation.ContentAfter, cancellationToken).ConfigureAwait(false);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation.Kind, "Unsupported mutation kind.");
        }
    }

    /// <summary>
    /// Re-applies a mutation during redo.
    /// </summary>
    public async Task ApplyForwardAsync(string workspaceRoot, FileMutationOperation operation, CancellationToken cancellationToken)
    {
        var resolver = new WorkspacePathResolver(pathService);
        var root = pathService.GetFullPath(workspaceRoot);
        var context = CreateBypassContext(root);
        var fullPath = resolver.ResolvePath(context, operation.RelativePath);

        var current = await fileSystem.ReadAllTextIfExistsAsync(fullPath, cancellationToken).ConfigureAwait(false);
        switch (operation.Kind)
        {
            case FileMutationKind.Create:
            case FileMutationKind.Replace:
                if (operation.ContentBefore is not null
                    && !string.Equals(current, operation.ContentBefore, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot redo mutation for '{operation.RelativePath}': file content does not match recorded before-state.");
                }

                if (operation.ContentBefore is null && current is not null)
                {
                    throw new InvalidOperationException(
                        $"Cannot redo create for '{operation.RelativePath}': file unexpectedly exists.");
                }

                await fileSystem.WriteAllTextAsync(fullPath, operation.ContentAfter, cancellationToken).ConfigureAwait(false);
                return;

            case FileMutationKind.Delete:
                if (operation.ContentBefore is null)
                {
                    throw new InvalidOperationException("Delete mutation requires ContentBefore snapshot for redo.");
                }

                if (!string.Equals(current, operation.ContentBefore, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cannot redo delete for '{operation.RelativePath}': file content does not match expected before-state.");
                }

                fileSystem.TryDeleteFile(fullPath);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(operation), operation.Kind, "Unsupported mutation kind.");
        }
    }

    private static ToolExecutionContext CreateBypassContext(string workspaceRoot)
        => new(
            SessionId: "mutation-replay",
            TurnId: "mutation-replay",
            WorkspaceRoot: workspaceRoot,
            WorkingDirectory: workspaceRoot,
            PermissionMode: PermissionMode.DangerFullAccess,
            OutputFormat: OutputFormat.Text,
            EnvironmentVariables: null,
            AllowedTools: null,
            AllowDangerousBypass: true,
            IsInteractive: false);
}
