$log = Join-Path $env:TEMP 'opentrcc-restart.log'
"$(Get-Date -Format 'HH:mm:ss') restart requested" | Set-Content $log -Encoding UTF8

Get-Process OpenTRCC -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Start-ScheduledTask -TaskName 'OpenTRCC_Startup'
Start-Sleep -Seconds 6

$proc = Get-Process OpenTRCC -ErrorAction SilentlyContinue | Select-Object -First 1
if ($proc) {
    "$(Get-Date -Format 'HH:mm:ss') started pid=$($proc.Id) path=$($proc.Path)" | Add-Content $log -Encoding UTF8
}
else {
    "$(Get-Date -Format 'HH:mm:ss') not started" | Add-Content $log -Encoding UTF8
}
