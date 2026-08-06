# Changelog

All notable changes to HideEverything are documented here. This project adheres to
[Semantic Versioning](https://semver.org/).

## [1.1.2] - 2026-06-15

### Added
- 简体中文作为默认界面语言，并可在设置页切换 English；语言选择保存到配置文件。
- 发布包附带 `LICENSE` 和 `THIRD-PARTY-NOTICES.md`，同时提供 `win-x64` 自包含单文件输出。

### Fixed
- **The "Hide a window…" picker now shows a window's existing session shortcut.**
  Previously a window that already had a temporary shortcut looked unassigned, so
  the picker always offered to assign a fresh one. Each window with a shortcut now
  shows it as a badge (e.g. `Ctrl+Alt+C`), and **Assign shortcut…** asks for
  confirmation before replacing an existing one. A window only ever keeps one
  temporary shortcut.

## [1.1.1] - 2026-06-13

### Fixed
- **"Mute audio while hidden" now works for multi-process apps** (Chrome, Spotify,
  Discord, browsers). They play audio from a separate child process, so matching the
  window's process id alone muted nothing. HideEverything now mutes every audio session in
  the window's process tree (the process and its descendants).

## [1.1.0] - 2026-06-13

### Added
- **Mute audio while hidden** (global toggle in Settings, default on) — when an app
  or window is hidden, its audio session is muted, and unmuted again when restored.
  Only sessions HideEverything muted are unmuted, so manual mutes are left alone.

### Fixed
- **Launching HideEverything now opens the Settings window** instead of starting silently
  into the tray with no visible window. A login auto-start (the "Run at Windows
  startup" entry) still starts quietly — it now passes a `--startup` flag.
- **Double-clicking the exe while HideEverything is already running** now brings the
  running copy's window to the front instead of the second copy exiting silently.
- The **tray icon opens Settings on a single or double click** (the previous
  double-click handler could be unreliable), and the window reliably comes to the
  foreground.

## [1.0.1] - 2026-06-13

### Added
- **Hide HideEverything itself** — a tray command and a configurable global shortcut
  (default `Ctrl+Alt+Shift+H`) hide HideEverything's own tray icon, including from the
  notification-area overflow. HideEverything keeps running; the shortcut brings it back.
  Refuses to hide the icon unless a working show/hide shortcut is registered.
- **Hide a specific window** — a "Hide a window…" picker (tray + Settings) lists
  every open window so you can hide individual ones instead of the whole process,
  e.g. just one of several Chrome profile windows.
- **Assign a temporary shortcut to specific windows** from the picker: tick one or
  more windows, capture a combo, and that shortcut toggles exactly those windows
  without opening HideEverything. Session-only (handles don't survive a window close).
- **Show last hidden window** shortcut (default `Ctrl+Alt+Shift+S`) and **Restore
  all** bring individually-hidden windows back.
- **Create Desktop / Start Menu shortcuts** from the tray ("Create shortcut") and
  from Settings — handy for the portable single-file build.

### Removed
- The per-app **floating icon** feature. (Old `showFloatingIcon`/`iconX`/`iconY`
  config fields are ignored on load and dropped on the next save.)

## [1.0.0] - 2026-06-12

First release.

### Added
- Tray utility (WPF, .NET 10) that hides/shows apps from the screen, the
  **taskbar**, and **Alt+Tab** on demand.
- Per-app **global keyboard shortcuts**. The same shortcut can be assigned to
  several apps so they hide/show together (group toggle).
- Optional per-app **floating icon button** to toggle a single app.
- Settings window with a running-app picker (icons), Browse-for-`.exe`, shortcut
  capture, and remove. Edits persist immediately.
- Tray menu: **Settings**, **Restore all hidden**, **Run at Windows startup**
  (HKCU Run key), **Exit**.
- **Panic hotkey** (default `Ctrl+Alt+`` `) restores every hidden window —
  **configurable** in Settings.
- **First-run onboarding** panel explaining the basics.
- Tray **Check for updates** link to the GitHub releases page.
- Config stored at `%AppData%\HideEverything\config.json`.
- Launch hardening: single-instance mutex, global crash logging to
  `%AppData%\HideEverything\logs`, and always-restore-on-exit so no window is left
  invisible.
- **Distribution:** GitHub Actions workflow that builds the single-file exe and
  attaches it to a Release on tag push; Inno Setup installer script with
  startup-entry cleanup on uninstall.
