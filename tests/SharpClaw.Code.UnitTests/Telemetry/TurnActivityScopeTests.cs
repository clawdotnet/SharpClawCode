using System.Diagnostics;
using FluentAssertions;
using SharpClaw.Code.Telemetry.Diagnostics;

namespace SharpClaw.Code.UnitTests.Telemetry;

/// <summary>
/// Verifies privacy controls on turn activity tags.
/// </summary>
public sealed class TurnActivityScopeTests
{
    /// <summary>
    /// Ensures opted-in prompt previews redact common credential forms before export.
    /// </summary>
    [Fact]
    public void Prompt_preview_should_be_redacted_and_truncated()
    {
        Activity? completed = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SharpClawActivitySource.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (Equals(activity.GetTagItem("sharpclaw.session.id"), "session"))
                {
                    completed = activity;
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        using (new TurnActivityScope(
            "session",
            "turn",
            "password=hunter2 api_key=top-secret sk-abcdefghijklm trailing words",
            promptPreviewMaxLength: 48))
        {
        }

        completed.Should().NotBeNull();
        var preview = completed!.GetTagItem("sharpclaw.turn.prompt_preview")?.ToString();
        preview.Should().NotContain("hunter2").And.NotContain("top-secret").And.NotContain("sk-abcdefghijklm");
        preview.Should().Contain("[REDACTED]");
        preview!.Length.Should().BeLessThanOrEqualTo(51);
    }

    /// <summary>
    /// Ensures omitting the prompt avoids creating any prompt-content tag.
    /// </summary>
    [Fact]
    public void Prompt_preview_should_be_absent_when_not_opted_in()
    {
        Activity? completed = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == SharpClawActivitySource.SourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (Equals(activity.GetTagItem("sharpclaw.session.id"), "session"))
                {
                    completed = activity;
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        using (new TurnActivityScope("session", "turn"))
        {
        }

        completed.Should().NotBeNull();
        completed!.GetTagItem("sharpclaw.turn.prompt_preview").Should().BeNull();
    }
}
