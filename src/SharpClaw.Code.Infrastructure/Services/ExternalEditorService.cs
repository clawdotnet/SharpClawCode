using SharpClaw.Code.Infrastructure.Abstractions;

namespace SharpClaw.Code.Infrastructure.Services;

/// <inheritdoc />
public sealed class ExternalEditorService(
    IShellExecutor shellExecutor,
    IFileSystem fileSystem,
    IPathService pathService) : IExternalEditorService
{
    /// <inheritdoc />
    public async Task<string?> ComposeAsync(string? initialContent, CancellationToken cancellationToken)
    {
        var editor =
            Environment.GetEnvironmentVariable("SHARPCLAW_EDITOR")
            ?? Environment.GetEnvironmentVariable("VISUAL")
            ?? Environment.GetEnvironmentVariable("EDITOR");
        if (string.IsNullOrWhiteSpace(editor))
        {
            return null;
        }

        var tempDir = pathService.Combine(pathService.GetTempPath(), "sharpclaw-compose");
        fileSystem.CreateDirectory(tempDir);
        var tempFile = pathService.Combine(tempDir, $"compose-{Guid.NewGuid():N}.md");
        await fileSystem.WriteAllTextAsync(tempFile, initialContent ?? string.Empty, cancellationToken).ConfigureAwait(false);

        var escapedPath = tempFile.Replace("\"", "\\\"", StringComparison.Ordinal);
        var command = $"{editor} \"{escapedPath}\"";

        try
        {
            await shellExecutor.ExecuteAsync(command, workingDirectory: null, environmentVariables: null, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            fileSystem.TryDeleteFile(tempFile);
            throw;
        }

        var text = await fileSystem.ReadAllTextIfExistsAsync(tempFile, cancellationToken).ConfigureAwait(false);
        fileSystem.TryDeleteFile(tempFile);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
