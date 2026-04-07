# Adopt Now Workflows Design

**Goal:** Add the next high-value SharpClaw Code workflow features in a way that fits the current architecture: custom command files, plan/build primary modes, session fork, `@file` prompt references, external editor compose, and session export.

**Scope split:** Deliver in two milestones.

- **Milestone 1:** custom commands, plan/build primary modes, `@file` references
- **Milestone 2:** session fork, external editor compose, session export

## Current codebase baseline

The current solution already has the right major seams for these features:

- `SharpClaw.Code.Cli` builds a Generic Host and composes `AddSharpClawRuntime()` with `AddSharpClawCli()`.
- `SharpClaw.Code.Commands` owns `System.CommandLine` handlers, REPL orchestration, slash command parsing, and output dispatch.
- `SharpClaw.Code.Runtime` owns `ConversationRuntime`, `IRuntimeCommandService`, turn execution, session lifecycle, and operational diagnostics.
- `SharpClaw.Code.Agents` owns the Microsoft Agent Framework bridge and provider-backed agent execution.
- `SharpClaw.Code.Tools` owns the tool registry and permission-gated tool execution.
- `SharpClaw.Code.Permissions` owns the rule pipeline, approvals, and session-scoped remembered approvals.
- `SharpClaw.Code.Sessions` persists `ConversationSession` snapshots and append-only NDJSON runtime event streams under `.sharpclaw/sessions/`.
- `SharpClaw.Code.Protocol` already carries shared DTOs, enums, runtime events, command results, and the source-generated `ProtocolJsonContext`.

That means the new workflow features should be implemented as small typed services layered into Runtime and Commands, not as large new handlers or agent-specific one-offs.

## Architecture

### Design principles

- Keep workflow behavior explicit and typed.
- Reuse runtime/session/event-first patterns rather than introducing parallel persistence.
- Keep command handlers thin and move reusable behavior into services.
- Preserve existing permission architecture and extend it where needed.
- Use `System.Text.Json` contracts in `Protocol` for machine-readable outputs.
- Avoid coupling session/admin workflows to `AgentFrameworkBridge`.

### New model additions

Add shared contracts in `SharpClaw.Code.Protocol`:

- `PrimaryMode` enum
  - `Build`
  - `Plan`
- custom command DTOs
  - `CustomCommandDefinition`
  - `CustomCommandArgumentMetadata` (minimal metadata holder)
  - `CustomCommandSourceScope`
  - `CustomCommandDiscoveryIssue`
  - `CustomCommandCatalogSnapshot`
  - `CustomCommandInvocationRequest`
  - `CustomCommandInvocationResult`
- prompt reference DTOs
  - `PromptReferenceKind`
  - `PromptReference`
  - `PromptReferenceResolution`
  - `PromptReferenceError`
- session export DTOs
  - `SessionExportFormat`
  - `SessionExportDocument`
  - `SessionExportTurn`
  - `SessionExportToolAction`
- optional session runtime event additions
  - `SessionForkedEvent`

These should be registered in `ProtocolJsonContext` so JSON output stays stable and source-generated.

### New service additions

Add narrow services instead of growing handlers:

In `SharpClaw.Code.Infrastructure`:

- `IUserSharpClawPathService`
  - resolves `~/.sharpclaw/...` paths in a cross-platform way
- `IExternalEditorService`
  - opens a temp file in a configured editor and returns composed content

In `SharpClaw.Code.Runtime`:

- `ICustomCommandDiscoveryService`
- `IMarkdownCustomCommandParser`
- `IPrimaryModePolicy`
- `IPromptReferenceResolver`
- `ISessionForkService`
- `ISessionExportService`
- runtime-facing permission helper for prompt references

In `SharpClaw.Code.Commands`:

- thin command handlers for `commands`, `mode`, `export`, and new `session fork` surface
- dynamic command registration support so discovered custom commands can be exposed as top-level CLI commands and REPL slash commands

## Milestone 1

### Custom command files

#### Discovery roots

Support command files in:

- workspace-local: `{workspace}/.sharpclaw/commands/*.md`
- global: `~/.sharpclaw/commands/*.md`

Workspace commands override global commands on the same command name.

#### File model

Each markdown file maps to `CustomCommandDefinition`.

Supported frontmatter fields:

