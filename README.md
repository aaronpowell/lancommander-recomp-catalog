# LANCommander Recomp Catalog Prototype

A Phase 0 proof-of-concept for turning a Quiver-compatible application catalog entry and a
GitHub release asset into a LANCommander LCX package.

This project is independent of Quiver and is not affiliated with, maintained by, sponsored by,
or endorsed by Quiver or its maintainers. Quiver inspired the workflow, and its public community
catalog feed format and data are compatibility inputs only. The prototype does not bundle,
mirror, or depend on the Quiver application.

## Prototype workflow

1. Load a configurable catalog index.
2. Browse GitHub-backed entries in a Hex1b terminal UI.
3. Resolve the latest GitHub release and select a Windows ZIP or EXE asset.
4. Download, hash, and normalize the payload without executing it.
5. Select the primary executable.
6. Generate an LCX with deterministic game/archive IDs and source provenance.

ZIP normalization rejects rooted and traversal paths, limits download/expanded sizes, limits
entry counts and expansion ratios, and requires at least one executable. This is a prototype,
not a trust guarantee: administrators must review upstream projects and release artifacts.

## Build

The prototype intentionally references locally built LANCommander DLLs until the packaging APIs
are distributed as stable packages. It is pinned for CI to LANCommander revision
`c207d4368eab14f16d24f3b08b86f74bf546313b`.

```powershell
$env:LANCOMMANDER_ROOT = "D:\copilot-app\copilot-worktrees\LANCommander\aaronpowell-potential-funicular"
dotnet build "$env:LANCOMMANDER_ROOT\LANCommander.SDK\LANCommander.SDK.csproj" -f net10.0
dotnet build "$env:LANCOMMANDER_ROOT\LANCommander.Packaging\LANCommander.Packaging.csproj" -f net10.0
dotnet build Hex1bCatalog.slnx
dotnet test Hex1bCatalog.Tests\Hex1bCatalog.Tests.csproj
```

You can also pass `-p:LANCommanderRoot=D:\src\LANCommander`. The build resolves
`LANCommander.SDK.dll`, `LANCommander.Packaging.dll`, and
`LANCommander.Packaging.Abstractions.dll` from each project's `bin\Debug\net10.0` directory.

## Run

```powershell
dotnet run --project Hex1bCatalog.Tui -- `
  --feed https://raw.githubusercontent.com/tgeorgiadis/quiver-community-app-catalog/main/index.json `
  --output .\packages
```

Use `--list-only` for a non-interactive feed connectivity/schema check, or
`--inspect owner/repository` to verify release resolution and Windows asset filtering.

Exercise the full normalization/manifest/LCX pipeline without network calls:

```powershell
dotnet run --project Hex1bCatalog.Tui -- `
  --exercise .\path\to\fixture.zip `
  --executable bin\game.exe `
  --output .\packages
```
`--build owner/repository` runs the complete pipeline with the first matching asset and
executable; pass `--exe relative/path.exe` when an artifact contains multiple executables.

Set `GITHUB_TOKEN` to increase the GitHub API rate limit. Only public repository access is
needed. The initial prototype supports GitHub releases and Windows `.zip`/`.exe` assets.

## Phase 0 gaps

- GitHub's latest release endpoint is the only release provider/strategy.
- Catalog icons and extra file acquisition are not embedded in the LCX.
- The operator must assess upstream licensing and supply-chain trust.
- LCX timestamps are assigned by LANCommander packaging APIs, so IDs are deterministic but the
  package is not byte-for-byte reproducible.
