<#
.SYNOPSIS
    Builds the self-contained single-file W10Wheel.exe and packages it into a zip.

.DESCRIPTION
    The version is read from PROGRAM_VERSION in W10Wheel/src/AppDef.fs.
    The zip is written to artifacts/W10Wheel.NET-<version>.zip.
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

dotnet publish (Join-Path $root 'W10Wheel/W10Wheel.fsproj') -p:PublishProfile=win-x64
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)" }

$name = "W10Wheel.NET-$version"
$staging = Join-Path $OutDir $name
$zip = Join-Path $OutDir "$name.zip"

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item $staging -ItemType Directory -Force | Out-Null

Copy-Item (Join-Path $root 'W10Wheel/bin/publish/win-x64/W10Wheel.exe') $staging
Copy-Item (Join-Path $root 'dist/*') $staging
Copy-Item (Join-Path $root 'License.txt') $staging
Copy-Item (Join-Path $root 'Library.txt') $staging
Copy-Item (Join-Path $root 'AccelTable.txt') $staging

Compress-Archive -Path $staging -DestinationPath $zip
Write-Host "Created: $zip"

if ($env:GITHUB_OUTPUT) {
    "version=$version" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
    "zip=$zip" | Out-File $env:GITHUB_OUTPUT -Append -Encoding utf8
}
