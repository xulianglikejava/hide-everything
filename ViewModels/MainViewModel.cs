using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HideIt.Models;
using HideIt.Services;
using HideIt.Views;

namespace HideIt.ViewModels;

/// <summary>驱动设置窗口：管理受控程序列表、快捷键、恢复和启动选项。</summary>
public partial class MainViewModel : ObservableObject
{
    private readonly AppController _controller;

    public ObservableCollection<AppEntryVm> Apps { get; } = new();

    /// <summary>设置界面展示的持久窗口规则。</summary>
    public ObservableCollection<WindowRuleVm> WindowRules { get; } = new();

    [ObservableProperty]
    private bool _runAtStartup;

    /// <summary>设置页当前选择的语言；保存后立即刷新应用级资源字典。</summary>
    [ObservableProperty]
    private string _language;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>动态窗口规则等待目标出现时的非错误提示。</summary>
    [ObservableProperty]
    private string? _dynamicStatusMessage;

    public MainViewModel(AppController controller)
    {
        _controller = controller;
        _runAtStartup = controller.IsRunAtStartupEnabled;
        _language = LocalizationService.Normalize(controller.Config.Language);
        controller.HotKeyRegistrationFailed += OnHotKeyFailed;
        controller.ConfigChanged += OnConfigChanged;
        controller.DynamicWindowStatusChanged += OnDynamicWindowStatusChanged;
        controller.WindowOperationStatusChanged += OnWindowOperationStatusChanged;
        LocalizationService.LanguageChanged += OnLanguageResourcesChanged;
        _dynamicStatusMessage = controller.DynamicWindowStatusMessage;
        _statusMessage = controller.WindowOperationStatusMessage;
        ReloadApps();

        // 控制器可能在设置窗口创建前就发现了启动时冲突，这里补显示一次提示。
        var failed = controller.FailedHotKeys.FirstOrDefault();
        if (failed != null)
            StatusMessage = BuildConflictMessage(failed);
    }

    private void OnConfigChanged() =>
        Application.Current?.Dispatcher.BeginInvoke(new Action(ReloadApps));

    private void OnHotKeyFailed(HotKeyCombo combo) =>
        StatusMessage = BuildConflictMessage(combo);

    /// <summary>接收控制器的动态匹配状态，并与快捷键冲突等错误提示分开显示。</summary>
    private void OnDynamicWindowStatusChanged(string? message) => DynamicStatusMessage = message;

    /// <summary>接收权限或特殊窗口提示；使用设置页状态文本，不弹出持续错误对话框。</summary>
    private void OnWindowOperationStatusChanged(string? message) => StatusMessage = message;

    /// <summary>统一生成设置窗口中的冲突提示，明确说明已有快捷键不会被破坏。</summary>
    private static string BuildConflictMessage(HotKeyCombo combo) =>
        LocalizationService.Format("Status_HotKeyConflict", combo.Display());

    /// <summary>语言切换后刷新派生文案；清除旧语言的暂态提示，避免中英文混杂。</summary>
    private void OnLanguageResourcesChanged()
    {
        if (Language != LocalizationService.CurrentLanguage)
            Language = LocalizationService.CurrentLanguage;
        StatusMessage = null;
        DynamicStatusMessage = null;
        OnPropertyChanged(nameof(PanicHotKeyText));
        OnPropertyChanged(nameof(ShowLastHotKeyText));
        ReloadApps();
    }

    /// <summary>把用户选择交给控制器事务保存，并由控制器负责应用资源字典。</summary>
    partial void OnLanguageChanged(string value) => _controller.SetLanguage(value);

