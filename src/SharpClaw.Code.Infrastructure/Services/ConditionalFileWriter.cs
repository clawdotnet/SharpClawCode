using SharpClaw.Code.Infrastructure.Abstractions;

namespace SharpClaw.Code.Infrastructure.Services;

/// <summary>Publishes a staged UTF-8 replacement from the same directory, avoiding partially written target files.</summary>
public sealed class ConditionalFileWriter(IFileSystem fileSystem) : IConditionalFileWriter
{
    /// <inheritdoc />
    public async Task<bool> WriteAsync(string path, string expectedText, string newText, CancellationToken cancellationToken)
    {
        var temporaryPath = Path.Combine(Path.GetDirectoryName(path)!, $".sharpclaw-edit-{Guid.NewGuid():N}.tmp");
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                // Apply the target's mode while the staged file is still empty, so replacement text is never exposed more widely.
                await fileSystem.WriteAllTextAsync(temporaryPath, string.Empty, cancellationToken).ConfigureAwait(false);
                try { File.SetUnixFileMode(temporaryPath, File.GetUnixFileMode(path)); }
                catch (FileNotFoundException) { return false; }
            }
            await fileSystem.WriteAllTextAsync(temporaryPath, newText, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(await fileSystem.ReadAllTextIfExistsAsync(path, cancellationToken).ConfigureAwait(false), expectedText, StringComparison.Ordinal)) return false;
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, path, overwrite: true);
            return true;
        }
        finally
        {
            fileSystem.TryDeleteFile(temporaryPath);
        }
    }
}
