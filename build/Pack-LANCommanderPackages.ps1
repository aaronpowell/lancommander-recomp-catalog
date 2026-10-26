#Requires -Version 7
<#
.SYNOPSIS
    Packs the LANCommander host packages into a local NuGet feed.

.DESCRIPTION
    Interim step until a LANCommander prerelease containing the plugin data contracts
    (IMetadataLookup, version redistributables, bulk custom fields) is published to nuget.org.
    Packs LANCommander.SDK, LANCommander.Server.Plugins and LANCommander.Server.UI from a
    LANCommander checkout into artifacts/lancommander-feed, which Directory.Build.props adds as a
    restore source when it exists. Once the packages are published, delete the feed and set
    LANCommanderPackageVersion in Directory.Build.props to the published version.

.PARAMETER LANCommanderRoot
    LANCommander checkout to pack. Defaults to the LANCOMMANDER_ROOT environment variable.

.PARAMETER Version
    Package version. Defaults to LANCommanderPackageVersion in Directory.Build.props.
#>
[CmdletBinding()]
param(
    [string] $LANCommanderRoot = $env:LANCOMMANDER_ROOT,
    [string] $Version
)

$ErrorActionPreference = 'Stop'

if (-not $LANCommanderRoot -or -not (Test-Path (Join-Path $LANCommanderRoot 'LANCommander.Server.Plugins'))) {
    throw 'Pass -LANCommanderRoot (or set LANCOMMANDER_ROOT) to a LANCommander checkout.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not $Version) {
    $props = [xml] (Get-Content -Raw (Join-Path $repositoryRoot 'Directory.Build.props'))
    $Version = $props.SelectSingleNode('//LANCommanderPackageVersion').InnerText.Trim()
}
if (-not $Version) {
    throw 'No -Version was given and Directory.Build.props does not declare LANCommanderPackageVersion.'
}

$feed = Join-Path $repositoryRoot 'artifacts\lancommander-feed'
New-Item -ItemType Directory -Force -Path $feed | Out-Null

$ui = Join-Path $LANCommanderRoot 'LANCommander.Server.UI'
if (-not (Test-Path (Join-Path $ui 'node_modules'))) {
    Push-Location $ui
    try {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed with exit code $LASTEXITCODE." }
    }
    finally { Pop-Location }
}

$packages = 'LANCommander.SDK', 'LANCommander.Server.Plugins', 'LANCommander.Server.UI'
foreach ($package in $packages) {
    dotnet pack (Join-Path $LANCommanderRoot "$package\$package.csproj") -c Release "-p:Version=$Version" -o $feed --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack $package failed with exit code $LASTEXITCODE." }

    # Repacking the same version must not resolve a stale copy from the global packages folder.
    $globalPackages = (dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
    $cached = Join-Path $globalPackages.Trim() "$($package.ToLowerInvariant())\$($Version.ToLowerInvariant())"
    if (Test-Path $cached) { Remove-Item -LiteralPath $cached -Recurse -Force }
}

Write-Host "Packed $($packages -join ', ') $Version into $feed"
