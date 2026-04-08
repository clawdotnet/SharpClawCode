using System.Text.Json;
using FluentAssertions;
using SharpClaw.Code.Cli.Rendering;
using SharpClaw.Code.Protocol.Commands;
using SharpClaw.Code.Protocol.Enums;

namespace SharpClaw.Code.IntegrationTests.Smoke;

/// <summary>
/// Verifies JSON command rendering stays envelope-shaped for automation consumers.
/// </summary>
public sealed class JsonOutputRendererTests
{
    /// <summary>
    /// Ensures machine-readable command output keeps a stable outer schema even when data payloads are present.
    /// </summary>
    [Fact]
    public async Task RenderCommandResultAsync_should_wrap_data_payload_in_stable_envelope()
    {
        var renderer = new JsonOutputRenderer();
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            await renderer.RenderCommandResultAsync(
                new CommandResult(
                    Succeeded: true,
                    ExitCode: 0,
                    OutputFormat: OutputFormat.Text,
                    Message: "ok",
                    DataJson: """{"version":"1.2.3"}"""),
                CancellationToken.None);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        root.GetProperty("succeeded").GetBoolean().Should().BeTrue();
        root.GetProperty("exitCode").GetInt32().Should().Be(0);
        root.GetProperty("outputFormat").GetString().Should().Be("json");
        root.GetProperty("message").GetString().Should().Be("ok");
        root.GetProperty("data").GetProperty("version").GetString().Should().Be("1.2.3");
    }

    /// <summary>
    /// Ensures malformed data payloads are surfaced without crashing JSON rendering.
    /// </summary>
    [Fact]
    public async Task RenderCommandResultAsync_should_fall_back_to_data_raw_for_invalid_payloads()
    {
        var renderer = new JsonOutputRenderer();
        using var writer = new StringWriter();
        var originalOut = Console.Out;
        Console.SetOut(writer);

        try
        {
            await renderer.RenderCommandResultAsync(
                new CommandResult(
                    Succeeded: false,
                    ExitCode: 1,
                    OutputFormat: OutputFormat.Text,
                    Message: "bad",
                    DataJson: "{not-json"),
                CancellationToken.None);
        }
        finally
        {
            Console.SetOut(originalOut);
        }

        using var document = JsonDocument.Parse(writer.ToString());
        var root = document.RootElement;

        root.GetProperty("succeeded").GetBoolean().Should().BeFalse();
        root.GetProperty("data").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("dataRaw").GetString().Should().Be("{not-json");
    }
}
