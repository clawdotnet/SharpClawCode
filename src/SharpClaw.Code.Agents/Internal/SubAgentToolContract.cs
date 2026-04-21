using SharpClaw.Code.Protocol.Models;
using SharpClaw.Code.Tools.BuiltIn;

namespace SharpClaw.Code.Agents.Internal;

internal static class SubAgentToolContract
{
    public const string ToolName = "use_subagents";
    public const int MaxTasks = 3;

    public static readonly string[] AllowedReadOnlyTools =
    [
        ReadFileTool.ToolName,
        GlobSearchTool.ToolName,
        GrepSearchTool.ToolName,
        WorkspaceSearchTool.ToolName,
        SymbolSearchTool.ToolName,
        ToolSearchTool.ToolName,
    ];

    public static readonly ProviderToolDefinition Definition = new(
        ToolName,
        "Delegate up to 3 bounded read-only repository investigation tasks to subagents. Use this for parallel codebase research, not for edits or shell commands.",
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "tasks": {
              "type": "array",
              "minItems": 1,
              "maxItems": 3,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "goal": { "type": "string" },
                  "expectedOutput": { "type": "string" },
                  "constraints": {
                    "type": "array",
                    "items": { "type": "string" }
                  }
                },
                "required": ["goal", "expectedOutput"]
              }
            }
          },
          "required": ["tasks"]
        }
        """);
}
