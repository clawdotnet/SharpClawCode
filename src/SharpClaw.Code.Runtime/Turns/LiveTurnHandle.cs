using System.Threading.Channels;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Runtime.Turns;

/// <summary>Owns one turn's cancellation, bounded steering queue, and bounded live progress stream.</summary>
public sealed class LiveTurnHandle : IDisposable
{
    private readonly CancellationTokenSource cancellation;
    private readonly Action restore;
    private readonly object gate = new();
    private readonly Queue<string> steering = new();
    private readonly Channel<RuntimeEventEnvelope> events = Channel.CreateBounded<RuntimeEventEnvelope>(new BoundedChannelOptions(256)
    { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true, SingleWriter = false });
    private bool disposed;
    private bool steeringClosed;
    private int cancellationCalls;

    internal LiveTurnHandle(CancellationToken token, Action restore)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        CancellationToken = cancellation.Token;
        this.restore = restore;
    }

    /// <summary>The token to pass through the entire runtime invocation, including approvals and recovery.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Requests cancellation of this turn only.</summary>
    public void Cancel() => RequestCancellation(disposing: false);

    /// <summary>Queues a correction without interrupting an in-flight tool. Returns false when closed, cancelled, full, or oversized.</summary>
    public bool TrySteer(string message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > 16_000) return false;
        lock (gate)
        {
            if (disposed || steeringClosed || CancellationToken.IsCancellationRequested || steering.Count >= 16) return false;
            steering.Enqueue(message.Trim());
            return true;
        }
    }

    /// <summary>Returns unconsumed steering so the caller can retain it as follow-up work after the turn settles.</summary>
    public IReadOnlyList<string> GetPendingSteering()
    {
        lock (gate) return steering.ToArray();
    }

    /// <summary>Atomically closes correction intake and returns pending follow-up work after execution settles.</summary>
    public IReadOnlyList<string> CloseSteering()
    {
        lock (gate)
        {
            steeringClosed = true;
            return steering.ToArray();
        }
    }

    internal string? PeekSteering() { lock (gate) return steering.TryPeek(out var message) ? message : null; }
    internal void CompleteSteering() { lock (gate) steering.Dequeue(); }
    internal void Publish(RuntimeEventEnvelope envelope) => events.Writer.TryWrite(envelope);

    /// <summary>Reads progress for this flow only. Slow viewers may miss progress; durable runtime events remain authoritative.</summary>
    public IAsyncEnumerable<RuntimeEventEnvelope> ReadEventsAsync(CancellationToken token = default) => events.Reader.ReadAllAsync(token);

    /// <inheritdoc />
    public void Dispose() => RequestCancellation(disposing: true);

    private void RequestCancellation(bool disposing)
    {
        lock (gate)
        {
            if (disposed) return;
            if (disposing) disposed = true;
            steeringClosed = true;
            cancellationCalls++;
        }
        try
        {
            // Token callbacks may call back into this handle, including from another thread.
            cancellation.Cancel();
        }
        finally
        {
            lock (gate)
            {
                cancellationCalls--;
                // Dispose only after all concurrent Cancel calls have left the source.
                if (disposed && cancellationCalls == 0) cancellation.Dispose();
            }
            if (disposing)
            {
                events.Writer.TryComplete();
                restore();
            }
        }
    }
}
