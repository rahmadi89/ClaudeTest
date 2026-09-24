<#
.SYNOPSIS
  Installs or upgrades the ATM Monitor Agent as a Windows Service on a terminal.

.DESCRIPTION
  - Copies the self-contained agent to the install directory
  - Writes appsettings.Production.json with the server URL, terminal id and one-time enrollment token
  - Locks down the data directory (SYSTEM + Administrators only) because it holds the agent key
  - Registers the service with automatic (delayed) start and SCM recovery actions
    (restart on failure, including non-zero exit – used by the RestartAgent command)

.EXAMPLE
  .\install-agent.ps1 -ServerUrl https://atm-monitor.bank.local -TerminalId ATM-NYC-0001 -EnrollmentToken <token>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ServerUrl,
    [Parameter(Mandatory)] [ValidatePattern('^[A-Za-z0-9_-]{1,32}$')] [string] $TerminalId,
    [string] $EnrollmentToken,
    [string] $SourceDir = $PSScriptRoot,
    [string] $InstallDir = "$env:ProgramFiles\AtmMonitor\Agent",
    [string] $DataDir = "$env:ProgramData\AtmMonitor\Agent",
    [string] $ServiceName = "AtmMonitorAgent",
    [string] $TransactionHost,
    [int] $TransactionHostPort = 0
)

$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell session."
}
if ($ServerUrl -notmatch '^https://') { throw "ServerUrl must use https." }

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing service for upgrade..."
    Stop-Service -Name $ServiceName -Force
    $existing.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}

New-Item -ItemType Directory -Force -Path $InstallDir, $DataDir | Out-Null
Copy-Item -Path (Join-Path $SourceDir '*') -Destination $InstallDir -Recurse -Force -Exclude 'install-agent.ps1', 'uninstall-agent.ps1', 'appsettings.Production.json'

# Data dir holds the DPAPI-protected agent key and the offline buffer: SYSTEM and Administrators only.
$acl = New-Object System.Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true, $false)
foreach ($id in 'NT AUTHORITY\SYSTEM', 'BUILTIN\Administrators') {
    $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($id, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
}
Set-Acl -Path $DataDir -AclObject $acl

$config = @{
    Agent = @{
        ServerUrl     = $ServerUrl
        TerminalId    = $TerminalId
        DataDirectory = $DataDir
        HostCheck     = @{ Host = $TransactionHost; Port = $TransactionHostPort }
    }
}
if ($EnrollmentToken) { $config.Agent.EnrollmentToken = $EnrollmentToken }
$configPath = Join-Path $InstallDir 'appsettings.Production.json'
if (-not (Test-Path $configPath) -or $EnrollmentToken) {
    $config | ConvertTo-Json -Depth 5 | Set-Content -Path $configPath -Encoding UTF8
}

$exe = Join-Path $InstallDir 'AtmMonitor.Agent.exe'
if (-not $existing) {
    New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName 'ATM Monitor Agent' `
        -Description 'Reports ATM device, cash, network and system status to the ATM Fleet Monitor and executes approved remote commands.' `
        -StartupType Automatic | Out-Null
}
sc.exe config $ServiceName start= delayed-auto | Out-Null
# Restart after 10s, 30s, then every 60s; reset the failure counter daily.
sc.exe failure $ServiceName reset= 86400 actions= restart/10000/restart/30000/restart/60000 | Out-Null
# Treat a stop with a non-zero exit code as a failure so recovery actions apply (RestartAgent command).
sc.exe failureflag $ServiceName 1 | Out-Null
[Environment]::SetEnvironmentVariable('DOTNET_ENVIRONMENT', 'Production', 'Machine')

Start-Service -Name $ServiceName
Write-Host "Agent installed and started. Logs: $DataDir\logs"
if ($EnrollmentToken) {
    Write-Host "After the terminal appears in the console, remove EnrollmentToken from $configPath."
}
