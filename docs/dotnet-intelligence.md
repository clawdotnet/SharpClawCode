# .NET intelligence

SharpClaw's Memory subsystem provides lazy compiler-backed solution inspection and exact C# symbol resolution alongside the existing SQLite workspace index. Indexed `workspace_search` and `symbol_search` do not load MSBuild and retain their existing formats.

Embedded hosts resolve `IDotNetWorkspaceTargetResolver` for non-executing discovery and `IDotNetWorkspaceSemanticService` for inspection and symbol queries. Requests select a workspace, optional contained target, configuration, and optional target framework. Discovery chooses an explicit target, one top-level `.sln`/`.slnx`, or one top-level `.csproj`; ambiguous workspaces require a selection.

## Project evaluation permissions

Loading through MSBuild can execute custom design-time targets. A host must provide `IDotNetWorkspaceEvaluationAuthorization` through its execution permission policy for a cold load or structural reload. A null authorization permits valid cached queries only. Authorization is a host-side interface, never an agent-controlled Boolean in a tool argument.

Compiler queries exclude project-supplied analyzer and source-generator references. Generated-symbol support is therefore limited to source documents already materialized by approved builds. This service is not an execution sandbox.

## Cache and SDK behavior

The lifecycle-owned cache retains up to four compilation contexts and serializes use/load/eviction to prevent stale/disposed workspace access. Source content refresh does not reevaluate MSBuild. Changed compile-item paths, project/solution/props/targets/configuration, and restore assets invalidate loading and require fresh authorization. Literal imported build files are fingerprinted recursively, including those outside the root. Property-driven or wildcard imports require fresh evaluation authorization on each call. No filesystem watchers or hidden package restore are used.

Roslyn 5.9 uses its bundled out-of-process build host to discover installed SDK/build tools and select the workspace SDK. A second in-process MSBuild Locator registration is unnecessary. The machine needs an appropriate .NET SDK and restored framework/package assets even when SharpClaw itself is a self-contained executable.

Results use dependency-light Protocol records and generated JSON metadata. They distinguish target ambiguity, no target, permission denial, missing prerequisites, load failure, and partial loading. Symbol queries return candidates when names, overloaded signatures, or project contexts are ambiguous; selectors include container, kind, project/path, signature, and declaration position. Locations are one-based and paths are workspace-relative.

Solution inspection reports evaluated project references, available restored target frameworks/package versions, and test-project heuristics. Multi-targeted projects identify the evaluated framework; one compilation does not imply all-framework semantic coverage.

## Semantic tools

The standard agent tool registry exposes `dotnet_solution_inspect`, `csharp_symbol_resolve`, `csharp_find_references`, `csharp_type_hierarchy`, and `csharp_diagnostics`. Each publishes an explicit JSON input schema and generated protocol output. `name` is exact; `container`, `kind`, `project`, `path`, declaration position, and `signature` can disambiguate it. References use compiler identity across project boundaries. Hierarchy includes base types, implemented interfaces, derived types and interface implementations. Diagnostics support project, path, severity and code filters.

Use `target`, `configuration` and `targetFramework` to choose compilation context. References and diagnostics accept `offset` and `limit` (1–200); results include total/truncation metadata. Hierarchy bounds each relationship collection with `limit` and reports truncation. Source generators and project analyzers remain excluded.

Tool execution first applies the normal caller permissions. A cold or structurally invalidated workspace additionally asks for `ShellExecution` permission under the original tool name and caller context, publishing permission events. No tool argument can grant that permission. Read-only cold queries return `PermissionDenied`; an already approved valid snapshot supports read-only queries without evaluation.


## Reversible rename

`csharp_rename_symbol` selects an exact symbol and takes `newName`. Roslyn computes the complete solution edit; the tool rejects invalid identifiers, unresolved conflicts, new compiler errors, generated documents, paths outside the canonical workspace, and disagreeing linked-document edits. It does not rename files or change string/comment occurrences.

The tool requires `FileSystemWrite` permission and follows existing workspace-write behavior. Cold evaluation additionally requires `ShellExecution`. A workspace lock serializes semantic writes across processes. Content preconditions run before the first write and again before each write, so external edits are preserved when detected. The complete successful edit set is recorded in chronological order for normal session checkpoint undo/redo. Source-only cache refresh observes rename, undo, redo and editor changes.

On failure or cancellation, rollback uses a separate bounded token. It does not overwrite a file whose content no longer matches the planned after-state. Residual changes are recorded and failed results list their paths and rollback errors. The filesystem does not provide a multi-file atomic transaction: external editors can still race between a check and a write. Recovery/undo checks content again.

Mutation snapshots preserve text and supported newline sequences. The existing text contract does not preserve original encoding or byte-order marks byte-for-byte; local writes use UTF-8. Cancellation still propagates after cleanup, with residual records retained for turn finalization.
