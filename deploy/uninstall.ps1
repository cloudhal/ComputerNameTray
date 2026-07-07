<#
.SYNOPSIS
    Removes TrayInfo.

    Intune uninstall command:
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File uninstall.ps1
#>

# Best-effort: keep going even if individual steps fail.
$ErrorActionPreference = 'SilentlyContinue'

$AppName    = 'TrayInfo'
$InstallDir = Join-Path $env:ProgramFiles $AppName

Write-Host "Uninstalling $AppName"

# 1. Stop running instances (in every session).
Get-Process -Name $AppName -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

# 2. Remove machine-wide autostart and the Add/Remove Programs entry.
Remove-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run' -Name $AppName
Remove-Item -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$AppName" -Recurse -Force

# 3. Remove installed files. A running .ps1 is not locked, so removing the
#    folder that contains this very script succeeds; retry once just in case.
foreach ($attempt in 1..2)
{
    Remove-Item -Path $InstallDir -Recurse -Force
    if (-not (Test-Path $InstallDir)) { break }
    Start-Sleep -Seconds 1
}

# Note: the in-app "Start with Windows" toggle writes a per-user HKCU Run entry
# that this machine-context uninstall cannot see. It's harmless (points at a
# now-missing exe) and each user can clear it, or handle it separately if needed.

Write-Host "Uninstall complete."
exit 0
