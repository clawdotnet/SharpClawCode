# Launch findings review

Reviewed against the working tree on November 5, 2026. The supplied report identifies real defects, but its severity counts and some feature descriptions overstate what was established. Changes below address the confirmed execution boundaries and correctness failures. This review does not certify a launch or claim live-provider acceptance.

## Security findings

| Finding | Assessment and change |
| --- | --- |
| C1: unauthenticated HTTP and caller-selected permissions | Valid. Every endpoint now requires a host-supplied secret of at least 32 characters (`SHARPCLAW_SERVER_TOKEN`). Origin-bearing requests and unexpected Host values are rejected. Request permissions cannot exceed host permissions, and an explicitly read-only workflow cannot be raised by request JSON. Workspace configuration cannot set listener or approval-auth configuration. Share URLs also require authentication. |
| C2: repository `appsettings.json` changes credential destinations | Valid. CLI startup disables default .NET configuration sources and adds environment configuration explicitly. Repository appsettings and configuration command-line overrides are not loaded. Embedded hosts remain responsible for the configuration they explicitly supply. |
| C3: repository hooks execute automatically | Valid. Only user configuration supplies executable hooks; workspace hooks are ignored. User hooks are an explicit executable opt-in, including in read-only sessions. Hook failures now honor exit codes and propagate caller cancellation. |
| C4: self-trusted enabled plugin executes without approval | Valid. Manifest trust and stored workspace trust cannot waive plugin process approval. Calls require an explicit approval that cannot be remembered or scope-auto-approved; Plan, Research, and ReadOnly block plugin process tools. Explicit plugin-enable commands still run the requested health check. |
| Permission rules bypass the mode | Valid. Mode restrictions and all deny rules win before any approval. Invalid runtime mode enums are denied. Non-rememberable security rules cannot consume scope auto-approval. |
| `RequiresApproval` ignored | Valid. Enforced outside explicitly selected DangerFullAccess. Existing noninteractive write fixtures now supply file approval or a scripted approver. |
| Broad, opaque remembered approvals | Valid. Prompts include tool, working directory, and exact arguments. Memory keys include arguments, and expired approvals are ignored. Authenticated hosts revalidate each approval before any memory/auto-approval shortcut, closing an additional tenant-identity bypass found during testing. |
| `web_fetch` SSRF | Valid. Uses network approval scope. Production HTTP connections validate and pin public DNS answers, disable proxy routing, validate redirects, and block private, loopback, link-local, documentation (TEST-NET, 2001:db8::/32), and IPv4-embedding transition (6to4, Teredo) destinations, while public 192.0.x hosts remain reachable. Supported schemes are HTTP(S), without URL credentials. Response bodies are read up to 5 MiB. |
| Plugin/skill path traversal | Valid. Package IDs reject path syntax on all platforms, canonical package directories must remain in the catalog, and workspace-local catalogs cannot resolve outside the workspace through links. |
| Dangling link write escape | Valid. Canonicalization resolves link targets even when the destination does not exist, with a link-depth limit. |
| HTTP tenant/storage header injection and event leakage | Valid. Identity and storage routing come from the host context, not request headers or JSON. Event filtering always compares tenant IDs, including the absence of a tenant. |
| VS Code workspace executable settings | Valid. Launch settings are machine scoped, execution reads only user values, and untrusted workspaces are unsupported and checked before spawning. Default is the installed `sharpclaw acp` command. |

## Functional findings

