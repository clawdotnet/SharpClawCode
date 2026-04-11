using Microsoft.Extensions.Options;

namespace SharpClaw.Code.Telemetry;

/// <summary>
/// Validates <see cref="TelemetryOptions"/> after binding.
/// </summary>
public sealed class TelemetryOptionsValidator : IValidateOptions<TelemetryOptions>
{
    /// <summary>
    /// Minimum allowed ring buffer capacity.
    /// </summary>
    public const int MinimumBufferCapacity = 64;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, TelemetryOptions options)
    {
        if (options.RuntimeEventRingBufferCapacity < MinimumBufferCapacity)
        {
            return ValidateOptionsResult.Fail(
                $"TelemetryOptions.RuntimeEventRingBufferCapacity must be at least {MinimumBufferCapacity} (was {options.RuntimeEventRingBufferCapacity}).");
        }

        return ValidateOptionsResult.Success;
    }
}
