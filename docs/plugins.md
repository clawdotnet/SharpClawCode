# Plugins

**Project:** `SharpClaw.Code.Plugins`  
**Registration:** `PluginsServiceCollectionExtensions.AddSharpClawPlugins` (via **`AddSharpClawTools`** → Runtime)

## On-disk layout

Under the workspace (see **`PluginLocalStore`**):

```
{workspace}/.sharpclaw/plugins/{pluginId}/
  manifest.json
  state.json          # LoadedPlugin snapshot
  package.txt         # optional, from install request
```

**`PluginManager`** validates manifests, writes state, emits telemetry/events when **`IRuntimeEventPublisher`** is present, and uses **`IPluginLoader`** (default out-of-process path) for tool execution.

Only plugins in **`PluginLifecycleState.Enabled`** surface tool descriptors to **`IPluginManager.ListToolDescriptorsAsync`** (**`ToolRegistry`** consumes this).

## Trust and permissions

Manifest trust flows into **Protocol** models and **`PermissionEvaluationContext`** (`ToolOriginatingPluginId`, **`PluginTrustRule`**, etc.).

## CLI

**`plugins`** command (**`PluginsCommandHandler`**):

- **`list`** — installed plugins
- **`install`** / **`update`** — **`--manifest`** path to JSON → **`PluginManifest`**
- **`enable`** / **`disable`** / **`uninstall`** — **`--id`**

JSON output uses **`ProtocolJsonContext`** for **`LoadedPlugin`**, **`List<LoadedPlugin>`**, and simple maps where applicable.

Manifest parsing in **`LoadInstallRequestAsync`** uses **`JsonSerializer.Deserialize` with `JsonSerializerDefaults.Web`** (see **`ARCHITECTURE-NOTES.md`** for tightening).

## Adding a plugin (operator)

1. Prepare a **`PluginManifest`** compatible with **`PluginManifestValidator`**.
2. Run **`plugins install --manifest path/to/manifest.json`** with **`--cwd`** set to the workspace.

## Adding plugin-hosted tools (developer)

Tool **descriptors** come from the manifest and loader; execution goes through **`PluginToolProxyTool`** and **`IPluginManager.ExecuteToolAsync`**. Ensure the plugin process/tool contract matches **`PluginToolDescriptor`** and **`ToolExecutionRequest`**.

For **tests only**, see **`ParityFixturePluginTool`** in the parity harness (extra **`ISharpClawTool`** registration).
