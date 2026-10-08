# Self-contained binary distribution

The release workflow creates six native CLI archives alongside the 22 production NuGet packages and their symbol packages. Windows archives are ZIP files; Linux and macOS archives use tar.gz. Each contains `sharpclaw` (or `sharpclaw.exe`), `LICENSE`, and `README.md`, with an adjacent SHA-256 checksum.

| Runtime | Matching smoke runner |
| --- | --- |
| win-x64 | windows-2025 |
| win-arm64 | windows-11-arm |
| linux-x64 | ubuntu-24.04 |
| linux-arm64 | ubuntu-24.04-arm |
| osx-x64 | macos-15-intel |
| osx-arm64 | macos-15 |

Runner labels follow the [GitHub hosted runner reference](https://docs.github.com/en/actions/reference/runners/github-hosted-runners). Each smoke script verifies its actual process OS and architecture before execution. Cross-publishing never substitutes for running the target executable.

Extract the archive and run it directly:

```shell
./sharpclaw version
./sharpclaw --cwd ./workspace --output-format json index refresh
./sharpclaw mcp serve --cwd ./workspace
```

Basic commands run without an installed .NET runtime. Roslyn/MSBuild semantic loading and build/test verification require a compatible installed .NET SDK and restored project assets. Restore remains explicit. Provider-dependent prompts also need provider configuration. The NuGet CLI tool retains its normal framework-dependent installation behavior.

The single-file bundle includes native libraries and all Roslyn helper content, extracted by the .NET host as required. Publishing is self-contained with trimming and AOT disabled. Release-only `PackAsTool=false` resolves the SDK's single-file/tool packaging conflict without changing ordinary tool packages. Assembly and informational versions receive the tag identity.

For local packaging and matching-platform verification:

```powershell
pwsh -File .github/scripts/Publish-Binaries.ps1 -Runtime osx-arm64 -Version 0.1.0-preview.1
pwsh -File .github/scripts/Test-Binary.ps1 -Runtime osx-arm64 -Version 0.1.0-preview.1 -Archive artifacts/binaries/sharpclaw-0.1.0-preview.1-osx-arm64.tar.gz
```

Smoke tests validate the checksum and three-file archive, unpack into a clean directory, start `version` with an empty PATH and isolated runtime roots, run offline non-.NET commands and SQLite index state, then launch the relocated executable from real SDK MCP clients. Those clients exercise read-only denials, semantic evaluation, reversible rename, and actual build/TRX test results. The installed SDK is used only for the SDK-dependent acceptance portion.

The release workflow validates build, tests, scenarios, documentation, dependency audit, and local NuGet installation before binary jobs. NuGet push and GitHub release assembly wait for all six native smoke jobs. Local success does not prove the other platform jobs have run. No release is created until a version tag triggers the gated workflow.
