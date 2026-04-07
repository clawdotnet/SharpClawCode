using System.IO;

namespace SharpClaw.Code.Infrastructure.Services;

/// <summary>
/// Holds an OS-level exclusive <see cref="FileStream"/> lock until disposed.
/// </summary>
internal sealed class ExclusiveFileLock : IAsyncDisposable, IDisposable
{
    private FileStream? _stream;

    public ExclusiveFileLock(FileStream stream)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_stream is null)
        {
            return ValueTask.CompletedTask;
        }

        var s = _stream;
        _stream = null;
        return s.DisposeAsync();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_stream is not null)
        {
            _stream.Dispose();
            _stream = null;
        }
    }
}
