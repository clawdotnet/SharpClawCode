using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Tools.Abstractions;
using SharpClaw.Code.Tools.BuiltIn;
using SharpClaw.Code.Tools.Models;
namespace SharpClaw.Code.Runtime.Verification.Tools;
/// <summary>Exposes the trusted verification worker through the normal shell-execution permission boundary.</summary>
public sealed class VerifyWorkspaceTool(IVerificationService verification) : SharpClawToolBase, IToolPermissionDeniedResultFactory
{
    /// <summary>The stable tool name.</summary>
    public const string ToolName = "verify_workspace";
    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(ToolName, "Build/test a .NET workspace. Executes project code and requires shell permission; restore is opt-in.", ApprovalScope.ShellExecution, false, true, nameof(VerifyWorkspaceArguments), "scope, target, configuration, targetFramework, restore, changedPaths and timeoutSeconds.", ["dotnet", "verification"], """{"type":"object","properties":{"scope":{"type":"string","enum":["affected","build","tests","all"]},"target":{"type":"string"},"configuration":{"type":"string"},"targetFramework":{"type":"string"},"restore":{"type":"boolean","default":false},"changedPaths":{"type":"array","items":{"type":"string"}},"timeoutSeconds":{"type":"integer","minimum":1,"maximum":3600}},"additionalProperties":false}""");
    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments(request, ProtocolJsonContext.Default.VerifyWorkspaceArguments);
        var report = await verification.VerifyAsync(new(context.WorkspaceRoot, arguments.Scope, arguments.Target, arguments.Configuration, arguments.TargetFramework, arguments.Restore, arguments.ChangedPaths, arguments.TimeoutSeconds), cancellationToken).ConfigureAwait(false);
        return CreateTypedResult(context, request, report, ProtocolJsonContext.Default.VerificationRunReport, report.Status is VerificationStatus.Passed or VerificationStatus.Skipped, report.Summary);
    }
    /// <inheritdoc />
    public ToolResult CreateDeniedResult(ToolExecutionContext context, ToolExecutionRequest request, PermissionDecision decision)
    {
        VerifyWorkspaceArguments arguments;
        try { arguments = DeserializeArguments(request, ProtocolJsonContext.Default.VerifyWorkspaceArguments); }
        catch (System.Text.Json.JsonException) { arguments = new(); }
        var now = DateTimeOffset.UtcNow;
        var report = new VerificationRunReport(context.WorkspaceRoot, arguments.Target, arguments.Scope, VerificationStatus.Failed, VerificationFailureReason.PermissionDenied, now, now, 0, [], null, null, null, decision.Reason ?? "Verification execution was denied.");
        return CreateTypedResult(context, request, report, ProtocolJsonContext.Default.VerificationRunReport, false, report.Summary);
    }
}