| Finding | Assessment and change |
| --- | --- |
| H1: FTS operators crash prompts | Valid. User tokens are quoted before prefix matching. Regression cases execute real SQLite searches for AND, OR, and NOT. |
| H2: ordinary `@` text becomes missing files | Valid for the supplied examples. Email boundaries are respected; nonexistent ordinary mentions remain literal, while existing extensionless files/directories still expand and explicit relative/absolute missing references retain diagnostics. Text references have a 200 KiB limit. |
| H3: whitespace stream chunks dropped | Valid. Both agent output assembly and persisted provider delta events preserve whitespace-only chunks. |
| H4: retired Anthropic default | Valid. Default changed to `claude-sonnet-5-5`, an active model in the [Anthropic lifecycle documentation](https://platform.claude.com/docs/en/about-claude/model-deprecations). Custom model aliases remain under host control. |
| H5: Ctrl+C terminates REPL | Valid at CLI composition. The REPL already has turn controls, but starting ConsoleLifetime also canceled its lifetime. CLI commands now own their lifetime. This was checked manually with a pseudo-terminal against a blocked local mock-provider request; there is no automated regression test. |
| H6: stack traces, broken JSON errors, slash failures | Valid. CLI invocation disables the default exception printer and emits the selected error envelope, including parser/option errors. Slash-command exceptions are contained by the REPL. The JSON envelope cases were checked manually; automated coverage is limited to invalid-mode rejection. |
| H7: invalid modes become permissive defaults | Valid. Unknown permission, primary, output, and store modes raise explicit errors. |
| H8: OpenAI finish reason drops tool calls | Early termination is confirmed; the report's claim about every live tool call was not proven. Adapter now drains later tool and usage updates and rejects length/content-filter termination. A regression stream puts the function call and usage after the finish reason. No paid/live API request was made. |
| H9: main-agent outbound MCP calls absent | Valid for workspace registrations. Documentation now states lifecycle/discovery support only. Inbound MCP tool serving and the direct Parallel Search example do execute tools; they are separate paths. Main-agent outbound tool invocation remains unimplemented. |
| Extension launch requires this source repo | Valid. Default now launches the installed tool; documentation includes an absolute-project user-setting override for development. |

## Business logic and durability

| Finding | Assessment and change |
| --- | --- |
| Custom commands raise Research to Build | Valid. Research now survives command-mode clamping, alongside Plan and Spec. |
| CLI overrides agent/saved primary modes | Valid. Omitted primary mode remains unspecified at the runtime boundary; saved session mode wins over agent defaults on resume. |
| Anthropic token accounting absent | Valid. Message-start and cumulative delta usage are recorded, including cache-read and cache-creation input. Providers that omit usage still use the existing approximate fallback. |
| Provider stop reason ignored | Valid. Anthropic tool calls are buffered until a successful message stop; truncated, interrupted, or unsupported continuation responses fail without releasing tools. OpenAI length/content-filter responses also fail. |
| Empty responses create fake replies | Valid. A turn that produced neither text nor tool results now fails explicitly; a turn that ran tools and ended without closing text completes. The unused placeholder helper was removed. |
| Circuit breaker reset by failed events | Valid. Failed events count as failures, with authentication classification retained. Regression verifies a 429 failed event opens the circuit. |
| Session saved before turn lock | Valid. Preference writes happen after the mutex, cross-process lock, and snapshot reload. |
| Undo/redo cancellation strands edits | Valid. Cancellation is accepted before mutation begins; once application starts, the operation finishes and saves its state despite caller cancellation. Filesystem failure or process death still requires the existing recovery mechanisms; this is not a filesystem transaction. |
| Nonatomic snapshots/torn NDJSON | Valid. Local text state uses same-directory temporary files and atomic replacement. Appends use an exclusive lock and discard an incomplete trailing record before writing the next event. Disk-loss/power-loss durability is not guaranteed by atomic rename alone. |
| One malformed plugin disables tools | Valid. Invalid manifests/states are logged and skipped, conflicting plugin tools are omitted, and built-in lookup is independent of plugin discovery. |
| MCP cancellation leaves Starting | Valid. Interrupted startup persists Faulted using a cleanup token and disposes a newly established session if needed. Stop checks cancellation before removing the tracked client. |
| ACP ignores cancel notifications | Valid for notifications without IDs. A spec-shaped `session/cancel` notification (only `sessionId`, optional `cwd`) cancels that session's live-controlled prompts on the connection, without a JSON-RPC response. Prompts that did not opt into `liveTurnControl` are not tracked and cannot yet be cancelled this way. Request-form cancels keep their `promptRequestId` targeting. |
| Every CLI command starts scheduler | Valid. CLI bootstrap removes the scheduler hosted service and no longer starts the host implicitly. Embedded-host scheduling remains available; explicit schedule commands retain their own workspace context. |
| Bash lacks bounds and denylist misses variants | Valid. Process execution defaults to a two-minute deadline; agent shell commands get ten minutes and external agents thirty. Each output stream retains at most one MiB characters (head and tail, with an omission marker) instead of failing, and a background child that keeps the pipe open no longer blocks completion. Processes are killed on failure or cancellation. Recursive `rm` in any flag form (`-r`, `-rf`, `-Rf`, `-f -r`, `--recursive`) and `find` delete/exec actions receive a non-auto-approvable gate. These patterns are not a complete shell sandbox. |

## Performance and feature limits

- Fixed: Anthropic client reuse; Anthropic automatic cache control; private SQLite caches with WAL and busy timeout; generated/dependency-directory pruning and link avoidance in grep/glob; reusable bounded glob regexes; grep regex timeout, binary detection, and large-file exclusion; read-file and text-reference size limits.
- Still valid and open: webhook deliveries block their publishing call; hook configuration is reread for lifecycle events; event appends reopen the log; source traversal does not implement full `.gitignore` semantics.
- Prompt SSE currently replays events after execution, and ACP sends final assistant text. `/v1/admin/events/stream` does stream runtime envelopes. Documentation now distinguishes these behaviors; incremental model-token delivery remains a larger provider/fallback orchestration change.
- No benchmark establishes the report's exact latency or cost multipliers. The existing fallback character-based token estimate is still approximate when a provider omits usage.
- Main-agent outbound MCP calls are deliberately not advertised as implemented. Adding them requires tool discovery, namespacing, permission-aware invocation, and lifecycle tests, rather than only exposing the SDK client.

## Validation

Regression coverage includes actual SQLite FTS, dangling links, path and session IDs, private and public HTTP destinations and redirects, bounded fetch bodies, permission-mode precedence, recursive-delete variants, exact approval arguments, post-finish tool calls, tool-only turns, Anthropic usage/truncation, circuit-breaker failures, HTTP authentication/Origin checks, HTTP prompt permission and primary-mode clamping, tenant identity, custom-command mode preservation, CLI scheduler removal, spec-shaped ACP cancel notifications, snapshot replacement with preserved file modes, torn log repair, batched event appends, and bounded process output.

Not covered by automated tests: the Ctrl+C REPL behavior and CLI JSON error envelopes (checked manually), the Host-header rejection (the loopback listener rejects foreign hosts itself, so the application check is defense in depth for wildcard prefixes), MCP Faulted-on-cancel, undo/redo under cancellation, and the two-minute default deadline. Direct CLI smoke checks used a temporary workspace and local mock provider with a fake API key. No production provider key was sent.
