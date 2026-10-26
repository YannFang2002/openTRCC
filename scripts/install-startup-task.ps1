<#
Switches auto-start from TRCC to OpenTRCC:
  - removes TRCC's elevated logon task (TRCCAppStartup)
  - stops TRCC / HWINFO so the screen is free
  - registers OpenTRCC_Startup with RunLevel=Highest
  - starts OpenTRCC through that task (elevated, no future UAC)
Must run elevated. Logs to %TEMP%\opentrcc-startup-task.log.
#>
$ErrorActionPreference = 'Continue'
$log = Join-Path $env:TEMP 'opentrcc-startup-task.log'
function Write-Log([string]$message) {
    "$(Get-Date -Format 'HH:mm:ss') $message" | Tee-Object -FilePath $log -Append
}

$principal = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
Write-Log "elevated=$($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator))"

# 1. Remove TRCC auto-start task.
$trcc = Get-ScheduledTask -TaskName 'TRCCAppStartup' -ErrorAction SilentlyContinue
if ($trcc) {
    try {
        Unregister-ScheduledTask -TaskName 'TRCCAppStartup' -Confirm:$false
        Write-Log 'removed TRCCAppStartup'
    }
    catch {
        Disable-ScheduledTask -TaskName 'TRCCAppStartup' -ErrorAction SilentlyContinue | Out-Null
        Write-Log "could not remove TRCCAppStartup, disabled instead: $($_.Exception.Message)"
    }
}
else {
    Write-Log 'TRCCAppStartup not present'
}

# 2. Free the LCD: stop TRCC and its HWiNFO helper.
foreach ($name in 'TRCC', 'HWINFO', 'OpenTRCC') {
    Get-Process $name -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            Stop-Process -Id $_.Id -Force -ErrorAction Stop
            Write-Log "stopped $name ($($_.Id))"
        }
        catch {
            Write-Log "stop $name ($($_.Id)) failed: $($_.Exception.Message)"
        }
    }
}
Start-Sleep -Seconds 2

# 3. Register the elevated logon task for OpenTRCC.
$exe = 'D:\Program Files\OpenTRCC\OpenTRCC.exe'
if (-not (Test-Path $exe)) {
    Write-Log "missing exe: $exe"
    exit 2
}

$user = "$env:USERDOMAIN\$env:USERNAME"
try {
    $action = New-ScheduledTaskAction -Execute $exe
    $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit ([TimeSpan]::Zero)
    Register-ScheduledTask -TaskName 'OpenTRCC_Startup' -Description 'OpenTRCC 以管理员身份随登录启动' `
        -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $settings -Force | Out-Null
    Write-Log "registered OpenTRCC_Startup (RunLevel=Highest, user=$user)"
}
catch {
    Write-Log "register failed: $($_.Exception.Message)"
    exit 3
}

# 4. Drop the old HKCU Run entry so only one instance can start.
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$runValue = (Get-ItemProperty -Path $runKey -Name 'OpenTRCC' -ErrorAction SilentlyContinue).OpenTRCC
if ($runValue) {
    Remove-ItemProperty -Path $runKey -Name 'OpenTRCC'
    Write-Log "removed HKCU Run OpenTRCC ($runValue)"
}

# 5. Start it now through the task so it runs elevated.
Start-ScheduledTask -TaskName 'OpenTRCC_Startup'
Start-Sleep -Seconds 8

$task = Get-ScheduledTask -TaskName 'OpenTRCC_Startup'
$proc = Get-Process OpenTRCC -ErrorAction SilentlyContinue | Select-Object -First 1
Write-Log "task state=$($task.State) runLevel=$($task.Principal.RunLevel) user=$($task.Principal.UserId)"
if ($proc) {
    $handle = $proc.Handle  # forces refresh
    $path = $proc.Path
    Write-Log "OpenTRCC running: pid=$($proc.Id) path=$path"
}
else {
    Write-Log 'OpenTRCC did not start'
}
Write-Log 'done'
