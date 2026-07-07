<#
.SYNOPSIS
    Publishes the single-file exe, stages the install payload, and (if the tool
    is available) produces the .intunewin package for Intune.

.PARAMETER IntuneWinAppUtil
    Path to IntuneWinAppUtil.exe (the Microsoft Win32 Content Prep Tool).
    Download: https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool
    If omitted, the script looks for it on PATH; if still not found it stages the
    payload and prints the command to run manually.

.EXAMPLE
    .\build-package.ps1 -IntuneWinAppUtil C:\Tools\IntuneWinAppUtil.exe
#>

param(
    [string]$IntuneWinAppUtil
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path $PSScriptRoot -Parent
$payload  = Join-Path $PSScriptRoot 'payload'
$output   = Join-Path $PSScriptRoot 'output'

# 1. Publish the self-contained single-file exe.
Write-Host "Publishing single-file exe..."
& dotnet publish (Join-Path $repoRoot 'TrayInfo.csproj') -p:PublishProfile=win-x64 -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
$publishedExe = Join-Path $repoRoot 'bin\Publish\TrayInfo.exe'
if (-not (Test-Path $publishedExe)) { throw "Published exe not found at $publishedExe" }

# 2. Stage the payload folder: exe + install/uninstall scripts.
if (Test-Path $payload) { Remove-Item $payload -Recurse -Force }
New-Item -ItemType Directory -Path $payload | Out-Null
Copy-Item $publishedExe                              $payload
Copy-Item (Join-Path $PSScriptRoot 'install.ps1')    $payload
Copy-Item (Join-Path $PSScriptRoot 'uninstall.ps1')  $payload
Write-Host "Payload staged at $payload"

# 3. Locate IntuneWinAppUtil.exe.
if (-not $IntuneWinAppUtil)
{
    $onPath = Get-Command IntuneWinAppUtil.exe -ErrorAction SilentlyContinue
    if ($onPath) { $IntuneWinAppUtil = $onPath.Source }
}

if (-not $IntuneWinAppUtil -or -not (Test-Path $IntuneWinAppUtil))
{
    Write-Warning @"
IntuneWinAppUtil.exe not found. Download it from
  https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool
then either add it to PATH or re-run with -IntuneWinAppUtil <path>.

To package manually:
  IntuneWinAppUtil.exe -c "$payload" -s install.ps1 -o "$output" -q
"@
    exit 0
}

# 4. Build the .intunewin package.
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $IntuneWinAppUtil -c $payload -s 'install.ps1' -o $output -q
if ($LASTEXITCODE -ne 0) { throw "IntuneWinAppUtil failed." }

Write-Host "Package created: $(Join-Path $output 'install.intunewin')"
exit 0
