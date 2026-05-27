param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Install', 'Uninstall')]
    [string]$Action,

    [Parameter(Mandatory = $true)]
    [string]$TaskName,

    [string]$ExecutablePath
)

$ErrorActionPreference = 'Stop'

if ($Action -eq 'Install') {
    if ([string]::IsNullOrWhiteSpace($ExecutablePath)) {
        throw 'ExecutablePath is required when installing the scheduled task.'
    }

    if (-not (Test-Path -LiteralPath $ExecutablePath)) {
        throw "Executable not found: $ExecutablePath"
    }

    $taskAction = New-ScheduledTaskAction -Execute $ExecutablePath
    $taskTrigger = New-ScheduledTaskTrigger -Daily -At 12:10AM
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $taskSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable

    $registerTaskParams = @{
        TaskName = $TaskName
        Action = $taskAction
        Trigger = $taskTrigger
        Principal = $taskPrincipal
        Settings = $taskSettings
        Force = $true
    }

    Register-ScheduledTask @registerTaskParams | Out-Null

    exit 0
}

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($null -ne $existingTask) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}
