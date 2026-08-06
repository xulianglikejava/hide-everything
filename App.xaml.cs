using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.Input;
using H.NotifyIcon;
using HideIt.Services;
using HideIt.Views;
using WpfControls = System.Windows.Controls;

namespace HideIt;

public partial class App : Application
{
    private const string MutexName = "HideEverything.SingleInstance.Mutex";
    private const string ShowEventName = "HideEverything.ShowSettings.Event";

    /// <summary>由开机启动注册表项传入，区分登录启动并保持静默托盘驻留。</summary>
    public const string StartupArg = "--startup";

    /// <summary>单实例信号创建存在短暂竞态时的重试次数。</summary>
    private const int ExistingInstanceSignalAttempts = 20;

    /// <summary>单实例信号重试间隔，避免重复启动时过早放弃唤起已有实例。</summary>
    private const int ExistingInstanceSignalDelayMilliseconds = 50;

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private AppController? _controller;
    private TaskbarIcon? _tray;
    private MainWindow? _mainWindow;
    private WpfControls.MenuItem? _startupItem;
    private WpfControls.MenuItem? _settingsItem;
    private WpfControls.MenuItem? _hideWindowItem;
    private WpfControls.MenuItem? _restoreAllItem;
    private WpfControls.MenuItem? _shortcutsItem;
    private WpfControls.MenuItem? _desktopShortcutItem;
    private WpfControls.MenuItem? _startMenuShortcutItem;
    private WpfControls.MenuItem? _updatesItem;
    private WpfControls.MenuItem? _exitItem;
    private bool _exiting;

    /// <summary>
    /// 建立单实例、全局异常处理、控制器和托盘；登录启动只加载配置并保持后台驻留。
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 单实例：如果已有实例，通知它把设置窗口带到前台，然后结束当前副本。
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        // 先创建单实例唤起事件，缩短第二个实例在首实例初始化期间无法通知的窗口。
        StartShowSettingsListener();

