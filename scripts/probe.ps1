#Requires -Version 5.1
<#
.SYNOPSIS
    Runs OpenTRCC.Probe with the repo-local SDK.

.EXAMPLE
    powershell -File scripts\probe.ps1 list
    powershell -File scripts\probe.ps1 handshake --dump
#>
[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $ProbeArgs
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $repo '.dotnet\dotnet.exe'

if (-not (Test-Path $dotnet)) {
    throw 'Local SDK not found. Run: powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-dotnet.ps1'
}

$env:DOTNET_ROOT = Join-Path $repo '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

$project = Join-Path $repo 'tools\OpenTRCC.Probe\OpenTRCC.Probe.csproj'
& $dotnet run --project $project -c Debug -- @ProbeArgs
exit $LASTEXITCODE
