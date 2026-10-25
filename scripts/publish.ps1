<#
.SYNOPSIS
Builds a Windows x64 release of OpenTRCC and zips it for distribution.

.DESCRIPTION
Default output is a self-contained single-file build (no .NET runtime needed).
Use -FrameworkDependent for a ~10 MB build that requires the .NET 9 desktop runtime.

.EXAMPLE
./scripts/publish.ps1
./scripts/publish.ps1 -Version 0.1.0 -FrameworkDependent
#>
param(
    [string]$Version = "0.1.0",
    [string]$Runtime = "win-x64",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root ".dotnet\dotnet.exe"
if (-not (Test-Path $dotnet)) {
    $dotnet = "dotnet"
}

$out = Join-Path $root "artifacts\publish\$Runtime"
$zip = Join-Path $root "artifacts\OpenTRCC-$Version-$Runtime.zip"

if (Test-Path $out) {
    Remove-Item -Recurse -Force $out
}

$arguments = @(
    "publish",
    (Join-Path $root "src\OpenTRCC.App\OpenTRCC.App.csproj"),
    "-c", "Release",
    "-r", $Runtime,
    "-o", $out,
    "-p:Version=$Version",
    "-p:ContinuousIntegrationBuild=true",
    "--nologo"
)

if ($FrameworkDependent) {
    $arguments += @("--self-contained", "false")
}
else {
    $arguments += @(
        "--self-contained", "true",
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:EnableCompressionInSingleFile=true"
    )
}

Write-Host "publishing OpenTRCC $Version ($Runtime)..." -ForegroundColor Cyan
& $dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (Test-Path $zip) {
    Remove-Item $zip -Force
}

Compress-Archive -Path (Join-Path $out "*") -DestinationPath $zip -CompressionLevel Optimal

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "zip: $zip ($size MB)" -ForegroundColor Green
