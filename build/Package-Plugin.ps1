#Requires -Version 7
<#
.SYNOPSIS
    Builds the Recomp Catalog server plugin and packages it as a NuGet package.

.DESCRIPTION
    The package contains the plugin and its private assemblies under lib/net10.0 for installation
    through LANCommander's Plugins page or a local NuGet feed. Packaging fails if any
    host-provided assembly would be shipped, because duplicate copies break contract and Razor
    type identity inside the plugin's isolated load context.

.PARAMETER Version
    Package version. Defaults to the <Version> in the plugin project. A leading "v" is removed.

.PARAMETER OutputDirectory
    Where the .nupkg and its .sha256 file are written. Defaults to ./artifacts.
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
$packArguments = @(
    'pack', $project,
    '-c', 'Release',
    '-o', $OutputDirectory,
    '--nologo',
    "-p:Version=$Version"
)
dotnet @packArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE."
}

$packagePath = Join-Path $OutputDirectory "$pluginName.$Version.nupkg"
$archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
try {
    $files = @($archive.Entries | Where-Object { $_.FullName.StartsWith('lib/') -and $_.Name } |
        ForEach-Object { $_.FullName })
    $expected = @(
        'LANCommander.RecompCatalog.Core.dll',
        'LANCommander.RecompCatalog.Infrastructure.dll',
        'LANCommander.RecompCatalog.Plugin.dll',
        'LANCommander.RecompCatalog.Providers.dll'
    ) | ForEach-Object { "lib/net10.0/$_" }
    $difference = @(Compare-Object $expected $files)
    if ($difference.Count -gt 0) {
        throw "Unexpected plugin payload. Expected only: $($expected -join ', '). Found: $($files -join ', ')."
    }

    $nuspec = $archive.Entries | Where-Object { $_.FullName -eq "$pluginName.nuspec" }
    $reader = [System.IO.StreamReader]::new($nuspec.Open())
    try {
        $metadata = ([xml] $reader.ReadToEnd()).package.metadata
    }
    finally {
        $reader.Dispose()
    }
    $types = @($metadata.packageTypes.packageType | ForEach-Object { $_.name })
    if ('LANCommander.Plugin' -notin $types -or 'LANCommander.Plugin.Server' -notin $types) {
        throw 'Package must declare both LANCommander.Plugin and LANCommander.Plugin.Server package types.'
    }
    if (@($metadata.SelectNodes('.//dependency')).Count -gt 0) {
        throw 'Plugin package must not declare NuGet dependencies; LANCommander does not install them.'
    }
}
finally {
    $archive.Dispose()
}

$hash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath "$packagePath.sha256" -Value "$hash  $(Split-Path -Leaf $packagePath)" -NoNewline

Write-Host "Packaged $packagePath"
Write-Host "SHA-256  $hash"
$files | ForEach-Object { Write-Host "  $_" }

if ($env:GITHUB_OUTPUT) {
    "package=$packagePath" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "version=$Version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}
