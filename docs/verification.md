# Verification

The runtime's `IVerificationService` produces typed build/test reports without a model or provider key. Its worker executes project code and is a trusted-host API; untrusted callers must use the permission-aware `verify_workspace` tool.

Scopes are `build` (selected target), `tests` (existing matching outputs), `all` (build then test), and `affected` (evaluated source owners plus reverse dependencies). Missing, deleted, unknown or configuration/build inputs conservatively select the whole target. Standalone affected calls inspect Git when no change list is supplied. A non-.NET workspace returns `Skipped`, with its reason.

Restore defaults off. Missing assets and missing SDKs remain explicit prerequisites; tests use `--no-build --no-restore` and `tests` scope does not build missing outputs. Build exit codes determine success even when no diagnostics were printed. Failed prerequisite builds prevent tests from starting.

Reports contain target/configuration context, timestamps, selected projects, step exit codes, deduplicated compiler/MSBuild diagnostics, test counters, failed cases and bounded logs. TRX results use a unique run directory, secure XML parsing, and project attribution; missing or malformed expected results fail verification. No test projects is an explicit skipped test section, not evidence that tests passed. Process timeouts and caller cancellation are distinct. A workspace file lock serializes overlapping verification runs.

SDKs, target framework assets and restored test dependencies are still required for these capabilities, including when the host is a self-contained binary. Fixture provisioning is explicit; verification never silently restores dependencies.


## CLI, REPL and agents

Use `sharpclaw verify --scope all --target MySolution.slnx`, `sharpclaw verify --scope build`, or `/verify --scope affected`. CLI and REPL share scope/target/restore/configuration/framework/timeout parsing. Existing global `--cwd`, `--permission-mode`, `--output-format` and scoped approval settings apply. JSON preserves the usual command envelope with a typed report in its data payload. Failed and denied checks return nonzero; non-.NET skips remain explicit in the report. `--restore` is the only CLI flag that permits restore.

`verify_workspace` publishes an explicit agent schema and uses `ShellExecution`, because MSBuild and tests execute project code. Read-only denies it before any process work. Noninteractive workspace-write calls require explicit shell auto-approval settings; existing approval budgets are honored. REPL permission and workflow overrides are preserved.

Prompt assembly never builds a workspace. It retains configured LSP metadata and consumes recently cached diagnostics from an authorized verification report. Embedded hosts should call `IToolExecutor.ExecuteAsync("verify_workspace", argumentsJson, callerContext, token)` for mediated execution; direct worker access is reserved for trusted, already-authorized orchestration.

## Automatic verification and repair

Automatic verification is disabled by default. Add this to workspace/user JSONC to enable it:

```json
{"verification":{"enabled":true,"scope":"affected","runAfterMutatingTurn":true,"maxRepairIterations":2,"allowRestore":false}}
```

Workspace fields override user fields. Request metadata overrides those defaults with `sharpclaw.verification.enabled`, `sharpclaw.verification.scope`, `sharpclaw.verification.maxRepairIterations`, and `sharpclaw.verification.allowRestore`. Invalid booleans, scopes and budgets fail validation. Budgets are 0–5; default 2 means one initial verification plus at most two agent repairs and their checks.

The loop runs after recorded source/project/build-input mutations in build mode. Plan, spec and research modes do not trigger it. Shell, plugin and external-agent writes without mutation records are outside initial automatic detection. No recorded mutation means no automatic check. Permission denial, missing prerequisites and skipped checks do not trigger agent repair; only build/test failures do.

Repair reuses the selected agent, session/turn, permissions, allowlist, history and ordered recorder. Bounded compiler diagnostics and failed test details form repair input. Usage, tool results and provider request/event pairs are aggregated across completed passes. A provider that throws before returning usage cannot supply a reliable token count; no usage is fabricated for that failed pass.

Verification and repair lifecycle events persist through the standard event infrastructure. A repair-start event consumes its budget before agent execution. Same-turn recovery retains consumed attempts and cannot increase the initial ceiling. Resuming the session with a new prompt starts a new logical turn.

All edits share one checkpoint mutation set. Failed verification emits `Succeeded=false`, persists the report on the turn and public result, and produces a nonzero prompt exit. Interrupted/cancelled turns finalize captured edits with a separate bounded persistence token, allowing inspection or undo. Normal mutation persistence retries do not append duplicate checkpoint IDs. Workspace verification locks prevent overlapping build/test output, while existing session locks preserve turn ordering.

Workspace evaluation and each child process have the requested timeout bound. With no explicit framework selection, multi-framework TRX results retain unknown framework attribution rather than claiming all results came from the single evaluated compilation.
