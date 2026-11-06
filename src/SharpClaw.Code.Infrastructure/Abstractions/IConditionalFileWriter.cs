namespace SharpClaw.Code.Infrastructure.Abstractions;

/// <summary>Stages a complete replacement and checks its text precondition immediately before publishing it.</summary>
public interface IConditionalFileWriter
{
    /// <summary>Returns false on stale content. Cancellation before publication leaves the target untouched. Callers coordinate cooperating writers with a workspace lock; this is not an OS compare-and-swap against external editors.</summary>
    Task<bool> WriteAsync(string path, string expectedText, string newText, CancellationToken cancellationToken);
}
