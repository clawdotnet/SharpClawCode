using System.Collections.Concurrent;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Permissions.Models;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Commands;

/// <summary>Routes REPL approvals through explicit request IDs while keeping a single owner of terminal input.</summary>
public sealed class ReplApprovalBroker(IReplTerminal terminal, IApprovalPrincipalAccessor principalAccessor) : IApprovalTransport
{
    private readonly AsyncLocal<ReplApprovalSession?> current = new();

    /// <summary>Begins approval routing for one REPL turn.</summary>
    public ReplApprovalSession Begin()
    {
        var previous = current.Value;
        var session = new ReplApprovalSession(() => current.Value = previous);
        current.Value = session;
        return session;
    }

    /// <inheritdoc />
    public bool CanHandle(PermissionEvaluationContext context)
        => context.IsInteractive && current.Value is not null
            && principalAccessor.CurrentStatus is not { RequireAuthenticatedApprovals: true, Mode: not ApprovalAuthMode.Disabled };

    /// <inheritdoc />
    public async Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, PermissionEvaluationContext context, CancellationToken cancellationToken)
    {
        var session = current.Value ?? throw new InvalidOperationException("No active REPL approval session.");
        var id = Guid.NewGuid().ToString("N")[..12];
        var response = session.Register(id);
        try
        {
            if (request.CanRememberDecision) terminal.WriteInfo("This approval may be remembered for the rest of the session.");
            terminal.WriteInfo($"Approval {id}: {request.ToolName} ({request.Scope}). {request.Prompt}\nRespond with /approve {id} yes or /approve {id} no.");
            var approved = await response.WaitAsync(cancellationToken).ConfigureAwait(false);
            return new(request.Scope, approved, request.RequestedBy, "repl", approved ? "Approved explicitly in the REPL." : "Denied in the REPL.", DateTimeOffset.UtcNow, null,
                approved && request.CanRememberDecision);
        }
        finally { session.Remove(id); }
    }
}

/// <summary>Owns pending approvals for one live turn. Ordinary queued input never resolves an approval.</summary>
public sealed class ReplApprovalSession : IDisposable
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource<bool>> pending = new(StringComparer.Ordinal);
    private readonly Action restore;
    private int inputClosed;
    internal ReplApprovalSession(Action restore) => this.restore = restore;
    internal Task<bool> Register(string id)
    {
        var source = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = source;
        if (Volatile.Read(ref inputClosed) != 0) source.TrySetResult(false);
        return source.Task;
    }
    internal void Remove(string id) => pending.TryRemove(id, out _);

    /// <summary>Resolves exactly the named pending request; stale or unknown IDs are rejected.</summary>
    public bool TryRespond(string id, bool approved) => pending.TryGetValue(id, out var source) && source.TrySetResult(approved);

    /// <summary>Denies pending and future requests after terminal input ends; an unattended REPL cannot hang on an approval.</summary>
    public void CloseInput()
    {
        Volatile.Write(ref inputClosed, 1);
        foreach (var source in pending.Values) source.TrySetResult(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var source in pending.Values) source.TrySetCanceled();
        pending.Clear();
        restore();
    }
}
