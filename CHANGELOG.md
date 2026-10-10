# Changelog

All notable changes to RoboXopy are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

## [1.2.0] - 2026-10-10

### Added

- **Activity chart** on each profile's History tab. Each run shows what it copied (bars up) and removed (bars down), with a line for the source's total size or file count.
  - Switch between **Size** and **Files**, and choose the last 10, 30 or 100 runs or all runs. **Include previews** adds preview runs as outlined bars.
  - Totals for the range are shown above the chart: copied, removed, runs (and how many failed), average duration, and the source's current size with its change over the range.
  - Failed runs and runs with warnings or unusual changes are marked. A run far larger than the rest is drawn with a break so the others stay readable.
  - Hover over a run (or use the arrow keys) for its details. Click a run to find it in the list below.
- **Breakdown bar** in the run output. It shows how the run's files split into unchanged, copied (or to copy), removed (or to remove) and, when there are any, kept extras, mismatched and failed files, with file counts and sizes.
- **Unusual runs**. RoboXopy compares each run and preview with the profile's recent runs and points out:
  - a source that is empty or much smaller than usual (for example a disconnected drive or a renamed folder);
  - a mirror or purge that removes far more than usual, or a large share of the destination;
  - far more copied than usual.
  These show in the run output, in the history, and in the log.
- **Run** asks for confirmation when the latest preview of the same command warned about something unusual, such as a mirror about to remove far more than usual or a source that shrank. Scheduled runs aren't affected.

### Changed

- **Open log** is now next to the run status in the output panel.
- The green result bar is no longer shown after a successful run when the breakdown already says the same thing.

### Fixed

- Choosing an option in a group of radio buttons (such as the copy mode) could clear the same option in another profile.

## [1.1.3] - 2026-10-10

### Fixed

- The **Import** menu next to **+** in the profiles panel showed boxes instead of text. It now uses the normal font, the same as the other menus.

## [1.1.2] - 2026-10-10

### Added

- Import robocopy batch files. **Import › Robocopy batch file (.bat, .cmd)…** (or choosing a `.bat`/`.cmd` in the profile import dialog) opens a three-step wizard:
  - **Command**: shows the robocopy command found in the file. If the file runs robocopy more than once, you choose which command to import.
  - **Settings**: lists each part of the command and the profile setting it became. Switches with no profile setting are passed to robocopy as they are. Switches RoboXopy handles itself (`/LOG`, `/TEE`, `/JOB`, `/MON`, …) are left out, with an explanation.
  - **Profile**: name the profile, check the source and destination, and see the exact command RoboXopy will run.
- The batch import understands quoted paths, `^` line continuations, `%~dp0`, `cd`/`pushd`, `set` variables, environment variables and relative paths. It also understands `robocopy.exe` with a full path, and `start`/`call`/`cmd /c` prefixes.

## [1.1.1] - 2026-10-10

### Added

- Update check. RoboXopy looks for a newer release on GitHub shortly after it starts and then once a day. It shows **Update available** in the title bar and the notification-area menu, and a notification when the window isn't open. Settings › Updates shows the status and has **Check for updates** and an option to turn automatic checks off.
- **Download and install** downloads the new setup, verifies its SHA-256, and runs it silently for the same install scope. Syncs in progress are stopped only after you confirm. RoboXopy reopens when Setup finishes and shows a notification that it was updated. Copies that weren't installed with Setup open the download page instead.
- Command line: `--check-update` reports whether a newer version is available (exit code 2 if so).
- Installer: `/RELAUNCH=normal|minimized` starts RoboXopy again after a silent install (used by the in-app updater).

## [1.1.0] - 2026-10-10

### Added

- Windows installer (`RoboXopy-<version>-win-x64-setup.exe`, built with Inno Setup 7). It installs for the current user without administrator rights, or for all users, and adds Start menu and optional desktop shortcuts. It supports silent installs.
- Upgrading with the installer closes a running RoboXopy and starts it again afterwards.
- Uninstalling closes RoboXopy and removes its Task Scheduler tasks and "Start with Windows" entry. It then asks whether to delete your profiles, settings, history and logs.
- Command line: `--exit` closes every running RoboXopy started from the same exe and cancels runs in progress.

### Changed

- Signing out or shutting down Windows now cancels running syncs and saves state before RoboXopy exits.

## [1.0.0] - 2026-10-10

First release.

### Added

- Profiles with a source, a destination, a copy mode (Copy, Mirror, Move files, Move files and folders), filters, copy options, retry and logging settings, and extra raw switches.
- Run, Preview (`/L`) and Stop, with live progress, a file list, a run summary and the exact robocopy command line.
- Schedules (interval, daily, weekly, at log-on) run by RoboXopy in the notification area, or by Windows Task Scheduler under `\RoboXopy\`.
- Run history for each profile, with links to the logs. Notifications when a run finishes or has problems.
- Settings: theme (system, light, dark), Start with Windows, close to the notification area, notifications, how many logs to keep, pausing all schedules, and a custom robocopy path.
- Creating, duplicating, reordering, importing and exporting profiles.
- Command line: `--run`, `--dry-run`, `--list`, `--minimized`, `--data-dir`, `--version` and `--help`.
