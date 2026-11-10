# SharpClaw Code VS Code Extension

This extension launches the SharpClaw ACP host as a subprocess and uses it as the single transport for prompts, workspace search, memory management, and approval requests.

Available commands:

- `SharpClaw: Prompt`
- `SharpClaw: Steer Active Turn`
- `SharpClaw: Cancel Active Turn`
- `SharpClaw: Refresh Index`
- `SharpClaw: Search Workspace`
- `SharpClaw: Save Memory`
- `SharpClaw: List Memory`
- `SharpClaw: Select Model`

What it currently does:

- sends the active editor file and selection as `editorContext` on prompt requests
- shows turn progress and the final assistant response in the SharpClaw output channel
- surfaces approval prompts from the ACP host and routes the response back over `approval/respond`
- refreshes and queries the workspace knowledge index
- saves and lists structured project/user memory
- lists models from the provider catalog, including local runtime profiles exposed through the OpenAI-compatible provider

By default the extension runs:

```bash
sharpclaw acp
```

Install the .NET tool first (`dotnet tool install --global SharpClaw.Code.Cli --prerelease`). Override `sharpClaw.cliCommand` and `sharpClaw.cliArgs` in user settings to select another installation. Workspace values are ignored, and the extension does not activate in untrusted workspaces. For source development, set the user command to `dotnet` and args to `["run", "--project", "/absolute/path/to/SharpClaw.Code.Cli", "--", "acp"]`.

While a prompt runs, use **Steer Active Turn** to queue a correction or **Cancel Active Turn** to stop it. Commands target the active editor’s workspace folder. The output channel displays corrections that still need to be resubmitted when execution ends. Live controls require a host advertising `liveTurnControl`. Older hosts continue to support ordinary prompts.
