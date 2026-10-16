using SharpClaw.Code.Memory.Abstractions;
using SharpClaw.Code.Permissions.Abstractions;
using SharpClaw.Code.Protocol.Abstractions;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Protocol.Serialization;
using SharpClaw.Code.Telemetry.Abstractions;
using SharpClaw.Code.Tools.Execution;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>Read compiler diagnostics without executing project analyzers or generators.</summary>
public sealed class CSharpDiagnosticsTool(IDotNetWorkspaceSemanticService semantics, IPermissionPolicyEngine policy, IRuntimeEventPublisher? publisher = null, IRuntimeHostContextAccessor? host = null) : SharpClawToolBase
{
    /// <summary>The stable tool name.</summary>
    public const string ToolName = "csharp_diagnostics";
    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(ToolName, "Read compiler diagnostics without executing project analyzers or generators.", ApprovalScope.ToolExecution, false, false, nameof(CSharpToolArguments), "Typed semantic selectors and workspace context.", ["dotnet", "semantic"], """{"type":"object","properties":{"target":{"type":"string"},"configuration":{"type":"string","default":"Debug"},"targetFramework":{"type":"string"},"project":{"type":"string"},"path":{"type":"string"},"severity":{"type":"string","enum":["error","warning","info","hidden"]},"code":{"type":"string"},"offset":{"type":"integer","minimum":0},"limit":{"type":"integer","minimum":1,"maximum":200}},"required":[],"additionalProperties":false}""");
    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments(request, ProtocolJsonContext.Default.CSharpToolArguments);
        var authorization = new DotNetEvaluationAuthorization(policy, context, request, publisher, host);
        var result = await semantics.GetDiagnosticsAsync(arguments.Workspace(context.WorkspaceRoot), arguments, authorization, cancellationToken).ConfigureAwait(false);
        return CreateTypedResult(context, request, result, ProtocolJsonContext.Default.CSharpDiagnosticsResult, result.Status is DotNetWorkspaceStatus.Ready or DotNetWorkspaceStatus.Partial, System.Text.Json.JsonSerializer.Serialize(result, ProtocolJsonContext.Default.CSharpDiagnosticsResult));
    }
}
