[CmdletBinding()]
param(
    [string] $InstallDir = "$env:ProgramFiles\AtmMonitor\Agent",
    [string] $DataDir = "$env:ProgramData\AtmMonitor\Agent",
    [string] $ServiceName = "AtmMonitorAgent",
    [switch] $KeepData
)
$ErrorActionPreference = 'Stop'
if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force
    sc.exe delete $ServiceName | Out-Null
}
Remove-Item -Recurse -Force $InstallDir -ErrorAction SilentlyContinue
if (-not $KeepData) { Remove-Item -Recurse -Force $DataDir -ErrorAction SilentlyContinue }
Write-Host "Agent removed. Revoke the terminal's agent key in the console if the device is being decommissioned."
