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

### Authenticated approval transports

Embedded hosts can enable approval identity independently from provider auth. The current runtime supports:

- **trusted-header** mode, where an upstream host supplies subject, tenant, role, and scope headers
- **OIDC** mode, where the embedded/admin HTTP surface validates a bearer token against discovery + JWKS metadata

`ConfiguredApprovalIdentityService` resolves the current `ApprovalPrincipal`, and `AuthenticatedApprovalTransport` approves or denies requests before the console/non-interactive transports are considered.

Two host flags matter:

- `RequireForAdmin`: admin routes must present a valid approval identity
- `RequireAuthenticatedApprovals`: approval-required operations are denied when no valid approval identity is present, even if the caller is otherwise interactive

Authenticated approvals are tenant-bound. If the runtime host context carries `TenantId`, an approval principal with a different tenant is denied before any remembered approval or console fallback path is used.

### Remembered approvals

**`ISessionApprovalMemory`** (**`SessionApprovalMemory`**) stores **approved** decisions in a **process-scoped** dictionary keyed by **`sessionId`** and a composite key (**tool name, scope, source, working directory, originating plugin id/trust**).

When a rule returns **`RequireApproval`** with **`CanRememberApproval`**, an approved outcome may be **`Store`**d and reused via **`TryGet`**. In embedded-host flows, the remembered approval remains scoped to the current session and tenant context.

## Tool execution context

**`ToolExecutionContext`** (`src/SharpClaw.Code.Tools/Models/ToolExecutionContext.cs`) carries **`IsInteractive`** (default **true** on the record). Parity tests set **`interactive: true/false`** to exercise approval vs deny paths.

**`ToolExecutor`** passes **`TrustedPluginNames`** and **`TrustedMcpServerNames`** into **`PermissionEvaluationContext`** when supplied.
