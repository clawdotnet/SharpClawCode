namespace SharpClaw.Code.Infrastructure.Abstractions;

/// <summary>
/// Provides the current system time for runtime and storage workflows.
/// </summary>
public interface ISystemClock
{
    /// <summary>
    /// Gets the current UTC timestamp.
    /// </summary>
    DateTimeOffset UtcNow { get; }
}
