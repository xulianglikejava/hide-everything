# Windows 桌面窗口“老板键”开源项目调研

> 调研日期：2026-08-05
> 范围：Windows 桌面程序；全局快捷键；隐藏/展示指定窗口；目标程序继续运行；配置可持久化；最终可打包为 `.exe`。
> 方法：优先检查 GitHub 仓库主页、README、源码入口、项目/构建文件、Release 页面和许可证；源码事实以仓库当前提交为准。

## 结论先行

最推荐从 [`innoventisinfotech/HideIt`](https://github.com/innoventisinfotech/HideIt) fork 或拆分复用。它已经同时覆盖了：按进程绑定快捷键、按单个窗口绑定快捷键、`SW_HIDE`/`SW_SHOW` 真隐藏、从任务栏和 Alt+Tab 移除、配置写入 `%AppData%\HideIt\config.json`、托盘驻留、开机启动和单文件自包含发布。它的源码入口、WPF 项目文件、Inno Setup 脚本和 Release 中的 `HideIt.exe` 都真实存在。

需要补的一点是：它把“按单个窗口绑定快捷键”明确做成了 session-only，因为原始 `HWND` 在窗口关闭或程序重启后不可靠。因此，若产品要求“我配置一次，之后一直保存”，建议持久化“匹配规则”而不是裸窗口句柄，例如：规范化 exe 路径 + 进程名 + 窗口类名 + 可选标题匹配规则；程序运行后重新枚举窗口并解析规则。

如果目标是最快做出可用原型，[`infinotiver/boss-key`](https://github.com/infinotiver/boss-key) 的 AutoHotkey v1 脚本最短：配置目标 exe 和一个快捷键，核心就是 `WinHide`/`WinShow`。但默认配置会先最小化，必须把 `minimize_on_hide` 设为 `0` 才符合“隐藏而不是最小化”。

## 判定口径：隐藏不等于最小化

本调研把下面情况认定为“真正隐藏窗口”：源码调用 Win32 [`ShowWindow`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-showwindow) 的 `SW_HIDE`（0），或 AutoHotkey 的 `WinHide`；展示时调用 `SW_SHOW`、`SW_SHOWNORMAL`、`SW_RESTORE` 或 `WinShow`。这改变的是窗口的可见状态，不是退出进程。候选项目中如果只调用 `WinMinimize`、`SW_MINIMIZE`，则标为“最小化方案”，不算完全满足需求。

全局快捷键通常基于 Win32 [`RegisterHotKey`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)，按下后由窗口消息循环接收 `WM_HOTKEY`；快捷键冲突时注册会失败，产品需要在设置界面提示用户。目标窗口枚举通常基于 [`EnumWindows`](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumwindows)，并结合进程 ID、窗口类名、标题和可见性过滤。

## 候选总览

| 候选 | 方向 | 真隐藏 | 配置/持久化 | `.exe` 与构建 | 许可证 | 判断 |
|---|---|---|---|---|---|---|
| [`HideIt`](https://github.com/innoventisinfotech/HideIt) | C# / WPF / .NET | 是；`SW_HIDE`，并修改扩展样式以脱离任务栏和 Alt+Tab | JSON；按进程配置持久化，单窗口快捷键当前为临时绑定 | Release 有 `HideIt.exe`；`.NET 10` 单文件自包含；Inno Setup | MIT | 首选基线 |
| [`Hide-My-Window`](https://github.com/priestofpsi/Hide-My-Window) | C# / WinForms / .NET Framework | 是；保存原始窗口样式后调用 `SW_HIDE`，展示时恢复 | XML；热键、隐藏窗口状态、固定窗口等均有存储设计 | Release 有 `Hide.My.Window.exe`；项目目标 .NET Framework 4.5 | 仓库根目录未发现 LICENSE | 适合参考旧实现，不建议直接复制 |
| [`MiniHide`](https://github.com/samcro1967/MiniHide) | C# / WinForms / .NET | 是；对活动窗口调用 `SW_HIDE`，展示用 `SW_RESTORE` | JSON；保存热键、启动项等，但隐藏目标窗口不是持久化配置 | Release 有安装器；项目启用 `win-x64` 自包含单文件；有 Inno Setup 脚本 | MIT | 适合拆出简洁骨架 |
| [`boss-key`](https://github.com/infinotiver/boss-key) | AutoHotkey v1 | 可选；配置为 `minimize_on_hide=0` 时是 `WinHide` | 同目录 `config.ini`；按 exe 列表配置 | 仓库含编译出的 `boss-key.exe`；Release 有源码归档但未见独立 exe 资产；README 仍要求 AHK v1 | MIT | 最快原型，工程化能力弱 |
| [`bosskey`](https://github.com/intothevoid/bosskey) | C++ / MFC | 是；`ShowWindow(SW_HIDE)` / `SW_SHOWNORMAL` | 目标窗口和热键主要在内存中；未发现现代配置文件实现 | VS/MFC 项目，Win32 Release 配置；无 GitHub Release 资产 | MIT | 最小 Win32 参考，年代久远 |
| [`global-hotkey`](https://github.com/tauri-apps/global-hotkey) | Rust 库 | 否；只负责全局热键 | 由宿主程序自行持久化 | Cargo crate；Windows backend 使用 Win32 | Apache-2.0 OR MIT | Rust 组合方案的热键部件 |

Release 资产核验：[`HideIt v1.1.2`](https://github.com/innoventisinfotech/HideIt/releases/tag/v1.1.2) 含 `HideIt.exe` 和校验文件；[`MiniHide v1.0.0`](https://github.com/samcro1967/MiniHide/releases/tag/v1.0.0) 含安装器；[`boss-key v0.0.1a`](https://github.com/infinotiver/boss-key/releases/tag/v0.0.1a) 页面存在但未发现单独下载的 exe；[`Hide-My-Window V1.0b`](https://github.com/priestofpsi/Hide-My-Window/releases/tag/V1.0b) 含 `Hide.My.Window.exe`；C++ `bosskey` 没有可用的 GitHub Release 页面资产。

## 逐项核验

### 1. HideIt：最符合需求的 C# / WPF 基线

**真实实现。** [`Services/WindowHider.cs`](https://github.com/innoventisinfotech/HideIt/blob/6c2041a48229398f4ad0bb4ca8341c1153329a56/Services/WindowHider.cs) 保存每个隐藏窗口的 `HWND`、原始扩展样式和进程 ID；隐藏时先把 `WS_EX_TOOLWINDOW` 加入扩展样式、去掉 `WS_EX_APPWINDOW`，再调用 `ShowWindow(SW_HIDE)`；展示时恢复原始样式并调用 `ShowWindow(SW_SHOW)`。源码没有结束、挂起或杀掉目标进程的调用，符合“隐藏界面、程序继续后台运行”的目标。其 [`Services/Native.cs`](https://github.com/innoventisinfotech/HideIt/blob/6c2041a48229398f4ad0bb4ca8341c1153329a56/Services/Native.cs) 还实现了顶层窗口枚举、进程 ID 过滤和真实应用窗口过滤。

**热键与长期驻留。** [`Services/HotKeyService.cs`](https://github.com/innoventisinfotech/HideIt/blob/6c2041a48229398f4ad0bb4ca8341c1153329a56/Services/HotKeyService.cs) 把全局热键注册到一个 message-only window，而不是设置窗口；所以设置窗口关闭到托盘后，热键仍可继续工作。`RegisterAll` 会去重、重新注册，并通过 `RegistrationFailed` 报告冲突。[`AppController.cs`](https://github.com/innoventisinfotech/HideIt/blob/6c2041a48229398f4ad0bb4ca8341c1153329a56/AppController.cs) 在配置保存后重新应用热键，退出时释放热键并恢复全部窗口。

**配置与发布。** [`ConfigStore.cs`](https://github.com/innoventisinfotech/HideIt/blob/6c2041a48229398f4ad0bb4ca8341c1153329a56/Services/ConfigStore.cs) 把配置写到 `%AppData%\HideIt\config.json`；[`Models/AppEntry.cs`](https://github.com/innoventisinfotech/HideIt/blob/6c2041a48229398f4ad0bb4ca8341c1153329a56/Models/AppEntry.cs) 当前按进程名、显示名、可选 exe 路径和快捷键建模。README 给出了 `dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true`，[`HideIt.csproj`](https://github.com/innoventisinfotech/HideIt/blob/6c2041a48229398f4ad0bb4ca8341c1153329a56/HideIt.csproj) 也明确是 WPF WinExe；[`installer/HideIt.iss`](https://github.com/innoventisinfotech/HideIt/blob/6c2041a48229398f4ad0bb4ca8341c1153329a56/installer/HideIt.iss) 提供 Inno Setup 安装器。

**不足与复用方式。**

- 进程配置的语义是“同一进程的所有真实顶层窗口一起切换”；这对浏览器多窗口有用，但不满足只选其中一个窗口的长期规则。
- 单窗口绑定存储的是当前 `HWND`，源码明确标为不持久化，因为窗口重建后句柄会变化。应把它改成稳定 selector，并在每次热键触发或窗口创建后重新匹配。
- 目标程序以管理员权限运行时，HideIt 的 README 明确提示普通权限实例可能无法操作，需要权限级别一致。
- 依赖 [`CommunityToolkit.Mvvm`](https://www.nuget.org/packages/CommunityToolkit.Mvvm/) 和 [`H.NotifyIcon.Wpf`](https://www.nuget.org/packages/H.NotifyIcon.Wpf/)，再分发时应逐项核对第三方依赖许可证和发布条款。

许可证：仓库 [`LICENSE`](https://github.com/innoventisinfotech/HideIt/blob/6c2041a48229398f4ad0bb4ca8341c1153329a56/LICENSE) 为 MIT；复用时保留版权和许可证文本即可，但仍需审核 NuGet 依赖。

### 2. Hide-My-Window：功能完整但老旧的 C# 参考

**真实实现。** [`ExternalReferences.cs`](https://github.com/priestofpsi/Hide-My-Window/blob/66fe3b86910283679e4955eb68e92b60f23e3e51/Hide%20My%20Window/ExternalReferences/ExternalReferences.cs) 读取原始窗口样式，去掉可见标志、加入 `WS_EX_TOOLWINDOW`、去掉 `WS_EX_APPWINDOW`，然后调用 `ShowWindow` 的 Hide 命令；展示时恢复原始样式和无激活展示。[`WindowInfo.cs`](https://github.com/priestofpsi/Hide-My-Window/blob/66fe3b86910283679e4955eb68e92b60f23e3e51/Hide%20My%20Window/Windows/WindowInfo.cs) 提供 `Hide`、`Show`、`ToggleHidden`，不是单纯最小化。

**持久化和热键。** [`HiddenWindowStore.cs`](https://github.com/priestofpsi/Hide-My-Window/blob/66fe3b86910283679e4955eb68e92b60f23e3e51/Hide%20My%20Window/FileStorage/HiddenWindowStore.cs) 使用 XML 存储隐藏窗口条目，并在新增/移除时保存；[`SettingsStore.cs`](https://github.com/priestofpsi/Hide-My-Window/blob/66fe3b86910283679e4955eb68e92b60f23e3e51/Hide%20My%20Window/FileStorage/SettingsStore.cs) 存储热键、固定应用、开机启动等；[`GlobalHotKeyManager.cs`](https://github.com/priestofpsi/Hide-My-Window/blob/66fe3b86910283679e4955eb68e92b60f23e3e51/Hide%20My%20Window/HotKeys/GlobalHotKeyManager.cs) 通过 Win32 `RegisterHotKey` 注册全局热键。

**成本与风险。** [`Hide My Window.csproj`](https://github.com/priestofpsi/Hide-My-Window/blob/66fe3b86910283679e4955eb68e92b60f23e3e51/Hide%20My%20Window/Hide%20My%20Window.csproj) 目标为 .NET Framework 4.5，项目较大、依赖旧式 WinForms/UI Automation/安装工程；最近提交时间也明显早于其他候选。仓库根目录没有 `LICENSE` 文件，README 没有声明许可证，因此不应直接把其代码复制进可分发产品；可以把它作为行为和边界处理参考，若要复用代码需先取得作者许可或确认仓库的明确授权。

### 3. MiniHide：简洁的 C# / WinForms 骨架

**真实实现。** [`Managers/WindowManager.cs`](https://github.com/samcro1967/MiniHide/blob/97c1ae5ad725f978b082e1b9dcdf3bbb7bf4627f/Managers/WindowManager.cs) 获取前台窗口、标题、类名、进程 ID 和图标，过滤桌面、任务栏、自身窗口等对象；`HideWindow` 调用 `ShowWindow(SW_HIDE)`，`RestoreWindow` 调用 `ShowWindow(SW_RESTORE)`。这是窗口级隐藏，不是退出目标程序。

**热键与配置。** [`Managers/HotkeyManager.cs`](https://github.com/samcro1967/MiniHide/blob/97c1ae5ad725f978b082e1b9dcdf3bbb7bf4627f/Managers/HotkeyManager.cs) 封装 `RegisterHotKey`/`UnregisterHotKey` 并在冲突时抛出错误；[`Managers/SettingsManager.cs`](https://github.com/samcro1967/MiniHide/blob/97c1ae5ad725f978b082e1b9dcdf3bbb7bf4627f/Managers/SettingsManager.cs) 将设置写入 `%LocalAppData%\MiniHide\settings.json`，并管理当前用户的开机启动项。需要注意，当前 `AppSettings` 持久化的是热键、启动项、排除进程等，不是用户选中的窗口规则；`ManagedWindow` 中的 `HWND` 也是运行时对象。

**构建与复用。** [`MiniHide.csproj`](https://github.com/samcro1967/MiniHide/blob/97c1ae5ad725f978b082e1b9dcdf3bbb7bf4627f/MiniHide.csproj) 配置了 `net10.0-windows`、`win-x64`、`SelfContained=true`、`PublishSingleFile=true`；[`MiniHide.iss`](https://github.com/samcro1967/MiniHide/blob/97c1ae5ad725f978b082e1b9dcdf3bbb7bf4627f/MiniHide.iss) 提供安装器。它适合提取 `WindowManager`、`HotkeyService`、`SettingsManager` 的分层结构，再增加“窗口规则”模型和按规则重新绑定。

许可证：仓库 [`LICENSE`](https://github.com/samcro1967/MiniHide/blob/97c1ae5ad725f978b082e1b9dcdf3bbb7bf4627f/LICENSE) 为 MIT。

### 4. boss-key：最短的 AutoHotkey v1 方案

**真实实现。** [`boss-key.ahk`](https://github.com/infinotiver/boss-key/blob/5bb48c8febdeb42b55b2baa703a4975d231b1da5/boss-key.ahk) 从同目录 `config.ini` 读取快捷键和目标 exe，使用 `WinGet ... List` 找出每个目标窗口；显示时调用 `WinShow`，隐藏时调用 `WinHide`。它没有结束目标进程，因此实现方向符合后台继续运行。

**关键陷阱。** [`config.ini`](https://github.com/infinotiver/boss-key/blob/5bb48c8febdeb42b55b2baa703a4975d231b1da5/config.ini) 默认 `minimize_on_hide = 1`，脚本先 `WinMinimize` 再 `WinHide`；要实现纯隐藏，应将其设为 `0`。展示时 `restore_on_show` 和 `activate_on_show` 也可分别关闭，以减少闪烁和抢焦点。

**交付方式。** README 说明可以直接运行仓库中的 exe，也可以运行 `.ahk`；但同一 README 又要求安装 AutoHotkey v1.1，意味着该 exe 不应未经测试就假设为完全自包含。配置文件必须和 exe/脚本位于同一目录。Release 页面目前只有源码归档，没有单独 exe 资产。

**适合什么。** 适合先验证“快捷键 + 多目标 exe + 真隐藏”的交互；不适合作为最终工程基线，因为目标粒度是进程名、窗口选择界面很弱、配置依赖手工编辑、没有稳定窗口规则和生命周期管理。

许可证：仓库 [`LICENSE`](https://github.com/infinotiver/boss-key/blob/5bb48c8febdeb42b55b2baa703a4975d231b1da5/LICENSE) 为 MIT；复用脚本时保留许可证文本，并明确选择 AutoHotkey v1 的运行时/编译链。

### 5. bosskey：最小 C++ / MFC 实现

**真实实现。** [`BossKeeDlg.cpp`](https://github.com/intothevoid/bosskey/blob/65f709aff963256b1c71b2613ce196bca95bbd73/BossKee/BossKeeDlg.cpp) 用 `EnumWindows` 按标题列出窗口，注册固定的 `Ctrl+Space`，热键回调中对选定 `HWND` 调用 `ShowWindow(SW_HIDE)`，再次调用 `SW_SHOWNORMAL`。因此它是真隐藏/展示，而不是最小化。

**不足。** 目标通过窗口标题映射到 `HWND`，窗口重建后映射会失效；快捷键写死在源码；全局变量保存当前标题、句柄和隐藏标记；README 和源码没有看到配置文件、稳定规则或现代 Release 发布流程。[`BossKee.vcxproj`](https://github.com/intothevoid/bosskey/blob/65f709aff963256b1c71b2613ce196bca95bbd73/BossKee/BossKee/BossKee.vcxproj) 是旧式 MFC/Win32 项目，Release 仍使用 `v110` 工具集，项目没有 GitHub Release 资产。

许可证：仓库 [`LICENSE`](https://github.com/intothevoid/bosskey/blob/65f709aff963256b1c71b2613ce196bca95bbd73/LICENSE) 为 MIT。可以参考其最小消息处理流程，但不建议以它作为完整产品基线。

### 6. global-hotkey：Rust 热键库，不是老板键程序

[`tauri-apps/global-hotkey`](https://github.com/tauri-apps/global-hotkey) 的 README、Cargo manifest 和 Windows backend 都真实存在。Windows backend 创建隐藏的 Win32 helper window，调用 `RegisterHotKey`/`UnregisterHotKey`，把按下/释放事件送到 receiver；README 特别要求创建它的线程同时运行 Win32 event loop。它只解决热键，不解决目标窗口枚举、窗口规则、持久化、托盘或 `.exe` 打包。

如果选择 Rust，可以组合 `global-hotkey` + `windows-sys`/`windows` 的 User32 API，再自己实现 `EnumWindows`、`GetWindowThreadProcessId`、`ShowWindow`、扩展样式保存和配置文件；这比 fork 一个现成老板键程序的工作量大。许可证为 Apache-2.0 OR MIT，见 [`Cargo.toml`](https://github.com/tauri-apps/global-hotkey/blob/8c8bcd5b28d6952ab50576e315be828a46aa9f1a/Cargo.toml) 和 [`LICENSE-MIT`](https://github.com/tauri-apps/global-hotkey/blob/8c8bcd5b28d6952ab50576e315be828a46aa9f1a/LICENSE-MIT)。

## Go 方向扫描结果

没有找到一个同时具备“窗口真隐藏 + 全局热键 + 持久化配置 + 可下载 exe”的 Go 开源候选，因而不把 Go 项目列入主候选。值得参考的底层库是 [`lxn/win`](https://github.com/lxn/win)：它是 Go 的 Windows API wrapper，`user32.go` 暴露 `ShowWindow` 常量/调用，许可证是 BSD 风格；但它不是老板键程序，也没有 `RegisterHotKey`、配置 UI 或发行版，不能直接满足需求。见 [`README.mdown`](https://github.com/lxn/win/blob/a377121e959e22055dd01ed4bb2383e5bd02c238/README.mdown)、[`user32.go`](https://github.com/lxn/win/blob/a377121e959e22055dd01ed4bb2383e5bd02c238/user32.go) 和 [`LICENSE`](https://github.com/lxn/win/blob/a377121e959e22055dd01ed4bb2383e5bd02c238/LICENSE)。

## 推荐组合

### 组合 A：fork HideIt，再补“稳定窗口规则”（推荐）

保留 HideIt 的 `WindowHider`、`HotKeyService`、`ConfigStore`、托盘生命周期和发布脚本；把配置模型从“进程名/当前 HWND”扩展为：

```text
WindowRule
├── ExecutablePath      可选，优先于进程名
├── ProcessName         可选
├── WindowClassName     可选
├── TitlePattern        可选，精确或正则
└── HotKey              必填
```

运行时流程应是：读取规则 → 枚举真实顶层窗口 → 计算匹配 → 保存当前隐藏窗口的 HWND 和原始样式 → 热键切换 → 窗口销毁时清理 → 新窗口出现时重新匹配。这样“配置一直保存”保存的是规则，`HWND` 只作为当前会话的缓存。

### 组合 B：先用 AHK 验证交互，再迁移 C#

从 `boss-key` 开始，把 `minimize_on_hide` 改成 `0`，验证目标程序是否允许被隐藏、展示时是否需要激活、多个窗口是否应一起切换。验证通过后，把配置格式和匹配语义迁移到 HideIt/MiniHide；不要把 AHK 的“同目录手工 INI + 进程名”直接当作最终产品模型。

### 组合 C：Rust/Go 自研

只有在明确需要 Rust/Go 的部署或生态优势时采用。Rust 至少可以复用 `global-hotkey`；Go 可复用 `lxn/win` 的 Win32 声明，但两者都不能替代窗口生命周期、稳定规则、托盘和发布工程。就本需求而言，直接 fork C# 基线的总风险最低。

## 许可证与再分发风险

- MIT 项目（HideIt、MiniHide、boss-key、BossKee）通常允许修改、商业使用和再分发，但必须保留版权声明和许可证文本；“允许复用”不等于作者提供担保。
- `Hide-My-Window` 仓库根目录未发现许可证文件，不能按“GitHub 公开 = 可复制”处理；推荐只参考行为，代码复用前先确认授权。
- `global-hotkey` 是 Apache-2.0 OR MIT 双许可证，按项目选择的许可证条件保留相应文件和声明。
- `lxn/win` 是 BSD 风格许可证，若使用其代码或分发其依赖，应保留版权、条件和免责声明。
- HideIt 的 NuGet 依赖、AutoHotkey 运行时/编译产物、图标和安装器也应在真正发布前单独做依赖清单与许可证核验；本调研没有把“仓库主许可证”误当成全部依赖许可证。

## 最终建议

第一阶段直接以 HideIt 为基线，先不改 UI：验证 1 个目标窗口、多个同进程窗口、窗口重启后自动重新匹配、快捷键冲突、管理员权限和退出恢复。第二阶段再增加稳定窗口规则和规则失效提示。验收标准必须明确写成“目标进程仍在运行，窗口从屏幕/任务栏/Alt+Tab 消失，快捷键再次按下后恢复”，这样可以避免误把最小化方案当成老板键。
