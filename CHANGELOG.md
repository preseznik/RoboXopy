# Changelog

All notable changes to RoboXopy are listed here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow [Semantic Versioning](https://semver.org/).

## [Unreleased]

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