- `description`
- `agent`
- `model`
- `permissionMode`
- `primaryMode`
- `arguments`
- `tags`

Body content after frontmatter is the prompt template exactly as written.

Unknown frontmatter keys should be preserved in an extension bag for forward compatibility, but ignored by execution for now.

#### Parsing rules

- Frontmatter is only parsed when the file starts with `---`.
- Frontmatter parsing remains intentionally small and permissive.
- Invalid files should not crash startup.
- Discovery should return both valid definitions and non-fatal discovery issues.

#### Command naming

- File name without `.md` becomes the command name.
- The same discovered command appears as:
  - a REPL slash command: `/<name>`
  - a top-level CLI command: `<name>`

#### Template substitution

Support:

- `$ARGUMENTS` for all positional arguments joined with spaces
- `$1`, `$2`, ... for positional arguments

Missing positional values resolve to empty strings.

No named arguments, branching, or function-like templating in v1.

#### Command execution

Execution path:

1. Command handler resolves the discovered command by name.
2. Runtime builds a `CustomCommandInvocationRequest`.
3. Runtime renders the template with argument substitution.
4. Runtime resolves effective execution settings:
   - agent override if present
   - model override if present
   - permission mode override if present
   - primary mode override if present, otherwise inherit active mode
5. Runtime runs `@file` preprocessing on the rendered template.
6. Runtime executes the resulting prompt through the normal prompt path.

This keeps custom commands as a thin specialization of normal prompt execution rather than a separate execution engine.

#### Listing and refresh

Add inspectable command management:

- CLI:
  - `commands list`
  - `commands refresh`
- REPL:
  - `/commands`
  - `/commands refresh`

`/help` should include discovered custom commands alongside built-in slash commands.

Dynamic discovery should happen when the command surface is built, and refresh should invalidate/reload the in-memory catalog.

### Plan vs Build primary modes

#### Core shape

Add a first-class `PrimaryMode`:

- `Build` (default)
- `Plan`

This mode is orthogonal to `PermissionMode`. It is not a replacement for `readOnly` / `workspaceWrite` / `dangerFullAccess`.

#### Where mode lives

Primary mode should be present in:

- CLI context
- REPL current session/context
- custom command frontmatter override
- runtime command context
- tool execution context
- permission evaluation context

For persistence in v1, store active mode in session metadata using a stable key rather than immediately restructuring `ConversationSession`.

#### Behavior

`Build`:

- current execution behavior

`Plan`:

- uses the same runtime and agent pipeline
- changes default mutation posture
- should feel analysis-first without creating a separate runtime stack

#### Permission integration

Implement `Plan` mode through a new permission rule, not by adding a fourth `PermissionMode`.

Recommended rule:

- `PrimaryModeMutationRule`

Behavior in `Plan`:

- allow reads/searches/non-mutating operations
- deny mutating file and shell actions by default
- continue to use approval flow where the existing architecture already expects approval, especially for out-of-workspace prompt reference reads

This keeps plan/build semantically distinct from trust and approval mode.

#### User surfaces

Add:

- global CLI flag: `--primary-mode build|plan`
- REPL:
  - `/mode`
  - `/mode build`
  - `/mode plan`

`status` should surface current/default primary mode when available.

### `@file` prompt references

#### Supported syntax in v1

- `@relative/path/to/file.ext`
- `@/absolute/path/to/file.ext`
- multiple references in one prompt

These should work in:

- direct prompt requests
- custom command templates after substitution

#### Resolution flow

Add `IPromptReferenceResolver` in Runtime.

Resolution steps:

1. Scan prompt for `@...` file tokens.
2. Resolve each token against working directory/workspace.
3. Validate boundaries and permission requirements.
4. Read file content safely.
5. Produce:
   - original prompt
   - expanded prompt
   - structured resolved reference metadata

#### Access policy

- Relative paths resolve from the active working directory.
- Absolute paths inside the workspace are allowed as normal reads.
- Absolute paths outside the workspace require approval.
- Missing, unreadable, or denied references fail the prompt request with a clear user-facing error.

#### Expansion behavior

Use explicit expansion markers, not silent raw concatenation:

```text
[Referenced file: path]
<content>
[End referenced file: path]
```

The original prompt should remain available in metadata/trace so later exports and debugging can show both the source prompt and the expanded form.

#### Extensibility

