# SharpClaw Code VS Code Extension

This extension launches the SharpClaw ACP host as a subprocess and uses it as the single transport for prompts, workspace search, memory management, and approval requests.

Available commands:

- `SharpClaw: Prompt`
- `SharpClaw: Refresh Index`
- `SharpClaw: Search Workspace`
- `SharpClaw: Save Memory`
- `SharpClaw: List Memory`
- `SharpClaw: Select Model`

What it currently does:

- sends the active editor file and selection as `editorContext` on prompt requests
- streams assistant output into the SharpClaw output channel
- surfaces approval prompts from the ACP host and routes the response back over `approval/respond`
- refreshes and queries the workspace knowledge index
- saves and lists structured project/user memory
- lists models from the provider catalog, including local runtime profiles exposed through the OpenAI-compatible provider

By default the extension runs:

```bash
dotnet run --project src/SharpClaw.Code.Cli -- acp
```

Override that command through the `sharpClaw.cliCommand` and `sharpClaw.cliArgs` settings if you want to point the extension at an installed CLI or a different workspace layout.
