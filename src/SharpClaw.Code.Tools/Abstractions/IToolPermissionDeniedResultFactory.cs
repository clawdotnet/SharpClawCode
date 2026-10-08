using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;
namespace SharpClaw.Code.Tools.Abstractions;
/// <summary>Allows a tool to preserve its typed operation report when execution is denied before invocation.</summary>
public interface IToolPermissionDeniedResultFactory
{
    /// <summary>Creates a failed result without executing the tool or any project code.</summary>
    ToolResult CreateDeniedResult(ToolExecutionContext context, ToolExecutionRequest request, PermissionDecision decision);
}
