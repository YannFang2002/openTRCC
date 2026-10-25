# Pushes the current HEAD snapshot through the GitHub REST API.
# Needed because github.com:443 is blocked here while api.github.com is reachable.
param(
    [string]$Owner = "YannFang2002",
    [string]$Repo = "openTRCC",
    [string]$Branch = "master"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $token = (("protocol=https`nhost=github.com`n`n" | git credential fill) -split "`n" |
        Where-Object { $_ -like "password=*" }) -replace "password=", ""
    if (-not $token) { throw "no stored GitHub credential found" }

    $headers = @{
        Authorization = "token $token"
        "User-Agent"  = "OpenTRCC-release"
        Accept        = "application/vnd.github+json"
    }

    $base = "https://api.github.com/repos/$Owner/$Repo"

    # The git data API refuses to create blobs in a repository with no commits,
    # so seed it with a placeholder README first (the final tree overwrites it).
    $branchExists = $true
    try { $null = Invoke-RestMethod "$base/git/ref/heads/$Branch" -Headers $headers -TimeoutSec 20 }
    catch { $branchExists = $false }

    if (-not $branchExists) {
        $seed = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("# OpenTRCC"))
        $null = Invoke-RestMethod "$base/contents/README.md" -Method Put -Headers $headers -TimeoutSec 60 -Body (
            @{ message = "Initialize repository"; content = $seed; branch = $Branch } | ConvertTo-Json -Compress)
        Write-Host "seeded empty repository" -ForegroundColor Yellow
    }

    $files = @(git ls-files)
    Write-Host "files: $($files.Count)" -ForegroundColor Cyan

    $treeEntries = New-Object System.Collections.Generic.List[object]
    $index = 0
    foreach ($file in $files) {
        $index++
        $path = Join-Path $root $file
        if (-not (Test-Path -LiteralPath $path)) {
            Write-Host "skip missing: $file" -ForegroundColor Yellow
            continue
        }

        $bytes = [System.IO.File]::ReadAllBytes($path)
        $payload = @{
            content  = [Convert]::ToBase64String($bytes)
            encoding = "base64"
        } | ConvertTo-Json -Compress

        $blob = Invoke-RestMethod "$base/git/blobs" -Method Post -Headers $headers -Body $payload -TimeoutSec 60
        $treeEntries.Add(@{
            path = ($file -replace "\\", "/")
            mode = "100644"
            type = "blob"
            sha  = $blob.sha
        })

        if ($index % 20 -eq 0) {
            Write-Host "  blobs: $index/$($files.Count)"
        }
    }

    $treeBody = @{ tree = $treeEntries } | ConvertTo-Json -Depth 5 -Compress
    $tree = Invoke-RestMethod "$base/git/trees" -Method Post -Headers $headers -Body $treeBody -TimeoutSec 120
    Write-Host "tree: $($tree.sha)"

    $message = "OpenTRCC 0.1.0 snapshot"
    $commit = Invoke-RestMethod "$base/git/commits" -Method Post -Headers $headers -TimeoutSec 60 -Body (
        @{ message = $message; tree = $tree.sha; parents = @() } | ConvertTo-Json -Compress)

    $exists = $false
    try {
        $null = Invoke-RestMethod "$base/git/ref/heads/$Branch" -Headers $headers -TimeoutSec 20
        $exists = $true
    }
    catch {
        $exists = $false
    }

    if ($exists) {
        $null = Invoke-RestMethod "$base/git/refs/heads/$Branch" -Method Patch -Headers $headers -TimeoutSec 60 -Body (
            @{ sha = $commit.sha; force = $true } | ConvertTo-Json -Compress)
    }
    else {
        $null = Invoke-RestMethod "$base/git/refs" -Method Post -Headers $headers -TimeoutSec 60 -Body (
            @{ ref = "refs/heads/$Branch"; sha = $commit.sha } | ConvertTo-Json -Compress)
    }

    Write-Host "pushed commit $($commit.sha)" -ForegroundColor Green
}
finally {
    Pop-Location
}
