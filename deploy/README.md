# Intune deployment

Packages TrayInfo as a **Win32 app** (`.intunewin`) that installs
per-machine to `C:\Program Files\TrayInfo`, autostarts for all users, and
uninstalls cleanly.

## 1. Build the package

From this `deploy` folder:

```powershell
.\build-package.ps1 -IntuneWinAppUtil C:\Tools\IntuneWinAppUtil.exe
```

This publishes the self-contained single-file exe (no .NET runtime needed on
targets), stages `payload\` (exe + `install.ps1` + `uninstall.ps1`), and produces
`output\install.intunewin`.

> `IntuneWinAppUtil.exe` is Microsoft's [Win32 Content Prep Tool](https://github.com/microsoft/Microsoft-Win32-Content-Prep-Tool).
> If it's on PATH you can omit `-IntuneWinAppUtil`. Without it, the script still
> stages `payload\` and prints the manual packaging command.

## 2. Create the app in Intune

**Apps → Windows → Add → App type: Windows app (Win32)** → upload `install.intunewin`.

| Setting | Value |
|---|---|
| **Install command** | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File install.ps1` |
| **Uninstall command** | `powershell.exe -NoProfile -ExecutionPolicy Bypass -File uninstall.ps1` |
| **Install behavior** | **System** |
| **Device architecture** | 64-bit |
| **Minimum OS** | Windows 10 20H2 (or your baseline) |

### Detection rule

Use a **file** rule (matches the version stamped into the exe — bump
`<Version>` in the `.csproj` for upgrades):

| Field | Value |
|---|---|
| Rule type | File |
| Path | `C:\Program Files\TrayInfo` |
| File | `TrayInfo.exe` |
| Detection method | String (version) **≥** `1.0.0.0` |

(Or simply "File or folder exists" if you don't need version-aware upgrades.)

## What the scripts do

**install.ps1**
- Stops any running instance, copies the exe to `C:\Program Files\TrayInfo`.
- Adds `HKLM\…\Run` so it launches for **every** user at logon.
- Registers an Add/Remove Programs entry (name, version, publisher, uninstall).
- Launches it immediately in the logged-on user's session via a short-lived
  scheduled task (Intune runs as SYSTEM in session 0, which can't show UI directly).

**uninstall.ps1**
- Stops the process, removes the `Run` key, the ARP entry, and the install folder.

## Notes

- The app's own **"Start with Windows"** tray toggle writes a *per-user* `HKCU\…\Run`
  entry. With machine-wide autostart from the installer it's redundant (the
  single-instance guard prevents a second copy), and a machine-context uninstall
  can't remove other users' HKCU entries — harmless, but worth knowing.
- Bump `<Version>` in `TrayInfo.csproj` **and** `$Version` in `install.ps1`
  together for each release, and Intune will treat it as an upgrade.
