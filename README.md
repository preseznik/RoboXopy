# RoboXopy

A Windows desktop app for robocopy. Each sync is a **profile** with a source, a destination, a copy mode, filters, retry and logging options, and an optional schedule. Profiles are listed on the left. You can run them on demand, preview them (`/L`), or run them on a schedule.

Built with WPF on .NET 10 and the Windows 11 (Fluent) theme. Light, dark and system themes are supported.

## Features

- **Copy modes**: Copy, Mirror (`/MIR`), Move files (`/MOV`) and Move files and folders (`/MOVE`). Destructive modes show warnings.
- **Filters**: include and exclude file patterns, excluded folders, file age and size limits, the archive attribute, and change-type filters (older, newer, changed, extra, lonely, same, tweaked).
- **Copy options**: which file and folder data to copy (`/COPY`, `/DCOPY`), multithreading (`/MT`), restartable and backup modes, unbuffered I/O, compression, junction and symlink handling, FAT time and DST tolerance, and inter-packet gap.
- **Retry and logging**: `/R`, `/W`, log verbosity, and extra raw switches. Every run writes a full robocopy log.
- **Live output**: progress, file list, a summary that matches robocopy's totals, and the exact command line, which you can copy.
- **Schedules**: interval, daily, weekly or at log-on. Two runners are available:
  - **RoboXopy (in-app)**: runs while RoboXopy is open or in the notification area. "Start with Windows" keeps it running in the background.
  - **Windows Task Scheduler**: registers a task under `\RoboXopy\` for your account, so it runs even when RoboXopy is closed.
- **History** for each profile, with the outcome, trigger, totals, duration and a link to the log.
- **Notifications** when a sync finishes, or only when it has problems.
- **Updates**: RoboXopy checks GitHub for a new release once a day (Settings › Updates; this can be turned off). When one is available it can download, verify and install it, then reopen.

## Requirements

- Windows 10 or 11 (`robocopy.exe` is built in).
- Release builds are self-contained, so no .NET install is needed to run them.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download) to build, and [Inno Setup 7](https://jrsoftware.org/isinfo.php) to build the installer.

## Install

Run `RoboXopy-<version>-win-x64-setup.exe`. It asks whether to install for you only (in `%LOCALAPPDATA%\Programs\RoboXopy`, no administrator rights needed) or for all users (in `Program Files`). It adds a Start menu shortcut and, if you choose, a desktop shortcut.

To upgrade, run the setup for the newer version. If RoboXopy is open, Setup closes it and starts it again afterwards. Your profiles and settings are kept.

RoboXopy can also update itself. It checks the [GitHub releases](https://github.com/preseznik/RoboXopy/releases) shortly after it starts and then once a day. When a newer version is out, an **Update available** button appears in the title bar and in the notification-area menu. **Download and install** downloads the setup, checks its SHA-256, asks before stopping any sync in progress, and runs Setup silently for the same scope (current user or all users; the latter shows a Windows prompt). RoboXopy reopens when Setup finishes. Nothing is downloaded until you choose to install. Copies that weren't installed with Setup (the zip) link to the download page instead.

Uninstalling (Settings › Apps, or the Start menu) closes RoboXopy and removes its Task Scheduler tasks and its "Start with Windows" entry. It then asks whether to delete your profiles, settings, history and logs as well; the default answer is No.

Unattended install and uninstall:

```powershell
.\RoboXopy-1.1.0-win-x64-setup.exe /VERYSILENT /SUPPRESSMSGBOXES /CURRENTUSER   # or /ALLUSERS (elevated)
.\RoboXopy-1.1.0-win-x64-setup.exe /VERYSILENT /DIR="D:\Apps\RoboXopy" /TASKS=desktopicon
& "$env:LOCALAPPDATA\Programs\RoboXopy\unins000.exe" /VERYSILENT /SUPPRESSMSGBOXES
```

A silent uninstall always keeps your data in `%APPDATA%\RoboXopy`. Add `/LOG=<file>` to either command for a detailed log.

The zip contains the same `RoboXopy.exe` if you prefer not to install it.

## Build and run

```powershell
dotnet build RoboXopy.slnx
dotnet test tests\RoboXopy.Core.Tests
dotnet run --project src\RoboXopy
```

## Releases and versioning

RoboXopy uses [Semantic Versioning](https://semver.org): `MAJOR.MINOR.PATCH[-prerelease]`.

| Part | Bump when |
|---|---|
| MAJOR | Incompatible changes, such as the profile or settings format or the command line |
| MINOR | New features that stay backwards compatible |
| PATCH | Bug fixes only |

The version is kept in `Version.props`. Every published build also gets a build number that increases by one and never resets:

| Where | Example |
|---|---|
| Settings page and `RoboXopy --version` | `1.2.0 (build 14)`, `RoboXopy 1.2.0+build.14.20261010` |
| File version (Explorer › Properties › Details) | `1.2.0.14` |
| Product version | `1.2.0+build.14.20261010` (adds `.sha.<commit>` inside a git repository) |

Local `dotnet build` output is tagged `+dev` and shows as a "dev build".

Build a release with `tools\publish.ps1`:

```powershell
.\tools\publish.ps1                          # current version, next build number
.\tools\publish.ps1 -Bump patch              # 1.0.0 -> 1.0.1
.\tools\publish.ps1 -Bump minor              # 1.0.1 -> 1.1.0
.\tools\publish.ps1 -Bump major -Prerelease beta.1   # 1.1.0 -> 2.0.0-beta.1
.\tools\publish.ps1 -FrameworkDependent      # small exe that needs the .NET 10 Desktop Runtime
.\tools\publish.ps1 -SkipInstaller           # zip only
```

The script runs the tests, publishes a single self-contained `RoboXopy.exe` and builds the installer with Inno Setup 7 (`installer\RoboXopy.iss`; found automatically, or pass `-InnoSetupPath`). Only after all of that succeeds does it update `Version.props`. Output:

```
artifacts\RoboXopy-<version>-win-x64\                RoboXopy.exe, README.md, CHANGELOG.md
artifacts\RoboXopy-<version>-win-x64.zip
artifacts\RoboXopy-<version>-win-x64.zip.sha256
artifacts\RoboXopy-<version>-win-x64-setup.exe
artifacts\RoboXopy-<version>-win-x64-setup.exe.sha256
```

Add a `## [<version>]` section to `CHANGELOG.md` before you publish. The script warns if it's missing.

