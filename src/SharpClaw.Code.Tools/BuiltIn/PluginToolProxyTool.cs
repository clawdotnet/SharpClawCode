using SharpClaw.Code.Plugins.Abstractions;
using SharpClaw.Code.Plugins.Models;
using SharpClaw.Code.Protocol.Enums;
using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.Models;

namespace SharpClaw.Code.Tools.BuiltIn;

/// <summary>
/// Adapts an enabled plugin tool into the normal SharpClaw tool execution model.
/// </summary>
internal sealed class PluginToolProxyTool(IPluginManager pluginManager, PluginToolDescriptor descriptor) : SharpClawToolBase
{
    private static string BuildInputDescription(PluginToolDescriptor descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor.InputSchemaJson))
        {
            return descriptor.InputDescription;
        }

        return $"{descriptor.InputDescription}{Environment.NewLine}Input schema (JSON):{Environment.NewLine}{descriptor.InputSchemaJson}";
    }

    /// <inheritdoc />
    public override ToolDefinition Definition { get; } = new(
        Name: descriptor.Name,
        Description: descriptor.Description,
        ApprovalScope: ApprovalScope.ToolExecution,
        IsDestructive: descriptor.IsDestructive,
        RequiresApproval: descriptor.RequiresApproval,
        InputTypeName: string.IsNullOrWhiteSpace(descriptor.InputTypeName) ? "PluginToolArguments" : descriptor.InputTypeName!,
        InputDescription: BuildInputDescription(descriptor),
        Tags: [.. descriptor.Tags ?? [], "plugin", descriptor.SourcePluginId ?? "external"]);

    /// <inheritdoc />
    public override PluginToolSource? PluginSource =>
        string.IsNullOrWhiteSpace(descriptor.SourcePluginId)
            ? null
            : new PluginToolSource(descriptor.SourcePluginId, descriptor.Trust);

    /// <inheritdoc />
    public override Task<ToolResult> ExecuteAsync(
        ToolExecutionContext context,
        ToolExecutionRequest request,
        CancellationToken cancellationToken)
        => pluginManager.ExecuteToolAsync(context.WorkspaceRoot, descriptor.Name, request, cancellationToken);
}