    private void ReloadApps()
    {
        Apps.Clear();
        foreach (var entry in _controller.Config.Apps)
        {
            var icon = _controller.Catalog.GetIconFor(entry.ExePath);
            Apps.Add(new AppEntryVm(entry, icon));
        }

        WindowRules.Clear();
        foreach (var rule in _controller.Config.WindowRules)
        {
            var icon = _controller.Catalog.GetIconFor(rule.ExePath);
            WindowRules.Add(new WindowRuleVm(rule, icon));
        }
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        if (_controller.TrySetRunAtStartup(value))
        {
            StatusMessage = null;
            return;
        }

        // 注册表写入失败时恢复复选框，避免用户看到一个实际未生效的开机启动状态。
        _runAtStartup = _controller.IsRunAtStartupEnabled;
        OnPropertyChanged(nameof(RunAtStartup));
        StatusMessage = LocalizationService.Get("Status_StartupFailed");
    }

    [RelayCommand]
    private void AddApp()
    {
        var dlg = new AddAppDialog(_controller.Catalog) { Owner = OwnerWindow() };
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            _controller.Config.Apps.Add(dlg.Result);
            _controller.SaveAndReapply();
        }
    }

    [RelayCommand]
    private void Remove(AppEntryVm? vm)
    {
        if (vm == null) return;
        // 删除前由控制器恢复该程序的隐藏窗口，避免规则消失后用户失去入口。
        if (!_controller.RemoveApp(vm.Model))
            StatusMessage = _controller.WindowOperationStatusMessage
                ?? LocalizationService.Get("Status_RemoveAppFailed");
    }

    /// <summary>从窗口选择器捕获稳定属性并立即保存持久规则。</summary>
    [RelayCommand]
    private void AddWindowRule()
    {
        var dlg = new AddWindowRuleDialog(_controller) { Owner = OwnerWindow() };
        if (dlg.ShowDialog() == true && dlg.Result != null)
        {
            _controller.Config.WindowRules.Add(dlg.Result);
            _controller.SaveAndReapply();
        }
    }

    [RelayCommand]
    private void SetHotKey(AppEntryVm? vm)
    {
        if (vm == null) return;
        var dlg = new HotKeyCaptureDialog { Owner = OwnerWindow() };
        if (dlg.ShowDialog() == true)
        {
            var requested = dlg.Result;
            var previous = vm.Model.HotKey;
            // 控制器只在新快捷键注册成功后提交配置，冲突时回滚到旧快捷键。
            bool applied = _controller.TrySetAppHotKey(vm.Model, requested);
            vm.RefreshHotKey();
            StatusMessage = applied
                ? null
                : requested == null
                    ? LocalizationService.Get("Status_ClearHotKeyFailed")
                    : LocalizationService.Format(
                        "Status_HotKeyRollback",
                        requested.Display(),
                        previous?.Display() ?? LocalizationService.Get("Common_None"));
        }
    }

    /// <summary>切换程序规则启用状态；控制器负责禁用前恢复窗口和冲突回滚。</summary>
    [RelayCommand]
    private void ToggleEnabled(AppEntryVm? vm)
    {
        if (vm == null) return;

        bool requested = !vm.IsEnabled;
        bool applied = _controller.SetAppEnabled(vm.Model, requested);
        vm.RefreshEnabled();
        StatusMessage = applied
            ? null
            : _controller.WindowOperationStatusMessage
                ?? (requested
                    ? LocalizationService.Format("Status_EnableRuleFailed", vm.ShortcutText)
                    : LocalizationService.Get("Status_DisableRuleFailed"));
    }

    /// <summary>删除窗口规则；控制器会先恢复仍隐藏的匹配窗口。</summary>
    [RelayCommand]
    private void RemoveWindowRule(WindowRuleVm? vm)
    {
        if (vm == null) return;
        if (!_controller.RemoveWindowRule(vm.Model))
            StatusMessage = _controller.WindowOperationStatusMessage
                ?? LocalizationService.Get("Status_RemoveWindowRuleFailed");
    }

    /// <summary>更新窗口规则快捷键，冲突时保留旧快捷键。</summary>
    [RelayCommand]
    private void SetWindowRuleHotKey(WindowRuleVm? vm)
    {
        if (vm == null) return;

        var dlg = new HotKeyCaptureDialog { Owner = OwnerWindow() };
        if (dlg.ShowDialog() != true) return;

        var requested = dlg.Result;
        var previous = vm.Model.HotKey;
        bool applied = _controller.TrySetWindowRuleHotKey(vm.Model, requested);
        vm.RefreshHotKey();
        StatusMessage = applied
                ? null
                : requested == null
                ? LocalizationService.Get("Status_ClearWindowHotKeyFailed")
                : LocalizationService.Format(
                    "Status_HotKeyRollback",
                    requested.Display(),
                    previous?.Display() ?? LocalizationService.Get("Common_None"));
    }

    /// <summary>启用或禁用窗口规则；禁用前由控制器恢复其隐藏窗口。</summary>
    [RelayCommand]
    private void ToggleWindowRuleEnabled(WindowRuleVm? vm)
    {
        if (vm == null) return;

        bool requested = !vm.IsEnabled;
        bool applied = _controller.SetWindowRuleEnabled(vm.Model, requested);
        vm.RefreshEnabled();
        StatusMessage = applied
            ? null
            : _controller.WindowOperationStatusMessage
                ?? (requested
                    ? LocalizationService.Get("Status_EnableWindowRuleFailed")
                    : LocalizationService.Get("Status_DisableWindowRuleFailed"));
    }

    [RelayCommand]
    private void RestoreAll() => _controller.ShowAllHidden();

    [RelayCommand]
    private void HideWindow()
    {
        var dlg = new HideWindowDialog(_controller) { Owner = OwnerWindow() };
        if (dlg.ShowDialog() == true)
            _controller.HideSpecificWindows(dlg.Result);
    }

    // ---- 恢复全部快捷键 ----
    public string PanicHotKeyText => _controller.Config.PanicHotKey?.Display()
        ?? LocalizationService.Get("Common_None");

    [RelayCommand]
    private void SetPanicHotKey()
    {
        var dlg = new HotKeyCaptureDialog { Owner = OwnerWindow() };
        if (dlg.ShowDialog() == true)
        {
            var requested = dlg.Result;
            var previous = _controller.Config.PanicHotKey;
            bool applied = _controller.SetPanicHotKey(requested);
            OnPropertyChanged(nameof(PanicHotKeyText));
            StatusMessage = applied
                ? null
                : LocalizationService.Format(
                    "Status_GlobalHotKeyRollback",
                    requested?.Display() ?? LocalizationService.Get("Common_None"),
                    previous?.Display() ?? LocalizationService.Get("Common_None"));
        }
    }

    // ---- 恢复最近隐藏窗口 ----
    public string ShowLastHotKeyText => _controller.Config.ShowLastHotKey?.Display()
        ?? LocalizationService.Get("Common_None");

    [RelayCommand]
    private void SetShowLastHotKey()
    {
        var dlg = new HotKeyCaptureDialog { Owner = OwnerWindow() };
        if (dlg.ShowDialog() == true)
        {
            var requested = dlg.Result;
            var previous = _controller.Config.ShowLastHotKey;
            bool applied = _controller.SetShowLastHotKey(requested);
            OnPropertyChanged(nameof(ShowLastHotKeyText));
            StatusMessage = applied
                ? null
                : LocalizationService.Format(
                    "Status_GlobalHotKeyRollback",
                    requested?.Display() ?? LocalizationService.Get("Common_None"),
                    previous?.Display() ?? LocalizationService.Get("Common_None"));
        }
    }

    // ---- 桌面/开始菜单快捷方式 ----
    [RelayCommand]
    private void AddDesktopShortcut() =>
        StatusMessage = ShortcutService.CreateDesktopShortcut()
            ? LocalizationService.Get("Status_DesktopShortcutCreated")
            : LocalizationService.Get("Status_DesktopShortcutFailed");

    [RelayCommand]
    private void AddStartMenuShortcut() =>
        StatusMessage = ShortcutService.CreateStartMenuShortcut()
            ? LocalizationService.Get("Status_StartMenuShortcutCreated")
            : LocalizationService.Get("Status_StartMenuShortcutFailed");

    private static Window? OwnerWindow() =>
        Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
}
