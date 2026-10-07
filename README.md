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

The prototype intentionally references locally built LANCommander DLLs until the packaging and
plugin APIs are distributed as stable packages. CI pins
[`aaronpowell/LANCommander`](https://github.com/aaronpowell/LANCommander) at
`226a1e6b1a5a86275872c002094cf8461c7b9898`, which carries the server plugin contracts that are not
upstream yet.

```powershell
$env:LANCOMMANDER_ROOT = "D:\copilot-app\copilot-worktrees\LANCommander\aaronpowell-potential-funicular"
dotnet build "$env:LANCOMMANDER_ROOT\LANCommander.SDK\LANCommander.SDK.csproj" -f net10.0
dotnet build "$env:LANCOMMANDER_ROOT\LANCommander.Packaging\LANCommander.Packaging.csproj" -f net10.0
dotnet build "$env:LANCOMMANDER_ROOT\LANCommander.Server.Plugins\LANCommander.Server.Plugins.csproj"
dotnet build "$env:LANCOMMANDER_ROOT\LANCommander.Server.UI\LANCommander.Server.UI.csproj"
dotnet build Hex1bCatalog.slnx
dotnet test Hex1bCatalog.Tests\Hex1bCatalog.Tests.csproj
```

You can also pass `-p:LANCommanderRoot=D:\src\LANCommander`. The build resolves
`LANCommander.SDK.dll`, `LANCommander.Packaging.dll`,
`LANCommander.Packaging.Abstractions.dll`, `LANCommander.Server.Plugins.dll`, and
`LANCommander.Server.UI.dll` from each project's `bin\Debug\net10.0` directory.

## Server plugin

`LANCommander.RecompCatalog.Plugin` is the server-side plugin. It currently contains no catalog
functionality — it exists to prove that a plugin living in this repository can contribute a page
to LANCommander Server. It registers an `IServerRouteAssemblyExtension` and an
`IServerNavigationExtension`, and serves a single server-styled page at
`/Plugins/RecompCatalog`.

The host contract and UI assemblies (`LANCommander.SDK`, `LANCommander.Server.Plugins`, and
`LANCommander.Server.UI`) are referenced with `Private="false"`, so the plugin's output contains
only its own assembly. This is required: the server must provide the already-loaded copies so
contract types and Razor controls keep a single identity across the plugin's isolated load
context. Shipping copies would break plugin registration or create a second UI component type
unusable by the host.

`LANCommander.Server.UI` is not published as a NuGet package yet. The explicit assembly reference
is a temporary development bridge; set `LANCommanderRoot` (or `LANCOMMANDER_ROOT`) to a built
LANCommander checkout. The plugin does not copy Server.UI, Radzen, or any other server UI
dependency into its deployment folder.

Deploy it by copying the build output into a folder named after the assembly, under the server's
`Data/Plugins` directory:

```powershell
$dest = "$env:LANCOMMANDER_ROOT\LANCommander.Server\Data\Plugins\LANCommander.RecompCatalog.Plugin"
New-Item -ItemType Directory -Path $dest -Force
Copy-Item LANCommander.RecompCatalog.Plugin\bin\Debug\net10.0\* $dest -Force
```

Start the server and sign in as an administrator. The sidebar gains a "Recomp Catalog" entry, and
startup logs `Loaded plugin 'Recomp Catalog' (dev.aaronpowell.lancommander.recompcatalog)`. The
page at `/Plugins/RecompCatalog` renders `PageHeader`, `PageContent`, `Alert`, `Card`, and
`Typography` from `LANCommander.Server.UI.Controls`.

The navigation entry only controls visibility. Access is enforced by the server from the page's
`[PluginAccess(PluginAccessLevel.Administrator)]` declaration, and a plugin page that declares
nothing is administrator-only by default.

### Host load-context prerequisite

The current LANCommander plugin load context must treat `LANCommander.Server.UI` as a shared
host assembly, alongside `LANCommander.Server.Plugins`, so the plugin resolves the server's
already-loaded UI assembly instead of loading a second copy. This repository intentionally does
not modify the LANCommander host; update that host-side shared-assembly list before deploying this
plugin to a server build whose load context does not already share `LANCommander.Server.UI`.

## Run

```powershell
dotnet run --project Hex1bCatalog.Tui -- `
  --feed https://raw.githubusercontent.com/tgeorgiadis/quiver-community-app-catalog/main/index.json `
  --output .\packages
```

Authenticate GitHub API requests to avoid the low anonymous rate limit:

```powershell
$env:GH_TOKEN = "your-token"
# GITHUB_TOKEN is also supported.
dotnet run --project Hex1bCatalog.Tui -- --inspect BanjoRecomp/BanjoRecomp
```

For a differently named environment variable, pass `--github-token-env VARIABLE_NAME`.
The token is read from the environment, attached only to `api.github.com` requests, and is
never written to disk or included in LCX metadata. Public repositories require no token
permissions; a fine-grained token with public-repository access is sufficient.

Use `--list-only` for a non-interactive feed connectivity/schema check, or
`--inspect owner/repository` to verify release resolution and Windows asset filtering.

Exercise the full normalization/manifest/LCX pipeline without network calls:

```powershell
dotnet run --project Hex1bCatalog.Tui -- `
  --exercise .\path\to\fixture.zip `
  --executable bin\game.exe `
  --output .\packages
```
When the API rejects a token or exhausts a rate limit, the prototype reports an actionable
error and includes the reset time when GitHub provides one. The initial prototype supports
GitHub releases and Windows `.zip`/`.exe` assets.

## Phase 0 gaps

- GitHub's latest release endpoint is the only release provider/strategy.
- Catalog icons and extra file acquisition are not embedded in the LCX.
- Original-game ROM provisioning is deferred. A future plugin version should let an
  administrator provide a ROM as a LANCommander redistributable and use an
  install/post-install script to copy it to the recomp's required path. The plugin must not
  download or bundle ROMs.
- Initial launcher validation reached game startup after import/install, but LANCommander
  returned a 404 from `GameClient.GetAllocatedKeyAsync` while retrieving allocated keys. This
  is tracked as a separate LANCommander launcher/server issue rather than a prototype
  import/package failure.
- The operator must assess upstream licensing and supply-chain trust.
- LCX timestamps are assigned by LANCommander packaging APIs, so IDs are deterministic but the
  package is not byte-for-byte reproducible.