Framework-dependent builds are only packaged as a zip. To upgrade a copy that isn't installed, replace `RoboXopy.exe` or run the new version from another folder. At startup RoboXopy points "Start with Windows" and any registered Task Scheduler tasks at the exe that is running.

## Command line

```
RoboXopy                          Open the window
RoboXopy --minimized              Start in the notification area
RoboXopy --run <profile> [--dry-run]
                                  Run a profile without opening the window
                                  (<profile> = name, id or short id)
RoboXopy --list                   List all profiles
RoboXopy --exit                   Close every running RoboXopy started from this exe
                                  (the window app and runs in progress, which are cancelled)
RoboXopy --version                Show the version
RoboXopy --check-update           Check GitHub for a newer release
                                  (exit code 0 = up to date, 2 = update available, 16 = check failed)
RoboXopy --help                   Show help
          --data-dir <folder>     Use another data folder (any command)
```

`--run` returns robocopy's exit code: 0–7 means success and 8 or higher means failure. Exit code 16 means RoboXopy couldn't start the run, for example because the profile is unknown. From PowerShell, pipe to `Out-Host` to wait for the run to finish:

```powershell
RoboXopy --run "My backup" | Out-Host
```

## Data

Everything is stored per user in `%APPDATA%\RoboXopy`:

| Path | Contents |
|------|----------|
| `settings.json` | App settings (theme, startup, notifications, …) |
| `profiles\<id>.json` | One file per profile |
| `history\<id>.jsonl` | Run history, one JSON line per run |
| `logs\<id>\` | Full robocopy logs |
| `locks\` | Prevents the same profile running twice at once |

Use `--data-dir <folder>` or the `ROBOXOPY_DATA_DIR` environment variable to point RoboXopy at another folder, such as a portable or test setup. Scheduled tasks and "Start with Windows" keep the custom folder.

## Project layout

| Folder | Purpose |
|--------|---------|
| `src\RoboXopy.Core` | UI-free logic: models, robocopy command building and output parsing, runner, storage, schedule calculation, Task Scheduler integration |
| `src\RoboXopy` | WPF app (MVVM with CommunityToolkit.Mvvm): views, view-models, tray, in-app scheduler, headless CLI |
| `tests\RoboXopy.Core.Tests` | xUnit tests for the Core library |
| `installer` | Inno Setup script and wizard images |
| `tools\publish.ps1` | Builds a versioned release: zip and installer |
| `tools\make-icon.ps1` | Regenerates the app icon |
| `tools\make-installer-images.ps1` | Regenerates the installer's wizard images from the logo |
