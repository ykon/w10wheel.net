<#
.SYNOPSIS
    Builds the single-file W10Wheel.exe, with and without the .NET runtime, and packages each
    into a zip.

.DESCRIPTION
    The version is read from PROGRAM_VERSION in W10Wheel/src/AppDef.fs.
    The zips are written to artifacts/:
      W10Wheel.NET-<version>.zip             self-contained (no .NET installation required)
      W10Wheel.NET-<version>-no-runtime.zip  framework-dependent (the .NET 11 Desktop Runtime
                                             (x64) must be installed)
    Used both locally and by .github/workflows/build.yml.

.EXAMPLE
    pwsh build/package.ps1
#>
param(
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts' }

$appDef = Get-Content (Join-Path $root 'W10Wheel/src/AppDef.fs') -Raw
if ($appDef -notmatch 'PROGRAM_VERSION = "([^"]+)"') {
    throw 'PROGRAM_VERSION not found in W10Wheel/src/AppDef.fs'
}
$version = $Matches[1]
Write-Host "Version: $version"

# Publishes with the profile and packages its W10Wheel.exe with the documents into $name.zip,
# returned (the output of dotnet goes to the host, not to the result)
function New-Package([string]$publishProfile, [string]$name) {
    dotnet publish (Join-Path $root 'W10Wheel/W10Wheel.fsproj') "-p:PublishProfile=$publishProfile" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($publishProfile) failed (exit code $LASTEXITCODE)" }

    $staging = Join-Path $OutDir $name
    $zip = Join-Path $OutDir "$name.zip"

    if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
    if (Test-Path $zip) { Remove-Item $zip -Force }
    New-Item $staging -ItemType Directory -Force | Out-Null

    Copy-Item (Join-Path $root "W10Wheel/bin/publish/$publishProfile/W10Wheel.exe") $staging
    Copy-Item (Join-Path $root 'dist/*') $staging
    Copy-Item (Join-Path $root 'License.txt') $staging
    Copy-Item (Join-Path $root 'Library.txt') $staging
    Copy-Item (Join-Path $root 'AccelTable.txt') $staging

    Compress-Archive -Path $staging -DestinationPath $zip
    Write-Host "Created: $zip"
    $zip
}

$zip = New-Package 'win-x64' "W10Wheel.NET-$version"
$zipNoRuntime = New-Package 'win-x64-no-runtime' "W10Wheel.NET-$version-no-runtime"

if ($env:GITHUB_OUTPUT) {
    "version=$version" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
    "zip=$zip" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
    "zip_no_runtime=$zipNoRuntime" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
}
