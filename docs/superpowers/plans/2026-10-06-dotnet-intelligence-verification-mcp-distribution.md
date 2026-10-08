# .NET Intelligence, Verification, MCP Server, and Binary Distribution

**Date:** 2026-10-06

**Repository baseline:** `main @ 72c29670c12cc11bd8c38e0447b1976460028367`

**Input:** the supplied “.NET Intelligence + Verification Engine + MCP Server + Binary Distribution” specification, originally grounded at `a5553865a3b6c5a30bfe4df83735b3193f42f1c2`.

**Status:** implementation plan; application code has not been changed.

**Target:** .NET 10, C# 13; incremental vertical slices.

## Recommended approach

Keep the specification's nine phases. Reuse Memory for semantic knowledge, Tools for permission-gated operations, Runtime for verification and repair, and Commands for user-facing entry points. Add only one production project: `SharpClaw.Code.Mcp.Server`, when its working stdio slice is implemented.

Start with a short compatibility and permission preflight, then prove solution inspection and exact symbol resolution. Do not build the entire contract/service catalog before that first slice works. Each phase below specifies concrete changes, verification, and an exit criterion.

The repository inspection confirmed an installed .NET SDK of `10.0.100` and a clean checkout before this document was added. This planning pass did not run builds, tests, package publishing, or deployments; the baseline commands below are work for implementation.

## 1. Current codebase and reuse map

Paths in this document are repository-relative. “Add” paths are proposed files, not existing implementations.

| Area | Verified current implementation | Implementation consequence |
| --- | --- | --- |
| Build settings | `Directory.Build.props` targets `net10.0`, C# `13.0`, XML documentation, analyzers, and vulnerability warnings as errors. | Preserve these settings; centrally manage every added package. |
| Dependencies | `Directory.Packages.props` pins `Microsoft.CodeAnalysis.CSharp` to `5.9.0` and `ModelContextProtocol` to `1.2.0`. | Validate compatible packages against these pins; do not independently upgrade one Roslyn assembly family. |
| Workspace knowledge | `Memory/Services/WorkspaceIndexService.cs` parses C# syntax and project-reference XML; `SqliteWorkspaceKnowledgeStore` persists chunks, symbols, and edges. | Add on-demand semantic loading alongside this index. Ordinary search must never trigger MSBuild loading. |
| Search | `Memory/Services/WorkspaceSearchService.cs`, `Tools/BuiltIn/WorkspaceSearchTool.cs`, and `SymbolSearchTool.cs` already expose indexed search. | Preserve existing tool names, result contracts, and SQLite formats. |
| Tool execution | `Tools/Registry/ToolRegistry.cs`, `Execution/ToolExecutor.cs`, and `ToolsServiceCollectionExtensions.cs` mediate discovery, permissions, execution, and events. | Register semantic adapters here; keep all callable operations on this execution path. |
| Provider discovery | `Agents/Services/AgentFrameworkBridge.cs` copies `ToolDefinition.InputSchemaJson` into provider definitions. | Supply actual schemas for every new tool; a CLR type name alone is insufficient. |
| Tool serialization | `Tools/Utilities/ToolJson.cs` currently uses reflection-based generic serialization. | New public contracts must explicitly use generated `ProtocolJsonContext` metadata; registering DTOs alone will not change this helper's behavior. |
| Permissions | `Permissions/Services/PermissionPolicyEngine.cs` allows file writes in `WorkspaceWrite`, requires approval for shell execution, and denies elevated operations in `ReadOnly`. | `RequiresApproval = true` does not guarantee an interactive prompt. Preserve the engine's actual mode rules. |
| Reversible changes | `Runtime/Turns/TurnMutationAccumulator.cs`, `Mutations/CheckpointMutationCoordinator.cs`, `MutationWorkspaceApplier.cs`, and `Sessions/Storage/FileMutationSetStore.cs` already support undo/redo. | Semantic rename must record complete before/after changes and reuse checkpoint persistence. |
| Mutation ordering | `TurnMutationAccumulator` stores operations in a concurrent bag and sorts by `OperationId`; built-in writes assign random GUID IDs. Undo reverses the saved operation list. | Fix chronological ordering before a repair loop can edit the same file repeatedly. |
| Diagnostics | `Runtime/Diagnostics/WorkspaceDiagnosticsService.cs` runs `dotnet build --no-restore`, parses output with a local regex, and caches snapshots. `PromptContextAssembler` invokes it during prompt assembly. | Extract one shared parser and close the existing implicit process-execution path while preserving the snapshot contract. |
| Process execution | `Infrastructure/Services/ProcessRunner.cs` uses `ProcessStartInfo.ArgumentList`, captures stdout/stderr, and kills the process tree on cancellation. | Reuse it; do not introduce shell-command strings or a second process runner. |
| Turn execution | `Runtime/Turns/DefaultTurnRunner.cs` executes one agent run and returns recorded mutations. `Orchestration/ConversationRuntime.cs` persists checkpoints and emits `TurnCompletedEvent(Succeeded: true)` on the normal path. | Add bounded verification before completion; propagate unsuccessful verification explicitly. |
| Public result | Internal `Runtime/Turns/TurnRunResult.cs` and public `Protocol/Commands/TurnExecutionResult.cs` are separate records. | Update both additively. Changing an imagined Protocol `TurnRunResult` would miss the real boundary. |
| Configuration | `Protocol/Models/OpenCodeParityModels.cs` contains `SharpClawConfigDocument`; Runtime configuration merges user/workspace documents. | Append optional verification configuration and update merging/defaults and tests. |
| MCP | `SharpClaw.Code.Mcp` is a client/supervisor. `McpCommandHandler` already supports list/status/register/start/stop/restart/doctor. | Keep client behavior intact; add `mcp serve` through an outer server host. |
| MCP trust | `Permissions/Rules/McpTrustRule.cs` requests approval for requests marked `Mcp` from an untrusted server, including read tools. | Distinguish inbound clients from the existing outbound-server trust category. Otherwise default noninteractive reads fail. |
| CLI composition | Handler registration lives in `Cli/Composition/CliServiceCollectionExtensions.cs`; there is no `CommandsServiceCollectionExtensions.cs`. | Register `verify` and `/verify` at the existing composition point. |
| CLI startup | `CliHostBuilder` clears log providers; `Program.cs` starts the Runtime host, including scheduled prompt services. | MCP needs deliberate protocol-only output and a host that does not start unrelated scheduled work. |
| Tests | Unit, integration, mock-provider, parity, and scenario harnesses already exist. MCP integration currently starts a fixture server and verifies discovery/lifecycle. | Extend these suites with real semantic fixtures and actual inbound MCP tool calls. |
| Distribution | CLI is a NuGet tool. `release.yml` already restores/audits/builds/tests/packs/verifies installation/pushes/creates a release. | Extend the release path rather than replace it. |
| Package checks | `SharpClawCode.Packages.slnf` includes 21 production projects; `Test-Packages.ps1` asserts exactly 21 packages. | A packaged MCP Server makes this 22; update the solution filter and smoke assertion together. |
| CI/docs | `ci.yml` runs Windows/Linux/macOS tests, package smoke checks, examples, scenario gates, VS Code compilation, and DocFX. | Add new checks to these lanes; update `docs/toc.yml` without editing generated `docs/api` or `docs/_site`. |

