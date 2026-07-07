# TrayInfo

A lightweight, tray-only Windows app that shows the computer name and key system
details in the system tray — a lighter-weight, non-intrusive take on Sysinternals
BGInfo. No window, no desktop overlay: just an icon you can hover or right-click.

## Features

- **Tray icon** shows the most distinctive part of the hostname (the last
  `-`/`_`/`.` segment, e.g. `167` for `HPT-LAP-167`), auto-scaled to fill the icon.
- **Hover tooltip** shows the computer name and current IP.
- **Right-click menu** with click-to-copy rows — each click copies just that value:
  - IP address, logged-in user & domain, OS version/build, model, BIOS serial,
    free disk space, uptime.
  - **Copy all details** — copies every field as a labeled block, handy for pasting
    into a helpdesk ticket.
  - **Start with Windows** — per-user autostart toggle (`HKCU\…\Run`).
- **Stays visible** on Windows 11 — promotes itself out of the tray overflow flyout
  (sets `IsPromoted` under `HKCU\Control Panel\NotifyIconSettings`).
- **Live refresh** — updates the IP on network changes and re-renders the icon on
  DPI/display changes (e.g. docking to another monitor).
- **Single instance** — a named mutex prevents duplicate tray icons.

All system lookups degrade gracefully (`unknown` / `unavailable`) rather than
crashing if a source is missing.

## Requirements

- .NET 10 SDK (targets `net10.0-windows`), Windows 10/11.

## Build & run

```powershell
dotnet build
dotnet run --project TrayInfo.csproj
```

Or press F5 in VS Code (see [.vscode/launch.json](.vscode/launch.json)). To stop it,
use the tray menu's **Exit**.

## Publish a standalone exe

Produces one self-contained `.exe` with the .NET runtime baked in (runs on any
Windows 10/11 x64 machine with no runtime install):

```powershell
dotnet publish -p:PublishProfile=win-x64
```

Output: `bin\Publish\TrayInfo.exe` (~49 MB). Profile:
[Properties/PublishProfiles/win-x64.pubxml](Properties/PublishProfiles/win-x64.pubxml).

## Deployment (Intune)

The [deploy/](deploy/) folder packages TrayInfo as an Intune Win32 app that installs
per-machine to `C:\Program Files\TrayInfo`, autostarts for all users, and uninstalls
cleanly. See [deploy/README.md](deploy/README.md) for the build command and the exact
Intune app settings (install/uninstall commands, detection rule).

## Releasing / version bumps

Bump the version in **two** places together for each release so the Add/Remove
Programs entry and Intune's version-based detection/upgrade logic stay in sync:

1. `<Version>` in [TrayInfo.csproj](TrayInfo.csproj) — stamps the exe's file version.
2. `$Version` in [deploy/install.ps1](deploy/install.ps1) — the ARP `DisplayVersion`.

With both bumped, Intune treats a new package as an upgrade of the existing app.

## Project layout

| Path | Purpose |
|---|---|
| [Program.cs](Program.cs) | Entry point; single-instance mutex. |
| [TrayApplicationContext.cs](TrayApplicationContext.cs) | All tray logic — icon rendering, info menu, autostart, self-promotion. |
| [TrayInfo.csproj](TrayInfo.csproj) | Project file and assembly/version metadata. |
| [Properties/PublishProfiles/win-x64.pubxml](Properties/PublishProfiles/win-x64.pubxml) | Single-file self-contained publish profile. |
| [deploy/](deploy/) | Intune packaging scripts and instructions. |

## Notes

- The installer's machine-wide autostart (`HKLM\…\Run`) and the in-app **Start with
  Windows** toggle (`HKCU\…\Run`) are independent. When deployed via Intune the
  per-user toggle is redundant (the single-instance guard prevents a second copy),
  and a machine-context uninstall can't clear other users' `HKCU` entries — harmless,
  but worth knowing.
- **Start with Windows** records the exe's *current* path, so enable it only after the
  exe is in its permanent location (the Intune install handles this by placing it in
  Program Files first).
