# Permissions

## Modes

**`PermissionMode`** (Protocol) is selected via CLI **`--permission-mode`** (`GlobalCliOptions`):

| CLI value (examples) | Enum |
|----------------------|------|
| `readOnly`, `read-only` | **`ReadOnly`** |
| `workspaceWrite`, `workspace-write`, `prompt`, `autoApproveSafe`, … | **`WorkspaceWrite`** |
| `dangerFullAccess`, `danger-full-access`, `fullTrust`, … | **`DangerFullAccess`** |

Default: **`WorkspaceWrite`**.

## Policy engine

**`PermissionPolicyEngine`** evaluates **`ToolExecutionRequest`** with **`PermissionEvaluationContext`** by running an ordered list of **`IPermissionRule`** instances:

1. **`WorkspaceBoundaryRule`**
2. **`AllowedToolRule`**
3. **`DangerousShellPatternRule`**
4. **`PluginTrustRule`**
5. **`McpTrustRule`**

Rules return **`PermissionRuleOutcome`**: Abstain, Allow, Deny, or **RequireApproval**.

If all rules abstain, **`EvaluateByModeAsync`** applies mode defaults (e.g. **ReadOnly** allows non-destructive **`ToolExecution`**; **WorkspaceWrite** auto-allows file/tool scope but may require approval for shell, network, session operations).

## Approvals

**`IApprovalService`** implementation **`ApprovalService`** delegates to:

- **`ConsoleApprovalService`** when **`PermissionEvaluationContext.IsInteractive`** is **true**
- **`NonInteractiveApprovalService`** otherwise — approvals are **denied** with a fixed reason

**`ConsoleApprovalService`** prints the tool name, scope, prompt, optional “may be remembered” line, and waits for **`y`/`yes`**.

### Remembered approvals

**`ISessionApprovalMemory`** (**`SessionApprovalMemory`**) stores **approved** decisions in a **process-scoped** dictionary keyed by **`sessionId`** and a composite key (**tool name, scope, source, working directory, originating plugin id/trust**).

When a rule returns **`RequireApproval`** with **`CanRememberApproval`**, an approved outcome may be **`Store`**d and reused via **`TryGet`**.

## Tool execution context

**`ToolExecutionContext`** (`src/SharpClaw.Code.Tools/Models/ToolExecutionContext.cs`) carries **`IsInteractive`** (default **true** on the record). Parity tests set **`interactive: true/false`** to exercise approval vs deny paths.

**`ToolExecutor`** passes **`TrustedPluginNames`** and **`TrustedMcpServerNames`** into **`PermissionEvaluationContext`** when supplied.
