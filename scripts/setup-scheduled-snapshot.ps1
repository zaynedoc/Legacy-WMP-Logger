param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDirectory,
    [string]$DataDirectory = (Join-Path $PSScriptRoot '..\data'),
    [string]$TaskName = 'WMPL Wrap Daily Snapshot'
)

$publishPath = (Resolve-Path -LiteralPath $PublishDirectory).Path
$dataPath = [System.IO.Path]::GetFullPath($DataDirectory)
$dllPath = Join-Path $publishPath 'WmplWrap.dll'

if (-not (Test-Path -LiteralPath $dllPath)) {
    throw "WmplWrap.dll was not found in '$publishPath'. Run dotnet publish first."
}

$action = New-ScheduledTaskAction -Execute 'dotnet.exe' -Argument ('"{0}" snapshot --data "{1}"' -f $dllPath, $dataPath)
$trigger = New-ScheduledTaskTrigger -Daily -At 12:05AM
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Settings $settings -Description 'Read-only snapshot of Windows Media Player cumulative play counts for WMPL Wrap.' -Force
Write-Host "Created '$TaskName'. It runs at 12:05 AM in this computer's local time and catches up if the PC was off."
