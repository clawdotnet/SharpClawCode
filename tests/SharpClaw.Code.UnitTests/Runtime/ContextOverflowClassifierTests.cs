using FluentAssertions;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Providers.Models;

namespace SharpClaw.Code.UnitTests.Runtime;

public sealed class ContextOverflowClassifierTests
{
    [Theory]
    [InlineData("context_length_exceeded", true)]
    [InlineData("This model's maximum context length is 8192 tokens", true)]
    [InlineData("prompt is too long: 200001 tokens", true)]
    [InlineData("prompt is too long: 140100 tokens", true)]
    [InlineData("HTTP 429: token rate limit exceeded", false)]
    [InlineData("HTTP 401: invalid API key", false)]
    [InlineData("HTTP 500: server failed", false)]
    public void Only_explicit_context_rejections_allow_recovery(string message, bool expected)
    {
        var evt = new ProviderEvent("event", "request", "failed", DateTimeOffset.UtcNow, message, true, null);
        (ProviderStreamFailureClassifier.ClassifyFailedEvent(evt) == ProviderFailureKind.ContextWindowExceeded).Should().Be(expected);
        ProviderStreamFailureClassifier.IsContextOverflow(new InvalidOperationException(message)).Should().Be(expected);
    }
}
