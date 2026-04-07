# Tools

## Registry

**`IToolRegistry`** (`src/SharpClaw.Code.Tools/Abstractions/IToolRegistry.cs`) exposes:

- **`ListAsync`**, **`SearchAsync`**, **`GetRequiredAsync`** (all **`async`**; plugin tools are loaded with **`CancellationToken`**)

**`ToolRegistry`** merges:

1. All **`ISharpClawTool`** instances registered in DI (built-ins).
2. Optional **plugin** tool descriptors from **`IPluginManager.ListToolDescriptorsAsync`** wrapped as **`PluginToolProxyTool`** (only for enabled plugins — see `PluginManager`).

Registration: **`ToolsServiceCollectionExtensions.AddSharpClawTools`**.

## Execution and permissions

**`ToolExecutor`** (`IToolExecutor`):

1. Resolves the tool.
2. Builds **`ToolExecutionRequest`** + **`PermissionEvaluationContext`** (includes **`PluginToolSource`** when applicable).
3. Publishes **`ToolStartedEvent`**, **`PermissionRequestedEvent`**, **`PermissionResolvedEvent`**, **`ToolCompletedEvent`** through **`IRuntimeEventPublisher`** when registered.
4. Calls **`IPermissionPolicyEngine.EvaluateAsync`**; on allow, **`ISharpClawTool.ExecuteAsync`**.

## Built-in tools (current DI registration)

From **`ToolsServiceCollectionExtensions`**:

| Tool | Constant / name |
|------|------------------|
| Read file | **`ReadFileTool.ToolName`** |
| Write file | **`WriteFileTool.ToolName`** |
| Edit file | **`EditFileTool.ToolName`** |
| Glob search | **`GlobSearchTool.ToolName`** |
| Grep search | **`GrepSearchTool.ToolName`** |
| Bash / shell | **`BashTool.ToolName`** |
| Web search | **`WebSearchTool`** |
| Web fetch | **`WebFetchTool`** |
| Tool search | **`ToolSearchTool.ToolName`** (`tool_search`) |

Each built-in is registered both as a concrete singleton and as **`ISharpClawTool`**.

## Authoring a built-in tool

1. Subclass **`SharpClawToolBase`** (`src/SharpClaw.Code.Tools/BuiltIn/SharpClawToolBase.cs`).
2. Implement **`Definition`** as **`ToolDefinition`** (name, description, **`ApprovalScope`**, **`RequiresApproval`**, **`IsDestructive`**, input type name, tags).
3. Implement **`ExecuteAsync`**: parse arguments with **`DeserializeArguments<T>`**, return **`CreateSuccessResult`** / **`CreateFailureResult`**.
4. Register in **`ToolsServiceCollectionExtensions.AddSharpClawTools`**:
   - **`services.AddSingleton<YourTool>();`**
   - **`services.AddSingleton<ISharpClawTool>(sp => sp.GetRequiredService<YourTool>());`**

Use **Protocol** types for stable payloads where appropriate; tool JSON helpers live in **`ToolJson`** (`Tools/Utilities`).

## CLI / agent usage

- **`tool_search`** queries **`IToolRegistry.SearchAsync`**.
- End-to-end **LLM-driven tool use** in the default agent path is **not** wired through **`AgentFrameworkBridge`** today; **`AgentRunContext.ToolExecutor`** is available for future or alternate agent implementations.

See [permissions.md](permissions.md) for gates on destructive and elevated tools.
