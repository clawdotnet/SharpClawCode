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
| Read snapshot | **`ReadFileSnapshotTool.ToolName`** (`read_file_snapshot`) |
| Edit snapshot | **`EditFileSnapshotTool.ToolName`** (`edit_file_snapshot`) |
| Glob search | **`GlobSearchTool.ToolName`** |
| Grep search | **`GrepSearchTool.ToolName`** |
| Bash / shell | **`BashTool.ToolName`** |
| Web search | **`WebSearchTool`** |
| Web fetch | **`WebFetchTool`** |
| Tool search | **`ToolSearchTool.ToolName`** (`tool_search`) |

Built-ins are registered as **`ISharpClawTool`** singletons; some also expose their concrete type through DI.

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
- **`AgentFrameworkBridge`** supplies registered tool definitions to **`ProviderBackedAgentKernel`**. Its tool loop dispatches calls through **`ToolCallDispatcher`** and the permission-aware **`IToolExecutor`**, including snapshot editing.

See [permissions.md](permissions.md) for gates on destructive and elevated tools.

Compiler-backed .NET tools are described in [.NET intelligence](dotnet-intelligence.md). They share normal tool discovery, explicit schemas, and a separate execution decision before cold MSBuild evaluation.

The permission-gated `verify_workspace` tool and CLI/REPL equivalents are documented in [Verification](verification.md).

The additive `read_file_snapshot` and `edit_file_snapshot` tools support bounded reads, stale-content rejection, and non-overlapping single-file edit batches through the same permission executor and mutation journal. Existing file-tool contracts are unchanged. See [snapshot editing](interactive-runtime.md#snapshot-aware-editing) for schemas, newline handling, and publication guarantees.

Runtime provider context uses bounded previews for large tool results while retaining complete results and events. The permission-aware `read_tool_output` tool retrieves full text from session artifacts. See [runtime resilience](interactive-runtime.md#bounded-tool-output-and-overflow-recovery).
