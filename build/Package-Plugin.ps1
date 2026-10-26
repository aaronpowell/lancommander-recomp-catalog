#Requires -Version 7
<#
.SYNOPSIS
    Builds the Recomp Catalog server plugin and packages it as a distributable ZIP.

.DESCRIPTION
    The ZIP contains a single top-level LANCommander.RecompCatalog.Plugin folder, which is
    extracted as-is into the LANCommander server's Data/Plugins directory. Packaging fails if any
    host-provided assembly would be shipped, because duplicate copies break contract and Razor
    type identity inside the plugin's isolated load context.

.PARAMETER Version
    Package version. Defaults to the <Version> in the plugin project. A leading "v" is removed.

.PARAMETER OutputDirectory
    Where the ZIP and its .sha256 file are written. Defaults to ./artifacts.
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$pluginName = 'LANCommander.RecompCatalog.Plugin'
$project = Join-Path $repositoryRoot "$pluginName\$pluginName.csproj"

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repositoryRoot 'artifacts'
}

if (-not $Version) {
    $Version = @(([xml] (Get-Content -Raw $project)).Project.PropertyGroup.Version | Where-Object { $_ })[0]
    if (-not $Version) {
        throw "No -Version was given and $project does not declare <Version>."
    }
}

$Version = $Version -replace '^v', ''
$publishDirectory = Join-Path $repositoryRoot "artifacts\publish\$pluginName"

if (Test-Path $publishDirectory) {
    Remove-Item -LiteralPath $publishDirectory -Recurse -Force
}

$publishArguments = @(
    'publish', $project,
    '-c', 'Release',
    '-o', $publishDirectory,
    '--nologo',
    "-p:Version=$Version"
)
dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

# The server does not need debug symbols.
Get-ChildItem -LiteralPath $publishDirectory -Filter '*.pdb' | Remove-Item -Force

$forbidden = @(
    'LANCommander.SDK.dll',
    'LANCommander.Server.Plugins.dll',
    'LANCommander.Server.UI.dll',
    'LANCommander.Server.Services.dll',
    'LANCommander.Server.Data.dll',
    'LANCommander.Server.ImportExport.dll',
    'LANCommander.Packaging.dll',
    'LANCommander.Packaging.Abstractions.dll'
)
$shipped = @($forbidden | Where-Object { Test-Path (Join-Path $publishDirectory $_) })
$shipped += @(Get-ChildItem -LiteralPath $publishDirectory -Filter 'Radzen*.dll' | Select-Object -ExpandProperty Name)
if ($shipped.Count -gt 0) {
    throw "Plugin output contains host assemblies: $($shipped -join ', ')"
}

if (-not (Test-Path (Join-Path $publishDirectory "$pluginName.dll"))) {
    throw "Plugin output is missing $pluginName.dll."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$zipPath = Join-Path $OutputDirectory "$pluginName-$Version.zip"
if (Test-Path $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

# Compress the folder itself so extraction yields Data/Plugins/LANCommander.RecompCatalog.Plugin.
Compress-Archive -Path $publishDirectory -DestinationPath $zipPath -CompressionLevel Optimal

$hash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$zipPath.sha256" -Value "$hash  $(Split-Path -Leaf $zipPath)" -NoNewline

Write-Host "Packaged $zipPath"
Write-Host "SHA-256  $hash"
Get-ChildItem -LiteralPath $publishDirectory -Name | ForEach-Object { Write-Host "  $pluginName/$_" }

if ($env:GITHUB_OUTPUT) {
    "zip=$zipPath" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "version=$Version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