Project prefixes in the table, such as `Memory/`, mean `src/SharpClaw.Code.Memory/`.

## 2. Architecture decisions and necessary specification adjustments

### 2.1 Keep the dependency graph acyclic

`Runtime` already references `Tools`. Therefore a `VerifyWorkspaceTool` in Tools cannot reference a verification implementation in Runtime.

Place `VerifyWorkspaceTool : SharpClawToolBase` in `Runtime/Verification/Tools/` and register it as `ISharpClawTool` from `RuntimeServiceCollectionExtensions`. The existing registry resolves the full DI tool collection, so this tool remains discoverable and executes through the same `ToolExecutor`. Keep `IVerificationService` and its implementation in Runtime. No new verification project or reverse project reference is needed.

The MCP Server references Runtime, Tools, Protocol, Infrastructure, and the existing MCP project. CLI references the new server project for composition. Commands depends only on `ISharpClawMcpServer` in the existing MCP project. Runtime and the existing MCP client project must not reference the new server project.

### 2.2 Semantic reads do not imply safe project evaluation

MSBuild design-time loading invokes project targets. Microsoft's project-system documentation explicitly describes custom targets participating in design-time builds. **Inference:** a compiler-backed read operation can execute project-controlled code during loading, so treating an untrusted cold load as an ordinary read would contradict the repository's permission rule. [Design-time builds](https://github.com/dotnet/project-system/blob/main/docs/design-time-builds.md)

Recommended acceptance adjustment: semantic queries can execute in `ReadOnly` against a previously approved, valid semantic snapshot. A cold load or reload requiring project evaluation must obtain execution authorization through the existing permission engine, or return a typed denial. A source-only refresh must also remain within the approved evaluation/generator policy. Do not silently use `DangerFullAccess`, equate process isolation with a sandbox, or execute external analyzers/generators as harmless reads.

Phase 0 must demonstrate this behavior with a fixture target that writes a sentinel. The implementation must document the prerequisite rather than claim that every arbitrary solution can safely load under default read-only permissions. This is a security-driven change to the supplied cold-load expectations.

### 2.3 All verification entry points share the same authorization boundary

Use this path for agents, direct CLI, REPL, automatic verification, and MCP:

```text
caller -> IToolExecutor("verify_workspace") -> permission policy
       -> VerifyWorkspaceTool -> IVerificationService -> IProcessRunner
```

Handlers remain thin and reach the underlying verification service through the tool adapter. This deliberately tightens the specification's suggestion that handlers call the service directly: direct service invocation without authorization would let `sharpclaw verify --permission-mode readOnly` execute arbitrary build/test code.

Embedded hosts can resolve the verification service for a trusted, already-authorized call; provide a documented permission-aware invocation path as the default. Keep permission orchestration outside parsers and the process worker so unit testing remains straightforward.

`WorkspaceDiagnosticsService` currently bypasses that boundary during prompt assembly. Refactor it to consume authorized/cached verification diagnostics, retaining configured LSP metadata when execution is unavailable. Do not add a nested prompt-context/verification recursion.

### 2.4 Preserve result and event compatibility

Append optional `VerificationRunReport? Verification = null` to the internal and public turn results. Add a nullable verification field to the persisted turn if the durable snapshot must carry the report; events alone must not be the only route to a final machine-readable result.

Register all new DTOs/enums/events in `ProtocolJsonContext`. Add stable event discriminators to `Protocol/Events/RuntimeEvent.cs` as well: generated metadata alone does not establish polymorphic event-log compatibility.

Preserve existing required fields and `CommandResult`/`ToolResult` envelopes. Failed verification should retain its structured report even when `ToolResult.Succeeded` is false; the current base failure helper has no structured payload, so add a narrow typed overload or construct the envelope explicitly.

### 2.5 Inbound MCP is a separate caller category

Add an explicit `PermissionRequestSourceKind.McpClient` value for incoming MCP requests. Keep the existing `Mcp` value and `McpTrustRule` unchanged for requests initiated by registered external servers. This is the closest correct typed context to the proposal's generic “MCP” source.

Inbound calls use `SourceName = "sharpclaw-mcp-server"`, `IsInteractive = false`, a fixed workspace, the configured mode, and a curated `AllowedTools` list. Normal boundary/mode/allowlist rules still run. Never spoof a trusted remote-server identity to make local read calls succeed.

### 2.6 Distribute a self-contained host, with explicit SDK prerequisites

A self-contained executable should run `version`, ordinary runtime features, and non-.NET tools without a preinstalled .NET runtime. Semantic MSBuild loading and `dotnet build/test` still need suitable SDK/build tools and target framework assets. Locator uses installed Visual Studio/.NET SDK build logic; report missing prerequisites structurally. [MSBuild Locator](https://github.com/microsoft/MSBuildLocator/blob/main/src/MSBuildLocator/README.md)

Single-file publishing needs a compatibility proof, not just a successful publish command. Native dependencies may require extraction; assembly-file-path assumptions are incompatible with bundled assemblies. Inspect Roslyn build-host assets and SQLite native loading in the actual published output. Keep AOT and trimming disabled for this feature. [Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)

## 3. Implementation sequence

### Phase 0 — Establish a tested baseline and resolve compatibility gates

**Purpose:** avoid discovering permission and packaging blockers after most of the feature is built.

- [x] Run the existing Release build/test/example/scenario/doc/package gates before changing application code. Record baseline failures separately from new regressions.
- [x] Validate centrally managed Roslyn Workspaces packages against the current `5.9.0` pin; verify compatible `Microsoft.Build.Locator` and matching MCP ASP.NET Core packages. Do not invent package versions or perform unrelated dependency upgrades.
- [x] In a temporary proof harness, load a tiny restored .NET solution, inspect framework references, and resolve an interface exactly. Check installed SDK discovery, `global.json`, missing SDK behavior, and required build-host files.
- [x] Exercise the sentinel design-time target to prove where execution authorization is required. Write down the approved-load/read-only-query behavior before implementing tool permissions.
- [x] Make a preliminary single-file publish on the current OS, checking whether Roslyn's build-host assets and SQLite can actually be used after moving the executable to a clean directory. This is a feasibility spike, not the release pipeline.

**Exit:** baseline is recorded; package family alignment, permission behavior, SDK prerequisites, and publish-layout constraints are understood. A packaging limitation must have an explicit resolution before single-file completeness is claimed.

### Phase 1 — Semantic foundation and one working vertical slice (R1–R3)

**Modify:** `Directory.Packages.props`, `Memory/SharpClaw.Code.Memory.csproj`, `Memory/MemoryServiceCollectionExtensions.cs`, `Protocol/Serialization/ProtocolJsonContext.cs`.

**Add incrementally:**

- `Protocol/Models/DotNetWorkspaceRequest.cs`, target-resolution status/result types, solution/project/reference/package summaries, and initial C# symbol query/result/location types.
- `Memory/Abstractions/IDotNetWorkspaceSemanticService.cs`.
- `Memory/Services/DotNetWorkspaceTargetResolver.cs`.
- `Memory/Services/DotNetWorkspaceSemanticService.cs`.
- `Memory/Services/DotNetSemanticWorkspaceCache.cs` only when the working loader needs bounded ownership and reuse.

**Tasks:**

1. Add `Microsoft.CodeAnalysis.Workspaces.MSBuild`, `Microsoft.CodeAnalysis.CSharp.Workspaces`, and Locator references after Phase 0 validation. Keep Roslyn/MSBuild types private to Memory.
2. Support an explicit target in the public request; the supplied interface sketch otherwise has no place to express its own explicit-target rule. Resolve: explicit contained target, one top-level `.sln`/`.slnx`, one top-level `.csproj`, typed ambiguity, typed no-target. Enumerate deterministically and respect Windows path casing.
3. Register Locator once before touching MSBuild types. Detect incompatible existing registration in embedded hosts and return a clear load error rather than re-registering blindly. Respect workspace SDK selection; do not perform hidden restore.
4. Report partial/load failures and unresolved framework references distinctly. Do not return an apparently complete solution summary from a broken compilation.
5. Implement solution/project graph inspection and exact symbol lookup with candidates for ambiguity. Include container, kind, project/path, and optional declaration position or signature for overloaded symbols. Never turn a lexical name match into an asserted exact semantic identity.
6. Cache by canonical root, target, configuration, and framework context. Use async-safe per-workspace load coordination, bounded entries, and leases so active workspaces cannot be disposed during eviction. Cancellation/failure must not permanently poison a cache entry.
7. Invalidate project structure on solution/project/props/targets/package/global.json changes. Refresh changed source documents before semantic queries; cache freshness cannot depend only on project files. Revalidate imports/assets used by the loader conservatively. No filesystem watchers in this slice.
8. Keep multi-target semantic scope explicit. Report all declared target frameworks, but identify the evaluated framework in query results; do not claim all-framework coverage from one compilation.

**Fixtures:** add a minimal checked-in fixture under `tests/Fixtures/DotNetSemantic/`, copied to temporary workspaces by tests. Include a library, consumer, interface, implementation, inherited type, overloaded symbol, and a small test project. Use existing test dependencies; explicitly provision fixture restore assets before semantic/build tests.

**Tests:** solution/project discovery; `.slnx`; ambiguous/no target; target escape; exact/ambiguous symbol; missing SDK/assets; load failure recovery; concurrent loading; eviction/disposal; source and project edits invalidate results; existing indexed search never loads semantics. Include at least one actual cross-project reference resolution to prove framework references are usable.

**Exit:** an approved fixture solution can be inspected and a symbol resolved exactly. Existing workspace search and persisted indexes remain compatible. Build the full solution at this boundary.

### Phase 2 — Semantic read tools (R4)

**Modify:** `Tools/ToolsServiceCollectionExtensions.cs`, `Tools/BuiltIn/SharpClawToolBase.cs` if typed serialization overloads are needed, `ProtocolJsonContext`, semantic service, and `Agents/Services/AgentFrameworkBridge.cs` tests.

**Add:** reference/hierarchy/diagnostic Protocol DTOs and five focused tool classes in `Tools/BuiltIn/`:

```text
dotnet_solution_inspect
csharp_symbol_resolve
csharp_find_references
csharp_type_hierarchy
csharp_diagnostics
```

**Tasks:**

1. Use Roslyn symbol/reference APIs for definitions, references, implementations, and derived types. Deduplicate partial declarations and linked documents; include project/framework context and stable locations.
2. Expose compiler diagnostics with project/path/severity/code filters. Keep generator/analyzer execution behind the approved-evaluation boundary; do not widen “compiler diagnostics” into arbitrary analyzer execution implicitly.
3. Provide explicit input schemas and bounded/paged results with truncation metadata where large solutions require it.
4. Deserialize/serialize new tool contracts through generated `JsonTypeInfo<T>`; preserve the legacy ToolJson path for existing tools until a separately justified migration.
5. Route adapters through `IToolRegistry` and `IToolExecutor`. Reads use the normal non-destructive query scope, with approved semantic loading as a separate prerequisite.

**Tests:** references across projects; interface implementations; class/interface/record hierarchy; ambiguous overloads; diagnostic filters; generated/linked document handling; provider-advertised schemas; read-only query success after approved load; unapproved cold load denial; JSON round-trips and absence of Roslyn/MSBuild objects.

**Parity:** `semantic_symbol_resolution` with a provisioned local fixture or a deterministic semantic seam; no network access.

**Exit:** providers discover and invoke all five tools through the existing bridge; results are compiler-backed and permission behavior is explicit.

### Phase 3 — Permission-aware, reversible semantic rename (R5)

**Add:** rename request/change/plan/result Protocol DTOs and `Tools/BuiltIn/CSharpRenameSymbolTool.cs`.

**Modify:** semantic service, tool registration, generated metadata, and mutation ordering support in `Runtime/Turns/TurnMutationAccumulator.cs`.

**Tasks:**

1. Generate a full Roslyn rename plan without writing through `MSBuildWorkspace.TryApplyChanges`. Resolve one exact symbol and validate the new identifier and rename conflicts.
2. Reject generated/unwritable documents and every canonical path outside the workspace, including linked-file and symlink escapes. Combine duplicate physical-file edits only if their final contents agree.
3. Read and retain all before/after text, validate every path, and compare the current content to the plan immediately before writing. Do not overwrite concurrent user changes. Preserve supported newline behavior; the existing text-only mutation contract does not guarantee byte-identical encoding/BOM restoration, so test and document that limit or extend encoding metadata additively if required.
4. Apply through `IFileSystem` only after permission evaluation: `FileSystemWrite`, destructive, requires approval. `ReadOnly` denies; `WorkspaceWrite` follows the current allow rules rather than introducing a forced interactive prompt.
5. Record the complete committed change set through `IToolMutationRecorder`. On failure/cancellation, roll back with an independent bounded cleanup token; a cancelled execution token must not prevent cleanup.
6. If rollback leaves residual changes, record their actual final state and include rollback failures in the failed result. Never leave recoverable changes invisible or report partial success as a complete rename.
7. Replace random-ID sorting with actual recording order for repeated mutations. Keep operation IDs and durable JSON formats stable. Serialize same-workspace semantic writes and use content preconditions against external edits.
8. Invalidate semantic snapshots after apply, rollback, undo, and redo, including changes performed outside semantic tools. A source fingerprint check remains necessary for external editor changes.

**Tests:** multi-file rename compiles; overload ambiguity/conflicts; denial writes nothing; workspace-write policy; outside/linked/symlink paths; concurrent edits; write failure and rollback failure; cancellation; all files recorded; real undo/redo restores the initial/final text; two successive edits to one file replay in the correct order.

**Parity:** `semantic_rename_mutation_recorded`.

**Exit:** rename is semantically correct, wholly permission-gated, and reversible through the existing checkpoint system. No refactoring catalog or file-renaming feature is added.

### Phase 4 — Structured verification core (R6–R9)

**Add under Runtime:**

```text
Verification/IVerificationService.cs
Verification/VerificationService.cs
Verification/IAffectedProjectResolver.cs
Verification/AffectedProjectResolver.cs
Verification/IDotNetDiagnosticParser.cs
Verification/DotNetDiagnosticParser.cs
Verification/ITestResultParser.cs
Verification/TrxTestResultParser.cs
```

Use one explicit orchestration service initially; split build/test workers only when their responsibilities justify it. Add Protocol verification enums, request/policy, build/step/test/run reports, and failure-reason types as needed by this slice.

**Tasks:**

1. Use typed argument arrays with `IProcessRunner`, explicit working directory, consistent configuration/framework selection, timeouts, and caller cancellation. Default restore off. Missing assets, unavailable SDK, permission denial, timeout, process failure, and no .NET target must remain distinguishable.
2. Parse build diagnostics once from stdout/stderr. Handle drive-letter paths, spaces, ranges, project suffixes, location-free SDK/MSBuild errors, and duplicate output. Do not infer success from an empty diagnostic list; use exit codes too.
3. Run tests with `--no-build --no-restore --logger trx` and a unique temporary results directory. Parse XML with DTD/external entity processing disabled. Merge multiple project/framework TRX files without collisions, retaining failed names/messages/stacks and project attribution.
4. Treat a nonzero test exit or missing/malformed expected result files as an explicit failed/incomplete test step. Distinguish “no test projects” from a passing test run. Clean temp data best-effort and bound report/log sizes.
5. Define scope behavior: `Build` builds the target; `Tests` runs existing matching outputs and reports a build prerequisite failure if unavailable; `All` builds then tests all selected test projects; `Affected` builds/tests the affected closure. Stop tests after a failed prerequisite build.
6. Determine ownership using evaluated compile items, then reverse dependency edges to include consumers and relevant test projects. Consider linked files, deletes, project/config/build-file changes, and unknown file ownership. Conservatively fall back to the explicit solution/project; a non-.NET workspace returns `Skipped` with a reason.
7. For runtime calls use the turn's recorded paths. For standalone `Affected`, use a supplied change list or existing Git inspection; when changes/ownership cannot be established, verify the target conservatively. Never treat a missing change list as proof that no verification is needed.
8. Extract the diagnostic parser from `WorkspaceDiagnosticsService`. Preserve `WorkspaceDiagnosticsSnapshot`, configured LSP metadata, and bounded caching. Wire permission-aware diagnostic refresh in Phase 5 before exposing new execution surfaces.
9. Ensure this service is callable without a model/provider or API key.

**Tests:** fake process runner success/failure; missing assets; structured diagnostics; Windows paths; malformed/multiple TRX; skipped tests; absent test output; build/test configuration mismatch; cancellation kills process tree; timeout; affected reverse graph; conservative fallback; no-target skip; restore is never requested implicitly.

**Integration:** one restored fixture builds and runs real tests on Windows/Linux/macOS. Restore/provision fixture dependencies during explicit test setup/CI restore, not inside verification. Test/parity execution itself must not fetch packages.

**Exit:** build/test verification produces stable generated JSON without an LLM; prompt diagnostic parsing is shared, not duplicated.

### Phase 5 — Verification tool, CLI, and REPL (R10–R11)

**Add:** `Runtime/Verification/Tools/VerifyWorkspaceTool.cs`, `Commands/Handlers/VerifyCommandHandler.cs`.

**Modify:** Runtime composition, CLI composition, command tests/renderers, diagnostic refresh orchestration, and generated metadata.

**Tasks:**

1. Register the Runtime-owned tool in the existing `ISharpClawTool` collection. Use `ShellExecution`, `RequiresApproval = true`, `IsDestructive = false`, and an explicit schema.
2. Implement `VerifyCommandHandler` as `ICommandHandler` and `ISlashCommandHandler`, following `IndexCommandHandler`'s pattern. Use the existing recursive `--cwd`, `--permission-mode`, `--output-format`, and scoped approval settings rather than duplicate global options.
3. Add `--scope affected|build|tests|all`, `--target`, and `--restore`. Validate options and use the same parser for equivalent slash-command arguments. Default to `affected`, with conservative target fallback.
4. Invoke via `IToolExecutor`, construct real caller context, preserve REPL session settings, and render the typed verification report within the existing command envelope. Expose structured failed reports in JSON.
5. Map pass to exit 0; map failed/denied/prerequisite errors to nonzero. A no-.NET skip must be visibly skipped rather than falsely reported as verified. Follow existing cancellation exit conventions and test them.
6. Remove the unconditional process call from prompt assembly: use cached compiler/verification diagnostics or an explicitly authorized refresh. Read-only/headless prompts must not build before a permission decision.

**Tests:** CLI and `/verify` scope/target/restore/output binding; no provider credentials; text/JSON envelopes; real build failure nonzero with code/path/line; tool discovery; read-only process denial; workspace-write approval flow; noninteractive denial; approved scoped shell execution; prompt assembly never bypasses permissions.

**Parity:** `verification_build_success`, `verification_build_failure`, `verification_permission_denied` using deterministic process results.

**Exit:** humans, scripts, agents, and embedded callers share the same worker and authorization path.

### Phase 6 — Opt-in automatic verification and bounded repair (R12–R13)

**Add:** Protocol `VerificationOptions`/policy and four verification event records; Runtime `IVerificationPolicyResolver`, policy resolver, `IVerificationLoopCoordinator`, coordinator, and a bounded failure-context formatter.

**Modify:** `OpenCodeParityModels.cs`, `SharpClawWorkflowMetadataKeys.cs`, `SharpClawConfigService.cs`, Runtime composition, `DefaultTurnRunner.cs`, `TurnRunResult.cs`, `ConversationRuntime.cs`, public `TurnExecutionResult.cs`, persisted turn/result rendering as needed, `PromptInvocationService.cs`, `RuntimeEvent.cs`, and `ProtocolJsonContext.cs`.

**Tasks:**

1. Append optional configuration; default to disabled, affected scope, post-mutation execution, repair limit 2, restore off. Use the four supplied metadata keys and resolve request overrides over workspace/user defaults. Validate invalid scope, negative/excessive budgets, and nullable backward compatibility.
2. Run only after supported source mutations and only when explicitly enabled. Preserve existing plan/spec/research modes; do not treat their generated planning artifacts as automatic coding-verification triggers. Shell/plugin/external-agent writes currently lack guaranteed mutation records: state that initial automatic detection covers recorded file changes, and use conservative change detection only when it is proven.
3. Invoke verification through the same `verify_workspace` permission boundary. Denial/prerequisite errors/non-.NET skips do not trigger speculative agent repair.
4. Implement an iterative loop in the coordinator, not recursion into `DefaultTurnRunner` or `IConversationRuntime`. With budget 2, permit at most initial verification plus two repair-and-verification passes. Reuse the selected agent, session/turn IDs, primary mode, allowlist, permissions, and mutation recorder.
5. Supply bounded structured compiler/test failures plus current-turn context. Keep previous assistant/tool history relevant to the repair; avoid raw megabyte logs. Aggregate token usage, provider events/requests, tool results, and mutations across attempts rather than retaining only the last run.
6. Publish started/completed/repair-started/repair-completed events once, with attempt numbers and correlation IDs. Persist through existing event infrastructure. On recovery/resume, do not silently reset or multiply the repair budget; retain attempt accounting using the durable event/state seam.
7. Keep all actual edits in one ordered reversible change set for the logical turn. Persist recoverable mutations even when verification, repair, or cancellation fails after edits. Use bounded finalization separate from cancelled execution; do not lose before/after data by throwing before checkpoint persistence.
8. Propagate the final report into internal/public/durable results. The normal completion path must not emit `Succeeded: true` after exhausted failed verification. Update prompt exit codes/renderers so enabled verification failure is visible to scripts as well as humans. Preserve old behavior when verification is absent/disabled.
9. Serialize verification per workspace where overlapping build/test output would collide; existing session locks alone do not coordinate two sessions in the same workspace.

**Tests:** disabled path unchanged; no mutations means no run; mode exclusions; metadata/config precedence; zero/two repair limits; max attempts never exceeded; permission denial means no process/repair; structured repair context; aggregate usage/results; same-file edit/repair undo/redo; failed repair/cancellation retains mutations; recovery does not reset budget; polymorphic persisted events and old config/turn payloads round-trip.

**Exit:** enabled coding turns verify, repair within the configured budget, and report truthful structured completion without bypassing permissions or breaking recovery.

### Phase 7 — Working stdio MCP server

**Add:** `src/SharpClaw.Code.Mcp.Server/SharpClaw.Code.Mcp.Server.csproj` and only the host/bridge/registration files needed for the working slice. Add `ISharpClawMcpServer` and typed server options to the existing MCP project's `Abstractions/` and `Models/`.

**Modify:** solution, package solution filter, CLI project/composition, `McpCommandHandler`, source-kind contract/tests, package smoke checks, and SDK documentation. If convenient embedded server registration is exposed by `SharpClaw.Code`, add its outer-project reference; never add a Runtime-to-server reference.

**Tasks:**

1. Use the already-pinned official MCP SDK. Register a curated tool set explicitly; use supported typed/programmatic APIs, not broad assembly scanning. The SDK supports tool creation and structured tool results; confirm the exact API against the pinned package. [Official MCP tools documentation](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/tools/tools.md)
2. Add `sharpclaw mcp serve` under the existing command. Default to stdio, `ReadOnly`, mutations disabled. The current global CLI default is `WorkspaceWrite`: detect whether a permission override was explicitly supplied instead of inheriting that default accidentally.
3. Host protocol traffic without Spectre output, banners, JSON command summaries, or approval prompts on stdout. Route logs to stderr. Do not start the outer Runtime scheduler for an MCP-only process; introduce a narrow MCP host composition/startup path and reuse shared services. Cancellation, EOF, and shutdown must dispose the server and semantic cache cleanly.
4. Expose `workspace_search`, `symbol_search`, and the five semantic tools. Expose `verify_workspace` with execution semantics; read-only default calls to it are denied. Keep the Phase 0 approved-evaluation prerequisite for semantic cold loads explicit.
5. Include `csharp_rename_symbol` only when `AllowMutations` is true and the selected mode permits it. Enforce the same allowlist on `tools/call`, not just `tools/list`; manually naming a hidden tool must not work.
6. Bridge every allowed call through `IToolExecutor` using inbound MCP context. Map denials/failures to MCP tool errors and preserve typed structured content. Do not catch cancellation as success.
7. Use valid SharpClaw session/turn correlation where events are persisted. Never invent nonempty session IDs with no session store entry: `ToolExecutor` treats them as durable. Mutation-enabled calls need a real session, recorder, checkpoint, and mutation-set persistence through the existing coordinator so MCP rename remains undoable. Read-only calls can use a non-durable context if no session is created.
8. Preserve existing MCP client lifecycle and outbound trust behavior. Do not make the inbound server auto-connect to configured external MCP servers or require model credentials.
9. Include the new packable project in `SharpClawCode.Packages.slnf` and update `Test-Packages.ps1` from 21 to 22 expected production packages. Verify local NuGet install includes the new CLI dependency and public server API.

**Integration:** launch the real `sharpclaw mcp serve` process with the official `StdioClientTransport`/client, negotiate, list tools, invoke indexed search without credentials, invoke semantics with an approved fixture, inspect structured results, verify default rename absence and direct-call denial, verify read-only build denial, and shut down. Assert every stdout message is valid MCP traffic. Separately enable permitted mutations and prove recorded rename/undo. No external network dependency.

**Exit:** an external MCP client can use real SharpClaw tools through normal execution and permission seams. Default exposure is safe and the existing MCP client subsystem still passes its tests.

### Phase 8 — Streamable HTTP MCP transport

**Modify:** MCP Server project/host, matching central package entry, server options, command binding, and transport integration tests.

**Tasks:**

1. Add the official `ModelContextProtocol.AspNetCore` package matched to the existing SDK family. Use the same bridge and schemas as stdio. Official transport documentation identifies this package and its MCP endpoint mapping. [MCP transports](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md)
2. Default to `127.0.0.1` and a configurable port; validate host/port and reject implicit wildcard binding. Require explicit configuration for non-loopback exposure.
3. Validate accepted Host/Origin values for local HTTP access, and preserve request cancellation. Keep caller/session/approval/mutation state isolated between clients.
4. Use the existing authenticated approval/host context seams when elevated execution is enabled. Non-loopback exposure must be explicitly authenticated/configured; do not create a separate enterprise identity platform for this slice.
5. Test both transports against the same exposure and permission matrix; test malformed requests, cancellation, shutdown, and client isolation.

**Exit:** HTTP clients invoke the same bounded tool surface, with loopback-only defaults and no stdio regression. This phase does not delay completion of Phase 7.

### Phase 9 — Six self-contained binary artifacts (R14)

**Modify:** `.github/workflows/release.yml` and CI where binary smoke belongs. Add focused cross-platform scripts such as `.github/scripts/Publish-Binaries.ps1` and `Test-Binary.ps1` only when shared workflow logic warrants them.

**Tasks:**

1. Separate release validation/package creation, RID publish-and-smoke jobs, and final publish/release assembly. Preserve restore, audit, warning-free build, tests, package install verification, NuGet push, and GitHub release creation. Do not push packages/create a release until all required binary checks pass.
2. Publish the CLI project, not the whole solution, with release-time settings:

   ```text
   dotnet publish src/SharpClaw.Code.Cli/SharpClaw.Code.Cli.csproj
     --configuration Release --runtime <RID> --self-contained true
     -p:PublishSingleFile=true -p:PublishTrimmed=false -p:PublishAot=false
     -p:Version=<tag-version> -p:PackageVersion=<tag-version>
     --output <isolated-rid-directory>
   ```

   Resolve any NuGet-tool/self-contained publish conflict with a release-only `PackAsTool=false` override if the Phase 0 proof requires it. Do not set a global RID, single-file setting, or change normal NuGet tool packaging.
3. Add release-only native extraction settings where the proof requires them, such as `IncludeNativeLibrariesForSelfExtract=true`. Resolve Roslyn helper content explicitly; do not accidentally omit it when staging an executable-only archive. If the proposed three-file archive cannot support the loader, record and resolve that specification deviation before release rather than silently dropping semantic support.
4. Produce `sharpclaw-<version>-<RID>.zip` for `win-x64`/`win-arm64`; `.tar.gz` for `linux-x64`/`linux-arm64`/`osx-x64`/`osx-arm64`. Stage the executable as `sharpclaw.exe` or `sharpclaw`, plus `LICENSE` and `README.md`. Preserve Unix executable mode. Exclude PDBs from binary archives while retaining existing NuGet symbol packages.
5. Pass the tag version into assembly metadata as well as PackageVersion. The current version command reads the entry assembly's version; package version alone does not update what the executable reports. Preserve the current JSON shape, and add informational-version detail only additively if prerelease identity needs exposure.
6. Smoke each actual RID executable on a matching OS/architecture runner or a documented equivalent. Select runner labels based on current account/repository availability; do not count cross-publishing an ARM64 binary as executing it. Missing execution coverage is a release gap, not a passed smoke test.
7. Unpack into a clean directory before testing. Run the executable directly for `version`, an offline non-.NET command, SQLite-backed state, stdio MCP handshake/tool call, and semantic/build/test behavior with an installed SDK. Also prove `version` works in an environment without a .NET runtime/SDK. Provider-dependent `doctor` checks may legitimately report unconfigured providers and need assertions that distinguish that from packaging failure.
8. Gather archives and `.nupkg`/`.snupkg` files into the same GitHub release after all gates pass. Check the expected artifact set and add checksums if practical.

**Exit:** all six binary archives are published and directly smoke-tested; basic execution requires no preinstalled .NET runtime; SDK-dependent features correctly describe their prerequisites; NuGet tool and SDK package installation still work.

## 4. Cross-phase verification and quality gates

Use focused unit/integration filters while implementing each slice, then build affected projects with warnings as errors. Broaden to full solution checks at phase boundaries that change dependencies, public contracts, composition, or runtime behavior.

Run the complete final gates:

```bash
dotnet restore SharpClawCode.sln
dotnet build SharpClawCode.sln --configuration Release --no-restore --warnaserror
dotnet test SharpClawCode.sln --configuration Release --no-build
dotnet build examples/WebApiAgent/WebApiAgent.csproj --configuration Release
dotnet build examples/MinimalConsoleAgent/MinimalConsoleAgent.csproj --configuration Release
dotnet build examples/WorkerServiceHost/WorkerServiceHost.csproj --configuration Release
dotnet build examples/McpToolAgent/McpToolAgent.csproj --configuration Release
dotnet run --project src/SharpClaw.Code.Cli/SharpClaw.Code.Cli.csproj --configuration Release --no-build -- test run
dotnet run --project src/SharpClaw.Code.Cli/SharpClaw.Code.Cli.csproj --configuration Release --no-build -- test gates
pwsh -File .github/scripts/Test-VulnerablePackages.ps1 -Target SharpClawCode.sln
pwsh -File .github/scripts/Test-Packages.ps1
dotnet tool restore
dotnet docfx docs/docfx.json --warningsAsErrors
```

Keep the existing coverage floor, Windows/Linux/macOS lanes, VS Code compilation, and package smoke checks passing. Run final binary tests on all six target architectures. Avoid network dependencies in parity and MCP tests; package/fixture provisioning is an explicit setup step.

Required manual acceptance in disposable fixture workspaces:

- Resolve a definition and cross-project references through an agent using a mock provider or explicitly configured provider.
- Rename an interface, build the updated solution, then undo/redo the whole multi-file change.
- Run `verify --scope all` and inspect typed passing build/test reports.
- Introduce a compile error and verify nonzero exit plus structured code/path/line.
- Enable repair limit 2, trigger a test-sensitive failure, and prove the attempt ceiling, truthful failure/pass result, usage accounting, and reversibility.
- Connect a real MCP client, invoke safe tools, and demonstrate default mutation/build denial plus permitted mutation recording when explicitly enabled.
- Run each downloaded/unpacked executable directly; separate no-runtime host execution from SDK-backed feature acceptance.

## 5. Documentation deliverables

Update documentation alongside each working slice, not after the entire implementation:

| Slice | Documentation |
| --- | --- |
| Semantic foundation/read tools | Add `docs/dotnet-intelligence.md`; update `docs/architecture.md`, `docs/tools.md`, and relevant README capability text. Explain approved project evaluation, ambiguity, cache freshness, and SDK/framework prerequisites. |
| Rename | Document mutation recording, rollback limits, read-only denial, workspace-write policy, linked/generated file handling, and undo/redo. |
| Verification | Add `docs/verification.md`; update `docs/runtime.md`, `docs/tools.md`, and `docs/testing.md`. Document scopes, restore-off behavior, failed JSON reports, authorization, and non-.NET skip. |
| Automatic repair | Document disabled default, configuration/metadata precedence, exact budget semantics, recorded-mutation detection limits, cancellation/recovery, and completion/exit behavior. |
| MCP | Add `docs/mcp-server.md`; update `docs/mcp.md` to distinguish client/server roles, stdio output rules, caller categories, transport defaults, SDK prerequisite, and mutation exposure. |
| Distribution | Add `docs/distribution.md`; update README installation instructions and artifact names, supported RIDs, SDK-dependent capabilities, and direct executable use. |

Add authored pages to `docs/toc.yml`. Do not hand-edit generated API YAML or site HTML. README differentiation should describe compiler-aware .NET intelligence, structured verification, and MCP-hosted tooling only as the corresponding slices become implemented and tested.

## 6. Completion and handoff

The complete implementation must satisfy this path through shared services:

```text
inspect approved .NET workspace -> resolve exact symbols/references
-> permission-aware reversible change -> affected-project selection
-> build/test -> structured failures -> bounded repair -> re-verify
```

CLI, agent runtime, MCP, and embedded hosts must expose that same behavior without duplicated business logic. Preserve existing JSON envelopes, index/storage formats, plugin execution, MCP client behavior, and disabled-default verification behavior.

Keep Native AOT, arbitrary C# skill compilation, in-process plugin loading, an IDE/LSP replacement, additional refactorings, and distributed build/workflow systems out of scope.

At delivery, report implemented phases, actual commands/tools, public contract additions, tests and platform evidence, archive/package artifacts, and any deliberately deferred or externally blocked acceptance. Do not equate “published successfully” with “executed on every RID,” or “build passed” with “tests ran.”

No branches, commits, PRs, issues, or production releases should be created as part of this planning request. Future implementation should start with Phase 0, then finish Phase 1 before proceeding.

## Implementation evidence

- Phase 0 complete: Release warning-as-error build, 318 baseline tests, all four scenario gates, 21-package install smoke, vulnerability audit, examples, and DocFX passed on macOS arm64 with SDK 10.0.100.
- Phase 0 adaptation: Roslyn 5.9 owns MSBuild Locator inside its bundled build host. The tested implementation uses matched CSharp.Workspaces/Workspaces.MSBuild packages without registering another in-process Locator. Sentinel targets confirmed that evaluation executes project code.
- Single-file feasibility passed after relocating executables: Roslyn load plus SQLite-backed index. Release needs PackAsTool=false, native extraction, and all-content extraction so helper files remain available. Basic execution and SDK-dependent features have separate prerequisites.
- Phase 1 complete: deterministic target discovery, permission-required lazy loading, cache capacity four, source refresh and project-input invalidation, typed SDK/restore/load failures, project graph and exact lookup. Eight focused unit tests and ten real SDK integration tests passed; full solution build and documentation checked.

- Phase 2 complete: five registered schema-bearing semantic read tools, exact cross-project references, type relationships, bounded compiler diagnostics, generated JSON, and execution-aware cold evaluation. Thirteen focused SDK integration tests passed. Offline semantic parity added; expanding its catalog required updating the old fixed-count assertion.

- Phase 3 complete: Roslyn rename plan/conflict validation, permission-gated multi-file writes, optimistic content checks, bounded rollback/residual recording, chronological mutations, and canonical-path undo fix. Seven real integration cases passed, including two successive renames, a build, durable undo/redo, injected write/rollback failure, cancellation, external edits, ambiguity, and generated-source rejection. Rename parity added.

- Phase 4 complete: typed build/test worker, evaluated source ownership/reverse closure, explicit restore, secure TRX parser, shared diagnostic parser, process timeout/cancellation, workspace serialization and bounded reports. Three parser/graph unit tests and three real/fault-controlled integration scenarios passed, including a real passing and failing xUnit fixture. Approved SDK/package bootstrap sources stay in compilations but are excluded from workspace query locations; their metadata stamps require fresh evaluation after changes.

- Phase 5 complete: verify_workspace, CLI verify and /verify share generated reports and the shell permission boundary. Cached prompt diagnostics execute no processes. CLI/REPL integration and permission/budget tests passed; three deterministic verification parity scenarios added.

- Phase 6 complete: disabled-default fieldwise configuration/metadata policy, iterative bounded repairs, durable attempt events, aggregated usage/tool/provider accounting, final report propagation and truthful completion/exit, and interrupted-turn mutation journaling. Eight integration cases and six config/policy unit cases passed, including a real test-sensitive repair and durable undo after failures/cancellation. Phases 0–5 full regressions passed (359 tests at the Phase 5 boundary); Phase 6 full regressions underway.

- Phase 6 full gates: 372 tests passed (275 unit, 77 integration, 20 parity); Release and DocFX passed with warnings as errors.
- Phase 7: real official-SDK stdio clients passed handshake, curated discovery, search, cold-evaluation and verification denial, hidden rename denial, explicitly elevated semantic loading, and durable rename undo. A narrow direct SDK transport leaves Runtime hosted services stopped; package set is now 22.

- Phase 8 full gates: 383 tests passed (275 unit, 88 integration, 20 parity); warning-free Release build. Stateful HTTP shares the stdio bridge, rejects wildcard/implicit remote exposure, validates Host/Origin, requires tokens for elevated/remote hosts, expires idle sessions, and isolates durable client state. Literal external build imports now invalidate semantic snapshots; unresolved property/wildcard imports require fresh evaluation authorization.

- Phase 9 implementation: release validation, six matching architecture publish/smoke lanes, final artifact/checksum validation, and gated NuGet/GitHub publication are implemented. Single-file publishing embeds native and Roslyn helper content; ordinary NuGet tool packaging remains framework-dependent. Informational version is additive in the version JSON payload.
- Native acceptance completed locally for osx-arm64: a relocated three-file archive starts with empty PATH and isolated runtime roots, reports the exact preview identity, supports offline non-.NET verification and SQLite index state, and passes six real native MCP checks (default denials, elevated semantics, durable rename undo, real build/TRX tests, workflow restrictions, protocol-only stdout and clean EOF shutdown). Archive: `artifacts/binaries/sharpclaw-0.1.0-preview.1-osx-arm64.tar.gz`, with adjacent SHA-256 checksum.
- Package smoke now isolates its consumer cache and maps SharpClaw package IDs to the local feed. This fixed a genuine stale same-version package false positive. All 22 production packages and 22 symbol packages were created; the new server API compiles from the aggregate SDK package and the freshly installed CLI runs.
- Final gates: Release solution/example build has zero warnings/errors; 389 tests pass (275 unit, 94 integration, 20 parity). Coverage collection passed the existing 38 percent floor at 41.24 percent. All four scenario gates, DocFX with warnings as errors, vulnerability audit of 35 projects, VS Code compilation, and actionlint validation of CI/release workflows pass. Additional strict wire and linked external-source tests pass in the final suite. Generated scenario-report churn was restored; fresh results remain in ignored `artifacts/testing/`.
- Release acceptance still pending externally: win-x64, win-arm64, linux-x64, linux-arm64 and osx-x64 must execute their configured matching CI jobs. No cross-publish is counted as runtime acceptance, and no packages, tag, commit, PR or GitHub release were published by this implementation run.
