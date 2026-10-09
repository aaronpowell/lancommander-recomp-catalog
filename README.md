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
`3a8cec3ff5cdf7b3c71d5042117eb168865c1c0e`, which carries the current
integration-boundary server plugin access-policy and Server.UI contracts that are not upstream
yet.

```powershell
$env:LANCOMMANDER_ROOT = "D:\copilot-app\copilot-worktrees\LANCommander\aaronpowell-potential-funicular"
dotnet build "$env:LANCOMMANDER_ROOT\LANCommander.SDK\LANCommander.SDK.csproj" -f net10.0
dotnet build "$env:LANCOMMANDER_ROOT\LANCommander.Server.Data\LANCommander.Server.Data.csproj"
dotnet build "$env:LANCOMMANDER_ROOT\LANCommander.Server.Services\LANCommander.Server.Services.csproj"
dotnet build "$env:LANCOMMANDER_ROOT\LANCommander.Server.ImportExport\LANCommander.Server.ImportExport.csproj"
dotnet build "$env:LANCOMMANDER_ROOT\LANCommander.Server.Plugins\LANCommander.Server.Plugins.csproj"
dotnet build "$env:LANCOMMANDER_ROOT\LANCommander.Server.UI\LANCommander.Server.UI.csproj"
dotnet build Hex1bCatalog.slnx
dotnet test Hex1bCatalog.Tests\Hex1bCatalog.Tests.csproj
```

You can also pass `-p:LANCommanderRoot=D:\src\LANCommander`. The build resolves
`LANCommander.SDK.dll`, `LANCommander.Server.Plugins.dll`, and
`LANCommander.Server.UI.dll`, `LANCommander.Server.Services.dll`,
`LANCommander.Server.Data.dll`, and `LANCommander.Server.ImportExport.dll` from each project's
`bin\Debug\net10.0` directory.

## Server plugin

`LANCommander.RecompCatalog.Plugin` is the server-side plugin. It registers an
`IServerRouteAssemblyExtension` and an `IServerNavigationExtension`, and serves an
administrator-only catalog browser at `/Plugins/RecompCatalog`.

The browser loads a Quiver-compatible catalog index, maps its lists into a searchable view, and
supports deterministic tag, original-platform, and target-platform filters when those facets are
present in the feed. Selecting an entry shows its catalog provenance, repository, project, tags,
and platform information. "Check release and prepare" resolves the latest GitHub release and lists
selectable Windows ZIP/EXE assets without downloading them.

Catalog entries now have a canonical identity built from the configured feed, source list,
repository provider/path, and stable entry key. LANCommander games are matched by the
`RecompCatalog.Identity` custom field; packages produced by the prototype write that field plus
the exact `RecompCatalog.Release` identity and serialized `RecompCatalog.UpdatePolicy`. Existing
prototype packages that predate those fields
are detected conservatively by their deterministic game ID, but their exact imported release is
reported as unknown rather than guessed from a display name.

The browser distinguishes never imported, imported/current, update available, imported release
unknown, and state-check failure. Initial browsing only reads LANCommander game/custom-field/version
state and does not call release providers per entry. A release check is explicit and compares exact
provider, repository, tag, selected asset name, and asset URL identity. A difference is reported as
an available update without assuming arbitrary tags are SemVer or claiming which tag is newer.

Selecting a release asset creates a reviewable import preparation containing immutable catalog and
release provenance plus the future Package-script policy: repository provider/path, release asset
filter, selected asset, archive format, and normalization behavior. Metadata lookup is then
explicitly invoked through LANCommander's configured `MetadataService`/`IMetadataProvider`
implementations. The operator can edit every imported metadata field, either from catalog-seeded
defaults or after explicitly applying a lookup result. Lookup never overwrites the draft
automatically, and a zero-result search displays an explicit empty state with guidance to shorten
the query or continue with manual entry. Catalog and release provenance remain read-only and
separate from the editable metadata draft.

