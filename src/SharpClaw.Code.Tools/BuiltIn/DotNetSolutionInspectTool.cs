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

/// <summary>Inspect an approved .NET solution/project graph. Cold MSBuild evaluation requires execution permission.</summary>
public sealed class DotNetSolutionInspectTool(IDotNetWorkspaceSemanticService semantics, IPermissionPolicyEngine policy, IRuntimeEventPublisher? publisher = null, IRuntimeHostContextAccessor? host = null) : SharpClawToolBase
{
    /// <summary>The stable tool name.</summary>
    public const string ToolName = "dotnet_solution_inspect";
    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(ToolName, "Inspect an approved .NET solution/project graph. Cold MSBuild evaluation requires execution permission.", ApprovalScope.ToolExecution, false, false, nameof(CSharpToolArguments), "Typed semantic selectors and workspace context.", ["dotnet", "semantic"], """{"type":"object","properties":{"target":{"type":"string"},"configuration":{"type":"string","default":"Debug"},"targetFramework":{"type":"string"}},"required":[],"additionalProperties":false}""");
    /// <inheritdoc />
    public override async Task<ToolResult> ExecuteAsync(ToolExecutionContext context, ToolExecutionRequest request, CancellationToken cancellationToken)
    {
        var arguments = DeserializeArguments(request, ProtocolJsonContext.Default.CSharpToolArguments);
        var authorization = new DotNetEvaluationAuthorization(policy, context, request, publisher, host);
        var result = await semantics.InspectSolutionAsync(arguments.Workspace(context.WorkspaceRoot), authorization, cancellationToken).ConfigureAwait(false);
        return CreateTypedResult(context, request, result, ProtocolJsonContext.Default.DotNetSolutionSummary, result.Status is DotNetWorkspaceStatus.Ready or DotNetWorkspaceStatus.Partial, System.Text.Json.JsonSerializer.Serialize(result, ProtocolJsonContext.Default.DotNetSolutionSummary));
    }
}
