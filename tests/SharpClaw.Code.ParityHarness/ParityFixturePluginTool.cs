using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.BuiltIn;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.ParityHarness;

/// <summary>
/// Synthetic plugin-originated tool used to validate trust and execution wiring without a packaged plugin.
/// </summary>
internal sealed class ParityFixturePluginTool : SharpClawToolBase
{
    /// <summary>
    /// Stable tool name for parity scenarios.
    /// </summary>
    public const string ToolName = "parity_fixture_echo";

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        ToolName,
        "Echo fixture used for plugin-sourced tool parity.",
        ApprovalScope.ToolExecution,
        IsDestructive: false,
        RequiresApproval: false,
        InputTypeName: nameof(ParityFixtureEchoArgs),
        InputDescription: "JSON object with message.",
        Tags: ["parity", "fixture"]);

    /// <inheritdoc />
    public override PluginToolSource? PluginSource { get; } = new("parity-fixture-plugin", PluginTrustLevel.WorkspaceTrusted);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var args = DeserializeArguments<ParityFixtureEchoArgs>(request);
        return Task.FromResult(CreateSuccessResult(context, request, args.Message, args));
    }
}

/// <summary>
/// Arguments for <see cref="ParityFixturePluginTool"/>.
/// </summary>
internal sealed record ParityFixtureEchoArgs(string Message);
