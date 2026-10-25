#Requires -Version 5.1
<#
.SYNOPSIS
    Builds (and optionally tests) OpenTRCC with the repo-local SDK.
#>
[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $Test,
    [switch] $NoRestore
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
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

$sln = Join-Path $repo 'OpenTRCC.sln'
$buildArgs = @('build', $sln, '-c', $Configuration, '-nologo')
if ($NoRestore) { $buildArgs += '--no-restore' }

& $dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if ($Test) {
    & $dotnet test $sln -c $Configuration --no-build -nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
