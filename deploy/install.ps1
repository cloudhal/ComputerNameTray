<#
.SYNOPSIS
    Installs TrayInfo to Program Files for all users.
    Intended to run as SYSTEM via Intune (Win32 app), but also works when run
    manually from an elevated prompt.

    Intune install command:
        powershell.exe -NoProfile -ExecutionPolicy Bypass -File install.ps1
#>

$ErrorActionPreference = 'Stop'

$AppName     = 'TrayInfo'
$DisplayName = 'TrayInfo'
$Publisher   = 'Hollyport Capital'
$Version     = '1.0.0'
$ExeName     = 'TrayInfo.exe'

$InstallDir = Join-Path $env:ProgramFiles $AppName
$SourceExe  = Join-Path $PSScriptRoot $ExeName
$TargetExe  = Join-Path $InstallDir $ExeName

Write-Host "Installing $DisplayName $Version to $InstallDir"

# 1. Stop any running instances so the exe isn't locked.
Get-Process -Name $AppName -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

# 2. Copy the payload into Program Files.
New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item -Path $SourceExe -Destination $TargetExe -Force
Copy-Item -Path (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination (Join-Path $InstallDir 'uninstall.ps1') -Force

# 3. Machine-wide autostart: launch for every user at logon.
$runKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
New-ItemProperty -Path $runKey -Name $AppName -Value "`"$TargetExe`"" -PropertyType String -Force | Out-Null

# 4. Add/Remove Programs entry (also usable as an Intune detection point).
$arp = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\$AppName"
New-Item -Path $arp -Force | Out-Null
$uninstallCmd = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`""
Set-ItemProperty -Path $arp -Name 'DisplayName'     -Value $DisplayName
Set-ItemProperty -Path $arp -Name 'DisplayVersion'  -Value $Version
Set-ItemProperty -Path $arp -Name 'Publisher'       -Value $Publisher
Set-ItemProperty -Path $arp -Name 'InstallLocation' -Value $InstallDir
Set-ItemProperty -Path $arp -Name 'DisplayIcon'     -Value $TargetExe
Set-ItemProperty -Path $arp -Name 'UninstallString' -Value $uninstallCmd
Set-ItemProperty -Path $arp -Name 'NoModify' -Value 1 -Type DWord
Set-ItemProperty -Path $arp -Name 'NoRepair' -Value 1 -Type DWord

# 5. Launch immediately in the interactive user's session (if someone is logged
#    on). Intune runs this as SYSTEM in session 0, so a plain Start-Process would
#    be invisible; a short-lived scheduled task runs it in the user's session.
try
{
    $consoleUser = (Get-CimInstance Win32_ComputerSystem).UserName  # DOMAIN\user, or null if no one is logged on
    $isSystem = [Security.Principal.WindowsIdentity]::GetCurrent().IsSystem

    if ($consoleUser)
    {
        if ($isSystem)
        {
            $taskName  = "$AppName-FirstRun"
            $action    = New-ScheduledTaskAction -Execute $TargetExe
            $principal = New-ScheduledTaskPrincipal -UserId $consoleUser -LogonType Interactive
            Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Force | Out-Null
            Start-ScheduledTask -TaskName $taskName
            Start-Sleep -Seconds 3
            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
        }
        else
        {
            Start-Process -FilePath $TargetExe
        }
        Write-Host "Launched for $consoleUser."
    }
    else
    {
        Write-Host "No interactive user; it will start at next logon."
    }
}
catch
{
    Write-Warning "Could not launch immediately ($_). It will start at next logon."
}

Write-Host "Install complete."
exit 0
