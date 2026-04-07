using SharpClaw.Code.Protocol.Operational;
using SharpClaw.Code.Sessions.Abstractions;

namespace SharpClaw.Code.Runtime.Diagnostics.Checks;

/// <summary>
/// Exercises the session store read path for the workspace.
/// </summary>
public sealed class SessionStoreHealthCheck(ISessionStore sessionStore) : IOperationalCheck
{
    /// <inheritdoc />
    public string Id => "session.store";

    /// <inheritdoc />
    public async Task<OperationalCheckItem> ExecuteAsync(OperationalDiagnosticsContext context, CancellationToken cancellationToken)
    {
        try
        {
            var latest = await sessionStore.GetLatestAsync(context.NormalizedWorkspacePath, cancellationToken).ConfigureAwait(false);
            return new OperationalCheckItem(
                Id,
                OperationalCheckStatus.Ok,
                "Session store read succeeded.",
                latest is null ? "No sessions yet." : $"Latest session: {latest.Id} ({latest.State}).");
        }
        catch (Exception exception)
        {
            return new OperationalCheckItem(
                Id,
                OperationalCheckStatus.Error,
                "Session store read failed.",
                exception.Message);
        }
    }
}
