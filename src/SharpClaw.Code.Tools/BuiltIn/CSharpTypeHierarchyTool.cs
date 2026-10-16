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

/// <summary>Inspect base types, interfaces, derived types and implementations.</summary>
public sealed class CSharpTypeHierarchyTool(IDotNetWorkspaceSemanticService semantics, IPermissionPolicyEngine policy, IRuntimeEventPublisher? publisher = null, IRuntimeHostContextAccessor? host = null) : SharpClawToolBase
{
    /// <summary>The stable tool name.</summary>
    public const string ToolName = "csharp_type_hierarchy";
    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(ToolName, "Inspect base types, interfaces, derived types and implementations.", ApprovalScope.ToolExecution, false, false, nameof(CSharpToolArguments), "Typed semantic selectors and workspace context.", ["dotnet", "semantic"], """{"type":"object","properties":{"target":{"type":"string"},"configuration":{"type":"string","default":"Debug"},"targetFramework":{"type":"string"},"name":{"type":"string"},"container":{"type":"string"},"path":{"type":"string"},"kind":{"type":"string"},"project":{"type":"string"},"signature":{"type":"string"},"line":{"type":"integer","minimum":1},"column":{"type":"integer","minimum":1},"limit":{"type":"integer","minimum":1,"maximum":200}},"required":["name"],"additionalProperties":false}""");
    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments(request, ProtocolJsonContext.Default.CSharpToolArguments);
        var authorization = new DotNetEvaluationAuthorization(policy, context, request, publisher, host);
        var result = await semantics.GetTypeHierarchyAsync(arguments.Workspace(context.WorkspaceRoot), arguments, authorization, cancellationToken).ConfigureAwait(false);
        return CreateTypedResult(context, request, result, ProtocolJsonContext.Default.CSharpTypeHierarchyResult, result.Resolution.Status == CSharpSymbolResolutionStatus.Resolved, System.Text.Json.JsonSerializer.Serialize(result, ProtocolJsonContext.Default.CSharpTypeHierarchyResult));
    }
}
