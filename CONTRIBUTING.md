# Contributing to SharpClaw Code

Thank you for helping improve SharpClaw Code. Open an issue before a large or compatibility-sensitive change so the architecture and migration path can be agreed first.

## Development setup

Install the .NET 10 SDK and Node.js 22 when working on the VS Code extension. Then run:

```shell
dotnet restore SharpClawCode.sln
dotnet build SharpClawCode.sln --configuration Release --warnaserror
dotnet test SharpClawCode.sln --configuration Release
```

For the editor extension, run `npm ci` and `npm run compile` from `extensions/vscode`.

## Pull requests

Keep changes focused and preserve serialized contracts unless the pull request includes a versioned migration. Add tests for behavior changes, pass cancellation tokens through I/O, retain permission gates for dangerous actions, and update user-facing documentation. Never commit credentials, local `.sharpclaw` state, or provider output containing private prompts.

By participating, you agree to follow the [Code of Conduct](CODE_OF_CONDUCT.md). Security vulnerabilities should be reported through the process in [SECURITY.md](SECURITY.md), not a public issue.
