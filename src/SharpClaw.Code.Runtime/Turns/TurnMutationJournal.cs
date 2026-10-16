using System.Collections.Concurrent;
namespace SharpClaw.Code.Runtime.Turns;
/// <summary>Shares a turn's recorder between execution and durable failure finalization, without implicit ambient state.</summary>
public sealed class TurnMutationJournal
{
    private readonly ConcurrentDictionary<(string Session, string Turn), TurnMutationAccumulator> active = new();
    /// <summary>Begins capture owned by the durable runtime. End must run in its finally block.</summary>
    public TurnMutationAccumulator Begin(string sessionId, string turnId) => active.GetOrAdd((sessionId, turnId), _ => new());
    /// <summary>Gets an active recorder; standalone runners can use their own recorder when none exists.</summary>
    public TurnMutationAccumulator? Find(string sessionId, string turnId) => active.TryGetValue((sessionId, turnId), out var recorder) ? recorder : null;
    /// <summary>Releases the completed or failed logical turn's capture.</summary>
    public void End(string sessionId, string turnId) => active.TryRemove((sessionId, turnId), out _);
}
