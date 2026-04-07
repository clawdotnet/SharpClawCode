using SharpClaw.Code.Infrastructure.Abstractions;

namespace SharpClaw.Code.Infrastructure.Services;

/// <summary>
/// Uses the host system clock for UTC timestamps.
/// </summary>
public sealed class SystemClock : ISystemClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
