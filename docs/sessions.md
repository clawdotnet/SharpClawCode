# Sessions

SharpClaw persists **per-workspace** conversation state under **`.sharpclaw/`** at the workspace root.

## Layout

Defined in **`SessionStorageLayout`** (`src/SharpClaw.Code.Sessions/Storage/SessionStorageLayout.cs`):

| Path | Purpose |
|------|---------|
| `{workspace}/.sharpclaw/sessions/{sessionId}/session.json` | **Snapshot** of **`ConversationSession`** (JSON via **`ProtocolJsonContext.ConversationSession`**) |
| `{workspace}/.sharpclaw/sessions/{sessionId}/events.ndjson` | **Append-only** newline-delimited **`RuntimeEvent`** JSON |
| `{workspace}/.sharpclaw/sessions/{sessionId}/checkpoints/` | Optional checkpoint JSON files (`ICheckpointStore`) |
| `{workspace}/.sharpclaw/sessions/{sessionId}/mutations/{id}.json` | **Mutation sets** for checkpoint-backed **`/undo`** / **`/redo`** (`MutationSetDocument`) |
| `{workspace}/.sharpclaw/active-session.json` | **Workspace attachment** — which session id is preferred when none is passed explicitly |

Creating a session ensures **`.sharpclaw`** exists (`ConversationRuntime.CreateSessionAsync`).

## Abstractions

- **`ISessionStore`** — `SaveAsync`, `GetByIdAsync`, `GetLatestAsync`, `ListAllAsync` (implementation: **`FileSessionStore`**).
- **`IMutationSetStore`** — durable **`MutationSetDocument`** payloads under **`mutations/`** (**`FileMutationSetStore`**).
- **`IWorkspaceSessionAttachmentStore`** — reads/writes **`active-session.json`** (**`FileWorkspaceSessionAttachmentStore`**).
- **`IEventStore`** — `AppendAsync`, `ReadAllAsync` (implementation: **`NdjsonEventStore`**).
- **`IRuntimeEventPersistence`** — bridges telemetry to the event store (**`EventStoreRuntimeEventPersistence`**).

When **`IRuntimeEventPublisher.PublishAsync`** is called with **`RuntimeEventPublishOptions`** that request persistence (see **`ToolExecutor`** / runtime), events are appended to **`events.ndjson`** for the active session.

## CLI inspection

```bash
dotnet run --project src/SharpClaw.Code.Cli -- session show
dotnet run --project src/SharpClaw.Code.Cli -- session show --id <sessionId>
```

REPL slash command: **`/session`** (optional session id argument) maps to the same **`InspectSessionAsync`** path.

Output includes **Protocol** **`SessionInspectionReport`** in JSON mode (includes optional **`UndoRedo`** snapshot when mutation state exists).

## Multi-session

- **`session list`** — machine-readable rows (`SessionSummaryRow`) with attachment markers.
- **`session attach --id`** / **`session detach`** — updates **`active-session.json`**; also use global **`--session`** on prompts.
- **Lineage** — fork metadata remains on **`ConversationSession`**; listing is ordered by `UpdatedAtUtc`.

## Undo / redo

SharpClaw records **file mutations** from successful tool runs (`write_file`, `edit_file`) into **mutation sets** keyed by checkpoint id. Undo/redo state is stored in session metadata (`SharpClawWorkflowMetadataKeys.UndoRedoStateJson`).

```bash
dotnet run --project src/SharpClaw.Code.Cli -- undo
dotnet run --project src/SharpClaw.Code.Cli -- redo
dotnet run --project src/SharpClaw.Code.Cli -- undo --id <sessionId>
```

REPL: **`/undo`**, **`/redo`**.

## Portable bundle export / import

Separate from Markdown/JSON transcript export:

```bash
dotnet run --project src/SharpClaw.Code.Cli -- session bundle --out ./snap.zip
dotnet run --project src/SharpClaw.Code.Cli -- session import --from ./snap.zip
dotnet run --project src/SharpClaw.Code.Cli -- session import --from ./snap.zip --replace --attach
```

Produces a **zip** with **`SessionBundleManifest`** and a copy of the session tree. **`session import`** extracts into **`.sharpclaw/sessions/{id}/`** (schema **1.0** only). **`--replace`** overwrites an existing session id; **`--attach`** sets **`active-session.json`** after import.

REPL: **`/session import <pathToZip>`** (no flags; use CLI for **`--replace`** / **`--attach`**).

## IDE bridge (local)

Local JSON-lines ingress for **`EditorContextPayload`** (feeds **`RunPromptRequest`** metadata via **`IEditorContextIngress`**). Pick one transport:

```bash
# TCP (default): loopback only
dotnet run --project src/SharpClaw.Code.Cli -- bridge listen --port 17337

# Unix domain socket (macOS / Linux; also supported on recent Windows with a filesystem path)
dotnet run --project src/SharpClaw.Code.Cli -- bridge listen --unix-socket /tmp/sharpclaw-bridge.sock

# Named pipe (Windows only)
dotnet run --project src/SharpClaw.Code.Cli -- bridge listen --pipe SharpClawBridge
```

Send one JSON object per line (see **`EditorContextPayload`** in Protocol).

## In-memory vs durable

**`SessionApprovalMemory`** (permissions) is **process-lifetime only** — not the same as durable session files.
