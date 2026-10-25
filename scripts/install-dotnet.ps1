#Requires -Version 5.1
<#
.SYNOPSIS
    Installs a repository-local .NET 9 SDK into .dotnet\ and pins it in global.json.

.DESCRIPTION
    No admin rights required and no machine-wide changes are made. The global
    dotnet.exe on PATH does NOT discover an SDK that lives inside this repo, so
    every build must go through .dotnet\dotnet.exe (scripts\build.ps1 does that).

.PARAMETER Channel
    SDK channel to install, defaults to 9.0.

.PARAMETER Force
    Reinstall even if a local SDK is already present.
#>
[CmdletBinding()]
param(
    [string] $Channel = '9.0',
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repo = Split-Path -Parent $PSScriptRoot
$installDir = Join-Path $repo '.dotnet'
$dotnet = Join-Path $installDir 'dotnet.exe'
$globalJson = Join-Path $repo 'global.json'

# Local SDK install is per-process only; no machine-wide policy change.
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass -Force -ErrorAction SilentlyContinue

function Write-GlobalJson([string] $SdkVersion) {
    $payload = [ordered]@{
        sdk = [ordered]@{
            version          = $SdkVersion
            rollForward      = 'latestPatch'
            allowPrerelease  = $false
        }
    }
    $json = $payload | ConvertTo-Json -Depth 4
    # UTF-8 without BOM: some tooling chokes on a BOM inside global.json.
    [System.IO.File]::WriteAllText($globalJson, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "global.json -> sdk $SdkVersion"
}

if ((Test-Path $dotnet) -and -not $Force) {
    $existing = & $dotnet --version 2>$null
    if ($LASTEXITCODE -eq 0 -and $existing) {
        Write-Host "Local SDK already installed: $existing"
        Write-GlobalJson $existing
        return
    }
}

New-Item -ItemType Directory -Force -Path $installDir | Out-Null

$installer = Join-Path $env:TEMP 'dotnet-install.ps1'
if (-not (Test-Path $installer) -or $Force) {
    Write-Host 'Downloading dotnet-install.ps1 ...'
    Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer -UseBasicParsing
}

Write-Host "Installing .NET SDK channel $Channel into $installDir ..."
& $installer -Channel $Channel -InstallDir $installDir -NoPath
# dotnet-install.ps1 is a script, so $LASTEXITCODE is not reliable here; verify the artifact instead.
if (-not (Test-Path $dotnet)) {
    throw 'dotnet-install.ps1 did not produce .dotnet\dotnet.exe.'
}

$sdkLines = & $dotnet --list-sdks
if (-not $sdkLines) { throw 'No SDK found after install.' }
$sdkVersion = ($sdkLines | Select-Object -First 1).Split(' ')[0]

Write-GlobalJson $sdkVersion
Write-Host ''
Write-Host "Done. SDK $sdkVersion installed."
Write-Host "Build with:  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build.ps1"
Write-Host "Or set:      `$env:DOTNET_ROOT = '$installDir'"
