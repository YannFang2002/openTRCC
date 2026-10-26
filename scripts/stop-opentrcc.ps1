$log = Join-Path $env:TEMP 'opentrcc-stop.log'
Get-Process OpenTRCC -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
"stopped at $(Get-Date -Format 'HH:mm:ss') running=$([bool](Get-Process OpenTRCC -ErrorAction SilentlyContinue))" |
    Set-Content $log -Encoding UTF8
