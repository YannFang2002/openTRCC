$log = Join-Path $env:TEMP 'opentrcc-install.log'
"$(Get-Date -Format 'HH:mm:ss') installing" | Set-Content $log -Encoding UTF8
$zip = 'D:\02_LocalWorkSpace\Project\OpenTRCC\artifacts\OpenTRCC-0.1.0-win-x64.zip'
$dest = 'D:\Program Files\OpenTRCC'

Get-Process OpenTRCC -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2
Expand-Archive -LiteralPath $zip -DestinationPath $dest -Force
Get-ChildItem $dest -Filter *.pdb -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
"$(Get-Date -Format 'HH:mm:ss') files updated" | Add-Content $log -Encoding UTF8
Start-ScheduledTask -TaskName 'OpenTRCC_Startup'
Start-Sleep -Seconds 6
$p = Get-Process OpenTRCC -ErrorAction SilentlyContinue | Select-Object -First 1
"$(Get-Date -Format 'HH:mm:ss') running=$([bool]$p)" | Add-Content $log -Encoding UTF8