After review, the plugin downloads the selected release asset into controlled temporary storage,
enforces the prototype's compressed-size, expanded-size, entry-count, expansion-ratio, path, and
symbolic-link safety checks, and normalizes ZIP or single-EXE releases without executing them. The
administrator explicitly chooses the primary executable when several candidates exist. The plugin
then builds an LCX carrying the edited metadata and immutable provenance, and streams it through
LANCommander's canonical `ImportRunner` pipeline. Temporary downloads and LCX files are removed on
discard, successful import, component disposal, and failed preparation.

The generated LCX also includes a deterministic game `Package` script. LANCommander's existing
package schedule can run it to compare the imported game version with the repository's latest
GitHub release, select an asset using the saved filter/name/format policy, and return
`New-Package` only when a different release is available. The script validates that the saved
executable path still exists in the new payload before returning an update.

The plugin is independent of Quiver and does not bundle a catalog snapshot. Quiver inspired the
workflow, and compatible public feeds are treated as untrusted remote input: invalid URLs, HTTP
errors, malformed documents, empty feeds, and release lookup failures are displayed to the
administrator rather than silently replaced with fallback data.

### Catalog configuration

The established prototype feed is used by default:

```text
https://raw.githubusercontent.com/tgeorgiadis/quiver-community-app-catalog/main/index.json
```

Override it in LANCommander configuration with:

```json
{
  "Plugins": {
    "RecompCatalog": {
      "FeedUrl": "https://catalog.example.test/index.json"
    }
  }
}
```

The URL must be absolute HTTPS. Set `Plugins:RecompCatalog:FeedUrl` to an empty value to show the
explicit unconfigured state. GitHub release preview uses `GH_TOKEN` or `GITHUB_TOKEN` when present,
matching the prototype conventions; unauthenticated public-repository lookup remains supported at
GitHub's lower rate limit.

The current import boundary uses LANCommander's host-provided `ImportRunner.RunStreamAsync`, which
reuses the same import context, queue preparation, archive storage, and persistence path as the
server API. The plugin never writes game/archive rows directly. The imported package carries the
catalog identity, exact release identity, and serialized update policy so later catalog refreshes
can classify existing games without title matching.

The host contract, UI, service, and import assemblies (`LANCommander.SDK`,
`LANCommander.Server.Plugins`, `LANCommander.Server.UI`, `LANCommander.Server.Services`,
`LANCommander.Server.Data`, and `LANCommander.Server.ImportExport`)
are referenced with `Private="false"`. Plugin output
contains the plugin plus its private `Hex1bCatalog.Core`, `Hex1bCatalog.Infrastructure`, and
`Hex1bCatalog.Providers` assemblies, but no host or Radzen binaries. This is required: the server
must provide the already-loaded copies so contract types and Razor controls keep a single identity
across the plugin's isolated load context.

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
page at `/Plugins/RecompCatalog` renders its loading, failure, empty, filters, imported/update
states, details, release check, and lookup-assisted preparation with
`LANCommander.Server.UI.Controls`. Import then downloads and normalizes the selected asset, requires
an executable selection, builds an LCX, and submits it to LANCommander's canonical importer.

The navigation entry only controls visibility. Access is enforced by the server from the page's
`[PluginAccess(PluginAccessLevel.Administrator)]` declaration, and a plugin page that declares
nothing is administrator-only by default.

### Host load-context prerequisite

The current LANCommander plugin load context must treat `LANCommander.Server.UI` and
`LANCommander.Server.Services` as shared host assemblies, alongside
`LANCommander.Server.Plugins`, so the plugin resolves the server's already-loaded UI and service
assemblies instead of loading second copies. `LANCommander.Server.Data` is also compile-time only
and must resolve from the host; it must not be copied into the plugin directory. The pinned host
already shares Server.Services and resolves its Data dependency from the default load context.
This repository intentionally does not modify LANCommander; update the host-side shared-assembly
list before deploying to a build that does not share Server.UI or Server.Services.

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