        Logger.Init();
        DispatcherUnhandledException += (_, args) =>
        {
            Logger.LogException("DispatcherUnhandledException", args.Exception);
            // 尽量继续运行，托盘工具不能因单次界面异常而退出。
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.LogException("UnhandledException", args.ExceptionObject as Exception);

        // 控制器启动恢复阶段也可能产生界面错误提示，先加载默认中文资源避免出现未翻译键名。
        LocalizationService.Apply(LocalizationService.ChineseSimplified);
        _controller = new AppController();
        _controller.Load();

        // 配置语言在创建托盘和窗口之前应用，保证首次可见的界面与用户选择一致。
        LocalizationService.Apply(_controller.Config.Language);

        BuildTray();

        bool startedAtLogin = e.Args.Any(a => string.Equals(a, StartupArg, StringComparison.OrdinalIgnoreCase));

        // 登录启动必须始终静默进入托盘；首次引导留到用户手动打开程序时再展示。
        if (!startedAtLogin && !_controller.Config.FirstRunComplete)
        {
            MaybeShowOnboarding();
        }
        else if (!startedAtLogin)
        {
            // 手动启动需要打开设置；延迟到消息循环运行后执行，确保窗口可靠渲染。
            Dispatcher.BeginInvoke(new Action(ShowSettings),
                System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>监听第二个实例的命名事件，并把设置窗口唤回前台。</summary>
    private void StartShowSettingsListener()
    {
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            // 第二个实例只负责唤起设置窗口，工具托盘图标本身始终保留。
            (_, _) => RequestShowSettings(),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>
    /// 向已有实例发送“打开设置”信号；短暂重试覆盖首实例刚创建互斥量但尚未创建事件的启动窗口。
    /// </summary>
    private static void SignalExistingInstance()
    {
        for (int attempt = 0; attempt < ExistingInstanceSignalAttempts; attempt++)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                {
                    using (existing)
                        existing.Set();
                    return;
                }
            }
            catch
            {
                // 第二个实例即将退出，唤起失败不应阻止已有实例继续运行。
            }

            if (attempt < ExistingInstanceSignalAttempts - 1)
                Thread.Sleep(ExistingInstanceSignalDelayMilliseconds);
        }
    }

    /// <summary>从后台事件安全切回 UI 线程；应用退出后忽略迟到的唤起请求。</summary>
    private void RequestShowSettings()
    {
        if (_exiting || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
            return;

        try
        {
            Dispatcher.BeginInvoke(new Action(ShowSettings));
        }
        catch (InvalidOperationException)
        {
            // Dispatcher 正在关闭时，第二实例的迟到信号无需再处理。
        }
    }

    /// <summary>仅在手动首次启动时展示引导，并在引导结束后持久化完成状态。</summary>
    private void MaybeShowOnboarding()
    {
        if (_controller!.Config.FirstRunComplete) return;

        var onboarding = new OnboardingWindow();
        onboarding.ShowDialog();

        _controller.Config.FirstRunComplete = true;
        _controller.Save();

        if (onboarding.OpenSettingsRequested)
            ShowSettings();
    }

    /// <summary>创建始终可见的工具托盘入口，并绑定设置、恢复、启动和退出操作。</summary>
    private void BuildTray()
    {
        _tray = new TaskbarIcon { ToolTipText = AppInfo.ProductName };
        try
        {
            _tray.IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/app.ico"));
        }
        catch (Exception ex)
        {
            Logger.LogException("Tray icon load", ex);
        }

        var menu = new WpfControls.ContextMenu();

        _settingsItem = new WpfControls.MenuItem();
        _settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(_settingsItem);

        _hideWindowItem = new WpfControls.MenuItem();
        _hideWindowItem.Click += (_, _) => OpenHideWindowPicker();
        menu.Items.Add(_hideWindowItem);

        _restoreAllItem = new WpfControls.MenuItem();
        _restoreAllItem.Click += (_, _) => _controller!.ShowAllHidden();
        menu.Items.Add(_restoreAllItem);

        _startupItem = new WpfControls.MenuItem
        {
            IsCheckable = true,
            IsChecked = _controller!.IsRunAtStartupEnabled,
        };
        _startupItem.Click += (_, _) => SetRunAtStartupFromTray();
        menu.Items.Add(_startupItem);

        _shortcutsItem = new WpfControls.MenuItem();
        _desktopShortcutItem = new WpfControls.MenuItem();
        _desktopShortcutItem.Click += (_, _) => MakeShortcut(desktop: true);
        _startMenuShortcutItem = new WpfControls.MenuItem();
        _startMenuShortcutItem.Click += (_, _) => MakeShortcut(desktop: false);
        _shortcutsItem.Items.Add(_desktopShortcutItem);
        _shortcutsItem.Items.Add(_startMenuShortcutItem);
        menu.Items.Add(_shortcutsItem);

        menu.Items.Add(new WpfControls.Separator());

        _updatesItem = new WpfControls.MenuItem();
        _updatesItem.Click += (_, _) => OpenUrl(AppInfo.ReleasesUrl);
        menu.Items.Add(_updatesItem);

        _exitItem = new WpfControls.MenuItem();
        _exitItem.Click += (_, _) => ExitApp();
        menu.Items.Add(_exitItem);

        // 托盘菜单不是 XAML 控件，语言切换时需要主动刷新每个菜单项的标题。
        LocalizationService.LanguageChanged += UpdateTrayLocalization;
        UpdateTrayLocalization();

        _tray.ContextMenu = menu;
        // 单击和双击都打开设置，避免仅依赖双击事件导致入口不稳定。
        var showCommand = new RelayCommand(ShowSettings);
        _tray.LeftClickCommand = showCommand;
        _tray.DoubleClickCommand = showCommand;
        _tray.ForceCreate(enablesEfficiencyMode: false);
    }

    /// <summary>刷新托盘菜单文案；勾选状态和点击行为在切换语言时保持不变。</summary>
    private void UpdateTrayLocalization()
    {
        if (_settingsItem != null) _settingsItem.Header = LocalizationService.Get("Tray_Settings");
        if (_hideWindowItem != null) _hideWindowItem.Header = LocalizationService.Get("Tray_HideWindow");
        if (_restoreAllItem != null) _restoreAllItem.Header = LocalizationService.Get("Tray_RestoreAll");
        if (_startupItem != null) _startupItem.Header = LocalizationService.Get("Tray_Startup");
        if (_shortcutsItem != null) _shortcutsItem.Header = LocalizationService.Get("Tray_Shortcuts");
        if (_desktopShortcutItem != null) _desktopShortcutItem.Header = LocalizationService.Get("Tray_Desktop");
        if (_startMenuShortcutItem != null) _startMenuShortcutItem.Header = LocalizationService.Get("Tray_StartMenu");
        if (_updatesItem != null) _updatesItem.Header = LocalizationService.Get("Tray_CheckUpdates");
        if (_exitItem != null) _exitItem.Header = LocalizationService.Get("Tray_Exit");
    }

    /// <summary>创建或复用设置窗口；关闭窗口只隐藏到托盘，保持控制器和老板键继续工作。</summary>
    private void ShowSettings()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow(_controller!);
            _mainWindow.Closing += (_, args) =>
            {
                // 普通关闭只隐藏到托盘；只有退出菜单设置了 _exiting 后才真正关闭窗口。
                if (!_exiting)
                {
                    args.Cancel = true;
                    _mainWindow!.Hide();
                }
            };
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
        // 从后台托盘进程唤起设置时，短暂置顶再取消以可靠地交还前台焦点。
        _mainWindow.Topmost = true;
        _mainWindow.Topmost = false;
        _mainWindow.Focus();

        if (_startupItem != null)
            _startupItem.IsChecked = _controller!.IsRunAtStartupEnabled;
    }

    /// <summary>应用托盘菜单中的开机启动变更，并在系统拒绝时回滚勾选状态。</summary>
    private void SetRunAtStartupFromTray()
    {
        if (_startupItem == null || _controller == null)
            return;

        bool requested = _startupItem.IsChecked;
        if (_controller.TrySetRunAtStartup(requested))
            return;

        // 以注册表真实状态回填，覆盖注册表写入或配置保存失败后的回滚结果。
        _startupItem.IsChecked = _controller.IsRunAtStartupEnabled;
        MessageBox.Show(
            LocalizationService.Get("Tray_StartupFailed"),
            AppInfo.ProductName,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    /// <summary>打开窗口选择器；设置窗口可见时将其作为对话框所有者。</summary>
    private void OpenHideWindowPicker()
    {
        var dlg = new HideWindowDialog(_controller!);
        if (_mainWindow is { IsVisible: true })
            dlg.Owner = _mainWindow;
        if (dlg.ShowDialog() == true)
            _controller!.HideSpecificWindows(dlg.Result);
    }

    /// <summary>创建桌面或开始菜单快捷方式，并把结果以非静默提示反馈给用户。</summary>
    private void MakeShortcut(bool desktop)
    {
        bool ok = desktop
            ? ShortcutService.CreateDesktopShortcut()
            : ShortcutService.CreateStartMenuShortcut();
        string where = desktop
            ? LocalizationService.Get("Tray_Desktop")
            : LocalizationService.Get("Tray_StartMenu");
        MessageBox.Show(
            ok
                ? LocalizationService.Format("Tray_ShortcutCreated", where)
                : LocalizationService.Format("Tray_ShortcutCreateFailed", where),
            AppInfo.ProductName, MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    /// <summary>使用系统默认浏览器打开更新地址；失败只记日志，不影响托盘驻留。</summary>
    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.LogException($"OpenUrl {url}", ex);
        }
    }

    /// <summary>执行显式退出：先停止控制器并恢复隐藏窗口，再释放托盘和设置窗口。</summary>
    private void ExitApp()
    {
        _exiting = true;
        LocalizationService.LanguageChanged -= UpdateTrayLocalization;
        // 控制器退出会恢复全部隐藏窗口并注销老板键。
        _controller?.Dispose();
        _tray?.Dispose();
        _mainWindow?.Close();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LocalizationService.LanguageChanged -= UpdateTrayLocalization;
        if (!_exiting)
        {
            _controller?.Dispose();
            _tray?.Dispose();
        }
        _showWait?.Unregister(null);
        _showEvent?.Dispose();
        try { _mutex?.ReleaseMutex(); } catch { /* not owned */ }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
