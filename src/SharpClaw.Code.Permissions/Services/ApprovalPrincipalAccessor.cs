using System.Threading;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Protocol.Models;

namespace SharpClaw.Code.Permissions.Services;

/// <summary>
/// Async-local approval principal scope used by embedded server and admin requests.
/// </summary>
public sealed class ApprovalPrincipalAccessor : IApprovalPrincipalAccessor
{
    private static readonly AsyncLocal<ApprovalScopeState?> Current = new();

    /// <inheritdoc />
    public ApprovalPrincipal? CurrentPrincipal => Current.Value?.Principal;

    /// <inheritdoc />
    public ApprovalAuthStatus? CurrentStatus => Current.Value?.Status;

    /// <inheritdoc />
    public IDisposable BeginScope(ApprovalPrincipal? principal, ApprovalAuthStatus? status)
    {
        var previous = Current.Value;
        Current.Value = new ApprovalScopeState(principal, status);
        return new Scope(previous);
    }

    private sealed record ApprovalScopeState(ApprovalPrincipal? Principal, ApprovalAuthStatus? Status);

    private sealed class Scope(ApprovalScopeState? previous) : IDisposable
    {
        private ApprovalScopeState? previousState = previous;

        public void Dispose()
        {
            Current.Value = previousState;
            previousState = null;
        }
    }
}