Represent each resolved token as a typed `PromptReference`. This keeps the resolver extensible for future syntax:

- `@file#L10-L40`
- `@diff`
- `@branch`

## Milestone 2

### Session fork

#### API shape

Add a runtime operation rather than copying files directly:

- `ForkSessionAsync(...)`

Expose it through:

- CLI: `session fork --id <sourceId>`
- REPL: `/session fork [id]`

If no id is provided in REPL, use the current/latest session in the current workspace.

#### Fork semantics

Forking creates a new child session with:

- a new session id
- its own `session.json`
- its own `events.ndjson`
- its own future checkpoint chain
- explicit parent-child linkage in metadata

Per chosen behavior, the child inherits:

- selected source metadata
- a compact derived history summary

The child does not duplicate the full source event log.

Recommended metadata fields:

- `parentSessionId`
- `forkedFromCheckpointId`
- `forkedAtUtc`
- `forkHistorySummary`

Emit an explicit `SessionForkedEvent` so the action is observable and exportable.

### External editor compose

#### Service

Implement in Infrastructure:

- `IExternalEditorService`

#### Behavior

For REPL slash command `/editor`:

1. Resolve editor command from environment in order:
   - `SHARPCLAW_EDITOR`
   - `VISUAL`
   - `EDITOR`
2. Create a temporary compose file.
3. Launch the editor process and wait for completion.
4. Read resulting content.
5. Return composed text to the REPL input flow.

Graceful behavior:

- no editor configured -> user-facing guidance
- launch failure -> user-facing error
- empty result -> treat as canceled compose

This should use infrastructure process abstractions where possible, without overbuilding a full terminal/editor framework.

### Session export

#### Service

Add `ISessionExportService`.

#### Formats

- `md`
- `json`

#### Output path behavior

Support explicit output path or default workspace-local destination:

- `{workspace}/.sharpclaw/exports/...`

#### Markdown export contents

- session metadata
- turn ordering
- prompts
- assistant responses
- concise notable tool actions
- important failure/cancellation markers

#### JSON export contents

- session metadata
- ordered turns
- selected structured runtime events
- fork lineage
- schema version

JSON export should use explicit Protocol DTOs rather than anonymous objects.

#### Surfaces

- REPL:
  - `/export md`
  - `/export json`
- CLI:
  - `session export --format md --id <id> [--out path]`
  - `session export --format json --id <id> [--out path]`

## Error handling

Across both milestones:

- malformed custom command files should be discoverable warnings, not process-fatal startup errors
- denied or unresolved `@file` references should fail the specific invocation with a clear message
- plan-mode mutation denials should explain that the current primary mode blocks mutating actions
- session fork should fail clearly when the source session cannot be resolved
- export should fail clearly on missing session or unwritable output path
- editor compose should distinguish configuration problems from launch failures and user cancellation

## Testing strategy

### Unit tests

Add focused tests for:

- custom command frontmatter parsing
- command precedence (workspace over global)
- template substitution (`$ARGUMENTS`, `$1`, `$2`)
- primary mode policy and `PrimaryModeMutationRule`
- prompt reference token parsing and expansion
- out-of-workspace absolute reference approval behavior
- export document shaping
- session fork metadata and summary derivation
- editor environment resolution

### Integration tests

Add integration coverage for:

- CLI and REPL command discovery/listing/refresh
- custom command execution using runtime prompt path
- plan/build mode propagation into tool permission evaluation
- prompt failure on denied `@file` reads
- session fork producing linked child sessions
- session export output files

### Parity harness

Add milestone-appropriate parity scenarios where they add value:

- custom command roundtrip
- prompt reference allowed/denied
- plan mode mutation blocked
- session fork lineage
- export roundtrip

## File structure guidance

The implementation should favor small focused files over pushing everything into:

- `ConversationRuntime`
- `ReplHost`
- `CliCommandFactory`
- any single command handler

Likely decomposition:

- parser/discovery services in Runtime
- editor path/process helpers in Infrastructure
- new Protocol records/enums for stable output and shared contracts
- command handlers only for wiring and rendering

## Deliberate non-goals for this slice

- no complex custom command templating language
- no line-range `@file#Lx-Ly` support yet
- no full event duplication on fork
- no editor-managed drafts store beyond temp files
- no broad doctor/status expansion beyond light primary-mode visibility
