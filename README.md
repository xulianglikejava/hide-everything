# HideEverything

HideEverything 是一个简体中文优先的 Windows 托盘老板键工具，也支持在设置页切换为 English。它隐藏受控窗口的桌面入口，但保持目标程序继续运行。

[![最新发行版](https://img.shields.io/github/v/release/xulianglikejava/hide-everything?display_name=tag&sort=semver)](https://github.com/xulianglikejava/hide-everything/releases)
[![构建状态](https://img.shields.io/github/actions/workflow/status/xulianglikejava/hide-everything/release.yml?label=build)](https://github.com/xulianglikejava/hide-everything/actions)
[![许可证](https://img.shields.io/github/license/xulianglikejava/hide-everything)](LICENSE)

项目主页：[GitHub](https://github.com/xulianglikejava/hide-everything) · [发行版](https://github.com/xulianglikejava/hide-everything/releases) · [问题反馈](https://github.com/xulianglikejava/hide-everything/issues)

## 中文介绍

HideEverything 是一款面向 Windows 的托盘老板键工具。它可以通过全局快捷键，
让指定程序或指定窗口从屏幕、任务栏和 `Alt+Tab` 中暂时消失，同时保持程序继续运行；
再次按下快捷键即可恢复窗口。

### 主要功能

- 为程序或单个窗口设置全局快捷键，支持多个目标共用同一个快捷键。
- 保存程序路径、进程名、窗口类名和可选标题文本，程序重启后仍能自动匹配。
- 一键恢复全部隐藏窗口，并在正常退出时自动恢复窗口。
- 支持隐藏单个窗口、临时窗口快捷键、开机启动和桌面/开始菜单快捷方式。
- 简体中文优先，同时支持在设置页面切换 English。
- 提供自包含单文件 EXE，无需预先安装 .NET 运行时。

### 快速开始

1. 从[发行版页面](https://github.com/xulianglikejava/hide-everything/releases)下载 `HideEverything.exe`。
2. 运行程序，点击托盘图标进入设置。
3. 添加正在运行的程序或窗口，并设置快捷键（默认建议使用 `Alt+D`）。
4. 在任意应用中按下快捷键，即可隐藏或恢复目标窗口。

### 支持环境

首发支持 Windows 10 22H2 及以上版本、Windows 11，以及 `win-x64` 桌面环境。
项目使用 .NET 10、WPF 和 Win32 API 构建。

### 安全与隐私

HideEverything 不是加密或安全工具，被隐藏的窗口仍可被其他用户恢复。程序不收集用户数据，
配置和日志仅保存在当前用户的 `%AppData%\HideEverything` 目录中。由于发行包暂未进行代码签名，
Windows SmartScreen 可能会对首次运行的下载文件显示提示。

Hide apps from your screen, the **taskbar**, and **Alt+Tab** with a global keyboard
shortcut — then bring them back the same way. A lightweight Windows tray utility.

> **What it is:** a convenience / screen-privacy tool. It hides windows instantly.
> **What it is not:** a security tool. Hidden windows are **not** protected or
> encrypted — anyone using the PC can restore them.

## Demo

<video src="https://pub-0acffcd7de9442b795cf24e626c3e448.r2.dev/LQ156M1JW09%202026-06-15%2011-03-30.mp4" controls muted width="720">
  Your browser can't play this video —
  <a href="https://pub-0acffcd7de9442b795cf24e626c3e448.r2.dev/LQ156M1JW09%202026-06-15%2011-03-30.mp4">watch the demo</a> instead.
</video>

▶️ [Watch the demo video](https://pub-0acffcd7de9442b795cf24e626c3e448.r2.dev/LQ156M1JW09%202026-06-15%2011-03-30.mp4)

## Features

- **Global shortcuts** — every newly added running app starts with `Alt+D`; press it
  from anywhere to hide/show that app. Hidden windows vanish from the screen, the
  taskbar, and Alt+Tab, and come back focused.
- **Shared shortcuts** — give two or more apps the *same* shortcut and one press
  hides/shows them all together.
- **Persistent window rules** — select one window from the picker, save its exe path,
  process name, window class, and optional case-insensitive title text, then control
  only matching windows after the target app restarts.
- **Restore all** — a tray command and a **panic hotkey** (default `Ctrl+Alt+反引号`,
  configurable in Settings) un-hide everything. Windows are always restored on exit.
- **Hide a specific window** — a "Hide a window…" picker lets you hide individual
  windows instead of a whole app (e.g. just one Chrome profile window). You can even
  **assign a temporary shortcut** to specific windows so a keypress toggles exactly
  those — no need to open HideEverything. **Show last hidden** (default `Ctrl+Alt+Shift+S`)
  or Restore all bring them back.
- **Run at Windows startup** — optional, via the per-user registry Run key.
- **Desktop / Start Menu shortcuts** — create them on demand from the tray
  ("Create shortcut") or Settings (handy for the portable `.exe`).
- **Runs in the tray** — no taskbar clutter; settings live behind the tray icon. A
  first-run welcome panel walks you through the basics.

## Install / Run

### Download

Grab `HideEverything.exe` from the [Releases](https://github.com/xulianglikejava/hide-everything/releases) page and run it. It is
**self-contained** — no .NET install required. It starts minimized to the system
tray (look for the HideEverything icon near the clock).

> The first time you run an unsigned download, Windows SmartScreen may show
> *"Windows protected your PC."* Click **More info → Run anyway**. See
> [Trust & antivirus](#trust--antivirus) below.

### Verify your download (optional but recommended)

Each release ships a `SHA256SUMS.txt`. Compare its hash against your downloaded
`HideEverything.exe` to confirm the file wasn't tampered with in transit:

```powershell
# PowerShell — should match the value in SHA256SUMS.txt
Get-FileHash .\HideEverything.exe -Algorithm SHA256
```

```bash
# Git Bash / Linux / macOS — run in the folder containing both files
sha256sum -c SHA256SUMS.txt
```

If the hashes don't match, do not run the file — re-download it from the
[Releases](https://github.com/xulianglikejava/hide-everything/releases) page.

### Usage

Launching HideEverything opens the Settings window. When it's already running, click its
tray icon (or just launch it again) to reopen Settings. Started automatically at
Windows login, it stays quietly in the tray.

1. **Click the tray icon** (or right-click → **Settings**).
2. **Add app…** → pick a running app or **Browse for .exe**.
3. Click **Set…** to record a shortcut (needs at least one of Ctrl/Alt/Shift/Win).
4. Press your shortcut to hide/show. Give two apps the same shortcut to toggle both.
5. Use the language selector at the bottom of Settings to switch between 简体中文
   and English. The choice is saved for the next launch.

Config is saved to `%AppData%\HideEverything\config.json`. Logs (if any) go to
`%AppData%\HideEverything\logs`. Releases also include `LICENSE` and
`THIRD-PARTY-NOTICES.md`.

## Build from source

Requires the **.NET 10 SDK** (`dotnet --version` reports `10.x`). No Visual Studio
needed.

```powershell
dotnet restore
dotnet build            # verify it compiles
dotnet run              # launch for testing

# single-file, self-contained release exe (with the app icon)
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
# output: bin/Release/net10.0-windows10.0.19041.0/win-x64/publish/HideEverything.exe
```

Replace `Assets/app.ico` with your own icon to rebrand; it drives both the exe and
tray icon.

### Build the installer (optional)

Install [Inno Setup](https://jrsoftware.org/isinfo.php) 6+, publish (above), then:

```powershell
iscc installer\HideIt.iss
# output: installer/output/HideEverything-Setup-1.1.2.exe
```

The installer is per-user (no admin), adds Start-Menu/optional desktop shortcuts, an
optional "start with Windows" task, and removes the startup entry on uninstall.

### Releasing

A GitHub Actions workflow (`.github/workflows/release.yml`) builds the single-file
exe and attaches it to a Release whenever you push a `v*` tag (e.g. `git tag v1.0.0
&& git push --tags`).

The in-app **Check for updates** entry and the optional installer support links point
to this repository's Releases and Issues pages.

## Known limitations & gotchas

- **Fullscreen-exclusive games** may not hide cleanly (black screen, audio keeps
  playing, or the game forces itself back). **Fix:** run the game in
  **borderless windowed** mode. This is an OS limitation, not a bug.
- **Elevated (admin) apps** can't be hidden unless HideEverything also runs as admin.
  HideEverything ships as `asInvoker` (normal privileges).
- **Crash recovery** — if HideEverything crashes while windows are hidden, the next
  launch makes a best-effort attempt to match and restore affected windows. While
  running, use **Restore all hidden** or the panic hotkey.
- **Multi-window apps** (e.g. several Chrome windows) — all matching windows hide
  and show together.
- **Hotkey conflicts** — if a shortcut is already owned by another app globally,
  registration fails and HideEverything shows a non-blocking warning in Settings.

## Privacy

HideEverything collects **no data** and makes **no network calls**. Configuration and
optional crash logs are stored locally under `%AppData%\HideEverything`.

## Trust & antivirus

HideEverything enumerates processes, registers global hotkeys, and hides other apps'
windows from the taskbar/Alt+Tab. That behavior overlaps with what some malware
does, so **SmartScreen warnings and occasional antivirus false positives are
expected** for an unsigned build. The source is open so the behavior is auditable.
For unsigned builds, consider scanning the downloaded file with your preferred
antivirus service before running it.

## License

[MIT](LICENSE) — provided "as is", without warranty of any kind.
