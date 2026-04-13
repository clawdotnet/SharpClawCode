using System.Diagnostics;
using System.Text.Json;
using SharpClaw.Code.Telemetry.Diagnostics;

namespace SharpClaw.Code.Telemetry.Export;

/// <summary>
/// Writes completed Activity spans as NDJSON lines to a file for offline analysis.
/// Register as an ActivityListener to capture spans.
/// </summary>
public sealed class NdjsonTraceFileSink : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly ActivityListener _listener;
    private readonly object _writeLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance targeting the specified file path.
    /// </summary>
    /// <param name="filePath">The NDJSON output file path.</param>
    public NdjsonTraceFileSink(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _writer = new StreamWriter(filePath, append: true) { AutoFlush = true, NewLine = "\n" };
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SharpClawActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = OnActivityStopped,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    private void OnActivityStopped(Activity activity)
    {
        var entry = new
        {
            traceId = activity.TraceId.ToString(),
            spanId = activity.SpanId.ToString(),
            parentSpanId = activity.ParentSpanId.ToString(),
            operationName = activity.OperationName,
            startTimeUtc = activity.StartTimeUtc,
            durationMs = activity.Duration.TotalMilliseconds,
            status = activity.Status.ToString(),
            tags = activity.Tags.ToDictionary(t => t.Key, t => t.Value),
        };
        var line = JsonSerializer.Serialize(entry);

        lock (_writeLock)
        {
            if (!_disposed)
            {
                _writer.WriteLine(line);
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_writeLock)
        {
            _disposed = true;
        }

        _listener.Dispose();
        _writer.Dispose();
    }
}
