using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Threading;
using HideIt.Models;
using HideIt.Services;

namespace HideIt;

/// <summary>一个当前打开的真实顶层窗口，既用于会话选择器，也用于生成持久窗口规则。</summary>
public sealed record OpenWindow(
    IntPtr Hwnd,
    string Title,
    string ProcessName,
    string? ExePath,
    string WindowClassName,
    ImageSource? Icon);

/// <summary>
/// 应用控制中枢：持有运行时配置，协调全局快捷键、窗口隐藏和动态窗口扫描。
/// 动态扫描使用 UI DispatcherTimer，但不依赖设置窗口本身，因此设置窗口关闭后仍能驻留托盘并维持控制能力。
/// </summary>
public sealed class AppController : IDisposable
{
    public ConfigStore ConfigStore { get; } = new();
    public RecoveryStore RecoveryStore { get; } = new();
    public WindowHider Hider { get; } = new();
    public HotKeyService HotKeys { get; } = new();
    public StartupRegistry Startup { get; } = new();
    public ProcessCatalog Catalog { get; } = new();

    public AppConfig Config { get; private set; } = new();

    /// <summary>读取当前用户注册表中的真实开机启动状态，避免界面只依赖可能过期的 JSON 字段。</summary>
    public bool IsRunAtStartupEnabled => Startup.IsEnabled();

    /// <summary>配置的恢复全部快捷键；为 null 时关闭该兜底操作。</summary>
    private HotKeyCombo? PanicCombo => Config.PanicHotKey;

    /// <summary>配置的恢复最近窗口快捷键；为 null 时关闭该快捷键。</summary>
    private HotKeyCombo? ShowLastCombo => Config.ShowLastHotKey;

    /// <summary>上次应用配置时被其他程序占用、因此未注册成功的快捷键。</summary>
    private readonly HashSet<HotKeyCombo> _failedCombos = new();

    /// <summary>以较低频率检查新建窗口，避免持续占用 UI 线程和重复枚举进程。</summary>
    private const int DynamicWindowScanIntervalMilliseconds = 500;

    /// <summary>负责在设置窗口隐藏后仍持续发现目标程序的新窗口。</summary>
    private readonly DispatcherTimer _dynamicWindowTimer;

    /// <summary>当前没有匹配窗口时给设置界面展示的轻量提示；相同内容不重复通知 UI。</summary>
    private string? _dynamicWindowStatusMessage;

    /// <summary>供设置界面展示启动时已发现的快捷键冲突，不暴露可修改的内部集合。</summary>
    public IReadOnlyCollection<HotKeyCombo> FailedHotKeys => _failedCombos;

    /// <summary>
    /// 只绑定当前窗口句柄的会话快捷键；窗口关闭或工具重启后失效，避免把 HWND 当作持久配置。
    /// </summary>
    private readonly Dictionary<HotKeyCombo, List<IntPtr>> _tempWindowBindings = new();

    /// <summary>快捷键注册失败时在 UI 线程通知设置界面。</summary>
    public event Action<HotKeyCombo>? HotKeyRegistrationFailed;

    /// <summary>配置保存并重新应用后通知设置界面刷新。</summary>
    public event Action? ConfigChanged;

    /// <summary>动态窗口状态发生变化时通知设置界面；该事件只承载信息提示，不表示错误。</summary>
    public event Action<string?>? DynamicWindowStatusChanged;

    /// <summary>窗口因权限或兼容性无法稳定操作时通知设置界面展示非弹窗提示。</summary>
    public event Action<string?>? WindowOperationStatusChanged;

    /// <summary>当前动态窗口扫描状态，供设置窗口首次打开时读取。</summary>
    public string? DynamicWindowStatusMessage => _dynamicWindowStatusMessage;

    /// <summary>当前窗口操作状态，供设置窗口首次打开时读取。</summary>
    public string? WindowOperationStatusMessage => _windowOperationStatusMessage;

    /// <summary>上次失败的窗口操作提示；相同提示不重复刷新界面。</summary>
    private string? _windowOperationStatusMessage;

    /// <summary>启动恢复阶段加载的一次性会话窗口快照，处理后不再作为永久规则保存。</summary>
    private readonly List<RecoveryWindow> _recoveryIndividualWindows = new();

    public AppController()
    {
        HotKeys.Pressed += OnHotKeyPressed;
        HotKeys.RegistrationFailed += OnRegistrationFailed;
        Hider.OperationFailed += OnWindowOperationFailed;
        LocalizationService.LanguageChanged += OnLanguageChanged;

        // 使用 DispatcherTimer 保证 Win32 窗口操作与快捷键回调在同一 UI 线程执行，
        // 500ms 的间隔足以覆盖窗口重建，同时避免对桌面窗口进行高频轮询。
        _dynamicWindowTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(DynamicWindowScanIntervalMilliseconds),
        };
        _dynamicWindowTimer.Tick += (_, _) => ScanDynamicWindows();
    }

    public void Load()
    {
        Config = ConfigStore.Load();
        // 旧配置或手工编辑的语言值不影响启动，统一回退到支持的语言标识。
        Config.Language = LocalizationService.Normalize(Config.Language);
        RestoreRecoveryState();
        Reapply();
        _dynamicWindowTimer.Start();
        ScanDynamicWindows();
    }

    public void Save() => ConfigStore.Save(Config);

    /// <summary>
    /// 保存并应用界面语言；语言切换只影响界面文案，不重新注册快捷键或改变隐藏规则。
    /// </summary>
    public void SetLanguage(string language)
    {
        string normalized = LocalizationService.Normalize(language);
        if (Config.Language == normalized)
            return;

        Config.Language = normalized;
        Save();
        LocalizationService.Apply(normalized);
    }

    /// <summary>
    /// 将当前隐藏意图写入独立恢复文件；只保存配置 ID 和稳定匹配字段，不保存会失效的 HWND。
    /// </summary>
    private void SaveRecoveryState()
    {
        var state = new RecoveryState();
        foreach (var app in Config.Apps.Where(app => Hider.IsHidden(app.ProcessName)))
            state.HiddenAppIds.Add(app.Id);
        foreach (var rule in Config.WindowRules.Where(rule => Hider.IsRuleHidden(rule.Id)))
            state.HiddenWindowRuleIds.Add(rule.Id);
        state.IndividualWindows.AddRange(_recoveryIndividualWindows);

        try
        {
            if (state.HiddenAppIds.Count == 0 && state.HiddenWindowRuleIds.Count == 0
                && state.IndividualWindows.Count == 0)
            {
                RecoveryStore.Clear();
                return;
            }

            RecoveryStore.Save(state);
        }
        catch (Exception ex)
        {
            // 恢复文件写入失败不能阻止当前窗口操作；记录日志并保留托盘运行。
            Logger.LogException("保存异常退出恢复状态", ex);
        }
    }

    /// <summary>
    /// 启动时加载上次异常退出留下的隐藏意图，并按当前运行窗口尽力重建隐藏集合。
    /// </summary>
    private void RestoreRecoveryState()
    {
        var state = RecoveryStore.Load();

        foreach (var app in Config.Apps.Where(app =>
                     state.HiddenAppIds.Contains(app.Id, StringComparer.OrdinalIgnoreCase)))
            Hider.RestoreHiddenProcess(app.ProcessName);

        foreach (var rule in Config.WindowRules.Where(rule =>
                     state.HiddenWindowRuleIds.Contains(rule.Id, StringComparer.OrdinalIgnoreCase)))
        {
            Hider.RestoreHiddenRule(rule.Id);
            var matches = GetOpenWindows(includeIcons: false, includeHidden: true)
                .Where(window => MatchesRule(rule, window))
                .Select(window => window.Hwnd)
                .ToList();
            Hider.HideNewRuleWindows(rule.Id, matches, recoverExistingHidden: true);
        }

        // 会话级隐藏没有持久规则，只做一次稳定字段匹配；未找到的窗口不继续造成错误提示。
        _recoveryIndividualWindows.AddRange(state.IndividualWindows ?? new List<RecoveryWindow>());
        var openWindows = GetOpenWindows(includeIcons: false, includeHidden: true);
        foreach (var recoveryWindow in _recoveryIndividualWindows.ToList())
        {
            var match = openWindows.FirstOrDefault(window => MatchesRecoveryWindow(recoveryWindow, window));
            if (match != null && (Hider.HideWindow(match.Hwnd, recoverExistingHidden: true)
                || Hider.IsWindowHidden(match.Hwnd)))
                _recoveryIndividualWindows.Remove(recoveryWindow);
        }

        // 启动恢复完成后，后续崩溃仍由当前重建出的规则状态接管。
        SaveRecoveryState();
    }

    /// <summary>比较会话窗口恢复快照；路径和类名优先，标题仅作为不区分大小写的弱条件。</summary>
    private static bool MatchesRecoveryWindow(RecoveryWindow recovery, OpenWindow window)
    {
        if (!string.Equals(recovery.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(recovery.ExePath)
            && !string.Equals(recovery.ExePath, window.ExePath, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrWhiteSpace(recovery.WindowClassName)
            && !string.Equals(recovery.WindowClassName, window.WindowClassName, StringComparison.Ordinal))
            return false;
        return string.IsNullOrWhiteSpace(recovery.TitleContains)
            || window.Title.Contains(recovery.TitleContains, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 事务式切换开机后台驻留：先更新注册表，成功后才同步 JSON 配置，避免界面与系统状态分离。
    /// </summary>
    public bool TrySetRunAtStartup(bool enabled)
    {
        if (!Startup.SetEnabled(enabled))
            return false;

        bool previous = Config.RunAtStartup;
        Config.RunAtStartup = enabled;
        try
        {
            Save();
            return true;
        }
        catch (Exception ex)
        {
            // 配置文件保存失败时回滚注册表，避免开机行为和持久配置指向不同状态。
            Logger.LogException("保存开机启动配置", ex);
            Config.RunAtStartup = previous;
            Startup.SetEnabled(previous);
            return false;
        }
    }

    /// <summary>重新应用当前快捷键集合并持久化配置；返回值表示所有组合是否注册成功。</summary>
    public bool SaveAndReapply()
    {
        Save();
        bool applied = Reapply();
        ConfigChanged?.Invoke();
        return applied;
    }

    /// <summary>
    /// 事务式更新程序规则快捷键：先试注册新组合，失败时恢复旧值，保证旧快捷键继续有效。
    /// 成功后才保存配置并通知设置界面刷新。
    /// </summary>
    public bool TrySetAppHotKey(AppEntry entry, HotKeyCombo? combo)
    {
        var previous = entry.HotKey;
        entry.HotKey = combo;
        if (Reapply())
        {
            Save();
            ConfigChanged?.Invoke();
            return true;
        }

        entry.HotKey = previous;
        Reapply();
        return false;
    }

    /// <summary>只为启用规则及全局兜底操作注册快捷键，禁用规则保留配置但不参与运行。</summary>
    private bool Reapply()
    {
        _failedCombos.Clear();
        var combos = Config.Apps
            .Where(a => a.Enabled && a.HotKey != null)
            .Select(a => a.HotKey!);
        combos = combos.Concat(Config.WindowRules
            .Where(r => r.Enabled && r.HotKey != null)
            .Select(r => r.HotKey!));
        if (PanicCombo != null)
            combos = combos.Append(PanicCombo);
        if (ShowLastCombo != null)
            combos = combos.Append(ShowLastCombo);
        combos = combos.Concat(_tempWindowBindings.Keys);
        return HotKeys.RegisterAll(combos);
    }

    private void OnRegistrationFailed(HotKeyCombo combo)
    {
        _failedCombos.Add(combo);
        HotKeyRegistrationFailed?.Invoke(combo);
    }

    /// <summary>接收窗口层的非致命失败提示；不弹窗、不结束目标进程，只更新设置界面状态。</summary>
    private void OnWindowOperationFailed(string message)
    {
        if (string.Equals(_windowOperationStatusMessage, message, StringComparison.Ordinal)) return;
        _windowOperationStatusMessage = message;
        WindowOperationStatusChanged?.Invoke(message);
    }

    // ---- 全局快捷键处理 ----
    /// <summary>按优先级处理恢复全部、会话绑定、恢复最近窗口、程序规则和窗口规则。</summary>
    private void OnHotKeyPressed(HotKeyCombo combo)
    {
        // 恢复全部是独立兜底动作，即使用户把同一组合键绑定到程序，也不能失去恢复入口。
        if (PanicCombo != null && combo.Equals(PanicCombo))
        {
            ShowAllHidden();
            return;
        }

        // 会话绑定优先于程序级切换，避免同一窗口被两套规则重复处理。
        if (_tempWindowBindings.TryGetValue(combo, out var hwnds))
        {
            ToggleSpecificWindows(combo, hwnds);
            return;
        }

        // 恢复最近窗口快捷键只有在未被程序配置占用时才执行，保持上游基线语义。
        if (ShowLastCombo != null && combo.Equals(ShowLastCombo) && !IsAssignedToRule(combo))
        {
            IntPtr? lastHiddenWindow = Hider.LastIndividualHiddenWindow;
            Hider.ShowLastWindow();
            if (lastHiddenWindow is { } hwnd && !Hider.IsWindowHidden(hwnd))
                RemoveRecoverySnapshotFor(hwnd);
            SaveRecoveryState();
            return;
        }

        ToggleGroup(combo);
    }

    /// <summary>判断快捷键是否已分配给任意启用的持久规则。</summary>
    private bool IsAssignedToRule(HotKeyCombo combo) =>
        Config.Apps.Any(a => a.Enabled && combo.Equals(a.HotKey)) ||
        Config.WindowRules.Any(r => r.Enabled && combo.Equals(r.HotKey));

    /// <summary>
    /// 共享快捷键按统一目标组切换程序和窗口规则；只要组内有一个目标可见就整体隐藏，
    /// 只有全部目标处于隐藏意图时才整体恢复，避免混合绑定留下半隐藏状态。
    /// </summary>
    public void ToggleGroup(HotKeyCombo combo)
    {
        var apps = Config.Apps
            .Where(a => a.Enabled && combo.Equals(a.HotKey))
            .ToList();
        var rules = Config.WindowRules
            .Where(rule => rule.Enabled && combo.Equals(rule.HotKey))
            .ToList();
        if (apps.Count == 0 && rules.Count == 0) return;

        // 目标未启动时 Hider.Hide/HideRule 只记录隐藏意图，不访问不存在的窗口。
        bool allHidden = apps.All(app => Hider.IsHidden(app.ProcessName))
            && rules.All(rule => Hider.IsRuleHidden(rule.Id));
        foreach (var app in apps)
        {
            if (allHidden) Hider.Show(app.ProcessName);
            else Hider.Hide(app.ProcessName);
        }
        foreach (var rule in rules)
        {
            if (allHidden)
                Hider.ShowRule(rule.Id);
            else
                HideMatchingWindows(rule);
        }
        SaveRecoveryState();
    }

    /// <summary>
    /// 保留窗口规则专用调用入口；实际切换复用统一目标组语义，避免与程序规则状态分叉。
    /// </summary>
    public void ToggleWindowRules(HotKeyCombo combo) => ToggleGroup(combo);

    /// <summary>枚举并隐藏当前满足规则的窗口；热键和事务回滚共用同一匹配路径。</summary>
    private void HideMatchingWindows(WindowRule rule)
    {
        // 热键路径只需要稳定匹配字段，不提取图标，避免切换时重复读取大量 exe 资源。
        var matches = GetOpenWindows(includeIcons: false)
            .Where(window => MatchesRule(rule, window))
            .Select(window => window.Hwnd)
            .ToList();
        Hider.HideRule(rule.Id, matches);
    }

    /// <summary>
    /// 扫描当前真实顶层窗口，并把隐藏状态下后来出现的匹配窗口纳入集合。
    /// 程序重启后旧 HWND 会被 WindowHider 清理，但程序级/窗口级隐藏意图继续有效。
    /// </summary>
    private void ScanDynamicWindows()
    {
        try
        {
            // 先清掉已退出目标留下的句柄，再处理异常恢复留下的一次性会话快照。
            Hider.CleanupStaleWindows();
            bool recoveryChanged = RestorePendingIndividualWindows();
            if (recoveryChanged)
                SaveRecoveryState();

            var hiddenApps = Config.Apps
                .Where(a => a.Enabled && Hider.IsHidden(a.ProcessName))
                .ToList();
            var hiddenRules = Config.WindowRules
                .Where(r => r.Enabled && Hider.IsRuleHidden(r.Id))
                .ToList();

            // 没有处于隐藏状态的规则时不必枚举所有桌面窗口，降低常驻时的资源消耗。
            if (hiddenApps.Count == 0 && hiddenRules.Count == 0
                && _recoveryIndividualWindows.Count == 0)
            {
                SetDynamicWindowStatus(null);
                return;
            }

            // 一次扫描共享窗口快照，避免程序级规则和窗口级规则各自重复枚举桌面。
            var openWindows = GetOpenWindows(includeIcons: false);
            var waitingForMatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var unableToConfirm = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var app in hiddenApps)
            {
                var matches = openWindows
                    .Where(window => string.Equals(
                        app.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase))
                    .Select(window => window.Hwnd)
                    .ToList();
                Hider.HideNewWindows(app.ProcessName, matches);

                // 隐藏意图仍在但没有任何存活窗口时，给设置界面一个轻量状态提示。
                if (!Hider.HasLiveHiddenWindows(app.ProcessName))
                    waitingForMatch.Add(string.IsNullOrWhiteSpace(app.DisplayName)
                        ? app.ProcessName
                        : app.DisplayName);
            }

            foreach (var rule in hiddenRules)
            {
                var candidates = openWindows
                    .Where(window => string.Equals(
                        rule.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // 路径或窗口类名读取失败时无法确认匹配，保持窗口可见并在设置界面提示用户。
                if ((!string.IsNullOrWhiteSpace(rule.ExePath) &&
                     candidates.Any(window => string.IsNullOrWhiteSpace(window.ExePath))) ||
                    (!string.IsNullOrWhiteSpace(rule.WindowClassName) &&
                     candidates.Any(window => string.IsNullOrWhiteSpace(window.WindowClassName))))
                {
                    unableToConfirm.Add(string.IsNullOrWhiteSpace(rule.DisplayName)
                        ? rule.ProcessName
                        : rule.DisplayName);
                }

                var matches = candidates
                    .Where(window => MatchesRule(rule, window))
                    .Select(window => window.Hwnd)
                    .ToList();
                Hider.HideNewRuleWindows(rule.Id, matches);

                if (!Hider.HasLiveRuleWindows(rule.Id))
                    waitingForMatch.Add(string.IsNullOrWhiteSpace(rule.DisplayName)
                        ? rule.ProcessName
                        : rule.DisplayName);
            }

            var statusParts = new List<string>();
            if (waitingForMatch.Count > 0)
                statusParts.Add(LocalizationService.Format(
                    "Status_NoMatchingWindows",
                    string.Join(LocalizationService.Get("Common_ListSeparator"), waitingForMatch)));
            if (unableToConfirm.Count > 0)
                statusParts.Add(LocalizationService.Format(
                    "Status_UnconfirmedWindows",
                    string.Join(LocalizationService.Get("Common_ListSeparator"), unableToConfirm)));

            var status = statusParts.Count == 0 ? null : string.Join(" ", statusParts);
            SetDynamicWindowStatus(status);
        }
        catch (Exception ex)
        {
            // 动态扫描失败不能影响托盘、快捷键或目标程序；记录日志并提示用户不要依赖本轮自动隐藏。
            Logger.LogException("动态窗口扫描", ex);
            SetDynamicWindowStatus(LocalizationService.Get("Status_DynamicScanFailed"));
        }
    }

    /// <summary>
    /// 尝试处理异常退出留下的会话窗口快照；目标尚未退出但窗口暂时未出现时保留快照等待后续扫描。
    /// </summary>
    private bool RestorePendingIndividualWindows()
    {
        if (_recoveryIndividualWindows.Count == 0)
            return false;

        var openWindows = GetOpenWindows(includeIcons: false, includeHidden: true);
        bool changed = false;
        foreach (var recoveryWindow in _recoveryIndividualWindows.ToList())
        {
            bool processExists;
            try
            {
                processExists = Process.GetProcessesByName(recoveryWindow.ProcessName).Length > 0;
            }
            catch
            {
                // 进程枚举失败时保留快照，避免一次权限抖动误删恢复机会。
                processExists = true;
            }

            if (!processExists)
            {
                _recoveryIndividualWindows.Remove(recoveryWindow);
                changed = true;
                continue;
            }

            var match = openWindows.FirstOrDefault(window => MatchesRecoveryWindow(recoveryWindow, window));
            if (match != null && (Hider.HideWindow(match.Hwnd, recoverExistingHidden: true)
                || Hider.IsWindowHidden(match.Hwnd)))
            {
                _recoveryIndividualWindows.Remove(recoveryWindow);
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>资源字典切换后清理旧语言的暂态提示，下一次扫描会用新语言重新生成状态。</summary>
    private void OnLanguageChanged()
    {
        _dynamicWindowStatusMessage = null;
        _windowOperationStatusMessage = null;
        DynamicWindowStatusChanged?.Invoke(null);
        WindowOperationStatusChanged?.Invoke(null);
    }

    /// <summary>仅在动态状态文字发生变化时通知 UI，避免计时器每次触发都刷新界面。</summary>
    private void SetDynamicWindowStatus(string? message)
    {
        if (string.Equals(_dynamicWindowStatusMessage, message, StringComparison.Ordinal)) return;

        _dynamicWindowStatusMessage = message;
        DynamicWindowStatusChanged?.Invoke(message);
    }

    /// <summary>判断当前窗口是否满足 exe、进程、类名和可选标题包含条件。</summary>
    private static bool MatchesRule(WindowRule rule, OpenWindow window)
    {
        if (!string.Equals(rule.ProcessName, window.ProcessName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.ExePath) &&
            !string.Equals(rule.ExePath, window.ExePath, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(rule.WindowClassName) &&
            !string.Equals(rule.WindowClassName, window.WindowClassName, StringComparison.Ordinal))
            return false;

        return string.IsNullOrWhiteSpace(rule.TitleContains) ||
            window.Title.Contains(rule.TitleContains, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>展示所有当前隐藏的受控窗口，成功后清除异常退出恢复标记。</summary>
    public void ShowAllHidden()
    {
        bool restored = Hider.ShowAll();
        if (restored)
            _recoveryIndividualWindows.Clear();
        SaveRecoveryState();
    }

    /// <summary>
    /// 删除受控程序前先展示其隐藏窗口；若快捷键重新应用失败，则恢复规则和原隐藏状态。
    /// </summary>
    public bool RemoveApp(AppEntry entry)
    {
        int index = Config.Apps.IndexOf(entry);
        if (index < 0) return true;

        bool wasHidden = Hider.IsHidden(entry.ProcessName);
        if (wasHidden && !Hider.Show(entry.ProcessName))
            return false;

        Config.Apps.RemoveAt(index);
        if (Reapply())
        {
            Save();
            ConfigChanged?.Invoke();
            SaveRecoveryState();
            return true;
        }

        Config.Apps.Insert(index, entry);
        Reapply();
        if (wasHidden)
            Hider.Hide(entry.ProcessName);
        SaveRecoveryState();
        return false;
    }

    /// <summary>
    /// 删除窗口规则前先恢复该规则隐藏的窗口；快捷键应用失败时恢复原配置。
    /// </summary>
    public bool RemoveWindowRule(WindowRule rule)
    {
        int index = Config.WindowRules.IndexOf(rule);
        if (index < 0) return true;

        bool wasHidden = Hider.IsRuleHidden(rule.Id);
        var hiddenHandles = Hider.GetRuleHiddenWindowHandles(rule.Id);
        if (wasHidden && !Hider.ShowRule(rule.Id))
            return false;
        Config.WindowRules.RemoveAt(index);
        if (Reapply())
        {
            Save();
            ConfigChanged?.Invoke();
            SaveRecoveryState();
            return true;
        }

        Config.WindowRules.Insert(index, rule);
        Reapply();
        if (wasHidden)
            Hider.HideRule(rule.Id, hiddenHandles);
        SaveRecoveryState();
        return false;
    }

    /// <summary>事务式更新窗口规则快捷键，冲突时保留原快捷键。</summary>
    public bool TrySetWindowRuleHotKey(WindowRule rule, HotKeyCombo? combo)
    {
        var previous = rule.HotKey;
        rule.HotKey = combo;
        if (Reapply())
        {
            Save();
            ConfigChanged?.Invoke();
            SaveRecoveryState();
            return true;
        }

        rule.HotKey = previous;
        Reapply();
        return false;
    }

    /// <summary>切换窗口规则启用状态；禁用前先恢复该规则隐藏的窗口。</summary>
    public bool SetWindowRuleEnabled(WindowRule rule, bool enabled)
    {
        if (rule.Enabled == enabled) return true;

        bool wasHidden = !enabled && Hider.IsRuleHidden(rule.Id);
        var hiddenHandles = wasHidden
            ? Hider.GetRuleHiddenWindowHandles(rule.Id)
            : Array.Empty<IntPtr>();
        if (!enabled && wasHidden && !Hider.ShowRule(rule.Id))
            return false;

        bool previous = rule.Enabled;
        rule.Enabled = enabled;
        if (Reapply())
        {
            Save();
            ConfigChanged?.Invoke();
            SaveRecoveryState();
            return true;
        }

        rule.Enabled = previous;
        Reapply();
        if (wasHidden)
            Hider.HideRule(rule.Id, hiddenHandles);
        SaveRecoveryState();
        return false;
    }

    /// <summary>
    /// 切换规则启用状态；禁用前先恢复该规则隐藏的窗口，失败时恢复原状态和快捷键注册。
    /// </summary>
    public bool SetAppEnabled(AppEntry entry, bool enabled)
    {
        if (entry.Enabled == enabled) return true;

        bool wasHidden = !enabled && Hider.IsHidden(entry.ProcessName);
        if (wasHidden && !Hider.Show(entry.ProcessName))
            return false;

        bool previous = entry.Enabled;
        entry.Enabled = enabled;
        if (Reapply())
        {
            Save();
            ConfigChanged?.Invoke();
            SaveRecoveryState();
            return true;
        }

        entry.Enabled = previous;
        Reapply();
        if (wasHidden)
            Hider.Hide(entry.ProcessName);
        SaveRecoveryState();
        return false;
    }

    /// <summary>事务式更新恢复全部快捷键；冲突时保留旧快捷键并返回 false。</summary>
    public bool SetPanicHotKey(HotKeyCombo? combo)
    {
        var previous = Config.PanicHotKey;
        Config.PanicHotKey = combo;
        if (Reapply())
        {
            Save();
            ConfigChanged?.Invoke();
            return true;
        }

        Config.PanicHotKey = previous;
        Reapply();
        return false;
    }

    /// <summary>事务式更新恢复最近窗口快捷键；冲突时保留旧快捷键并返回 false。</summary>
    public bool SetShowLastHotKey(HotKeyCombo? combo)
    {
        var previous = Config.ShowLastHotKey;
        Config.ShowLastHotKey = combo;
        if (Reapply())
        {
            Save();
            ConfigChanged?.Invoke();
            return true;
        }

        Config.ShowLastHotKey = previous;
        Reapply();
        return false;
    }

    // ---- 隐藏指定窗口（窗口选择器） ----

    /// <summary>返回所有可供窗口选择器使用的真实顶层窗口，并包含图标。</summary>
    public List<OpenWindow> GetOpenWindows() => GetOpenWindows(includeIcons: true);

    /// <summary>枚举当前窗口并提取匹配字段；图标仅在设置窗口展示时读取。</summary>
    private List<OpenWindow> GetOpenWindows(bool includeIcons, bool includeHidden = false)
    {
        uint selfPid = (uint)Environment.ProcessId;
        var list = new List<OpenWindow>();

        foreach (var w in Native.GetAllRealWindows(includeHidden))
        {
            if (w.Pid == selfPid) continue;

            string procName = "";
            string? exe = null;
            try
            {
                using var p = Process.GetProcessById((int)w.Pid);
                procName = p.ProcessName;
                try { exe = p.MainModule?.FileName; } catch { /* denied / bitness */ }
            }
            catch { /* process vanished */ }

            var title = string.IsNullOrWhiteSpace(w.Title) ? procName : w.Title;
            list.Add(new OpenWindow(
                w.Hwnd,
                title,
                procName,
                exe,
                Native.GetWindowClassName(w.Hwnd),
                includeIcons ? Catalog.GetIconFor(exe) : null));
        }

        return list
            .OrderBy(x => x.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>按句柄隐藏指定窗口，并为异常重启保留稳定匹配快照。</summary>
    public void HideSpecificWindows(IEnumerable<IntPtr> handles)
    {
        var openWindows = GetOpenWindows(includeIcons: false)
            .ToDictionary(window => window.Hwnd);
        foreach (var h in handles)
        {
            if (Hider.HideWindow(h) && openWindows.TryGetValue(h, out var window))
            {
                _recoveryIndividualWindows.Add(new RecoveryWindow
                {
                    ProcessName = window.ProcessName,
                    ExePath = window.ExePath,
                    WindowClassName = window.WindowClassName,
                    TitleContains = string.IsNullOrWhiteSpace(window.Title) ? null : window.Title,
                });
            }
        }
        SaveRecoveryState();
    }

    /// <summary>从恢复快照中移除已经成功展示的会话窗口，避免下一次启动重复匹配。</summary>
    private void RemoveRecoverySnapshotFor(IntPtr hwnd)
    {
        var window = GetOpenWindows(includeIcons: false).FirstOrDefault(candidate => candidate.Hwnd == hwnd);
        if (window == null) return;

        // 多个同类窗口可能拥有相同快照，只移除一个对应项，保留其余窗口的恢复机会。
        int index = _recoveryIndividualWindows.FindIndex(recovery => MatchesRecoveryWindow(
            recovery,
            window));
        if (index >= 0)
            _recoveryIndividualWindows.RemoveAt(index);
    }

    /// <summary>
    /// 为指定窗口绑定仅当前会话有效的快捷键；若组合键已被全局占用则返回 false。
    /// 绑定前先移除这些窗口上的旧会话快捷键，保证一个窗口同时只有一个临时入口。
    /// </summary>
    public bool AddTempWindowBinding(HotKeyCombo combo, IReadOnlyList<IntPtr> handles)
    {
        // 先复制会话绑定，冲突时才能恢复原有窗口快捷键而不破坏当前控制入口。
        var previous = _tempWindowBindings.ToDictionary(
            pair => pair.Key, pair => pair.Value.ToList());
        foreach (var h in handles)
            RemoveTempBindingFor(h);

        _tempWindowBindings[combo] = handles.ToList();
        if (Reapply()) return true;

        _tempWindowBindings.Clear();
        foreach (var pair in previous)
            _tempWindowBindings[pair.Key] = pair.Value;
        Reapply();
        return false;
    }

    /// <summary>返回窗口当前绑定的会话快捷键；没有绑定时返回 null。</summary>
    public HotKeyCombo? GetTempBindingFor(IntPtr hwnd) =>
        _tempWindowBindings.FirstOrDefault(kv => kv.Value.Contains(hwnd)).Key;

    /// <summary>从所有会话绑定中移除窗口句柄，并清理因此变空的快捷键。</summary>
    private void RemoveTempBindingFor(IntPtr hwnd)
    {
        foreach (var combo in _tempWindowBindings.Keys.ToList())
        {
            var list = _tempWindowBindings[combo];
            if (list.Remove(hwnd) && list.Count == 0)
                _tempWindowBindings.Remove(combo);
        }
    }

    /// <summary>按组切换一组指定窗口；所有窗口关闭后清理该会话绑定。</summary>
    private void ToggleSpecificWindows(HotKeyCombo combo, List<IntPtr> handles)
    {
        var alive = handles.Where(Native.IsWindow).ToList();
        if (alive.Count == 0)
        {
            // 窗口全部关闭时移除会话绑定；冲突回滚时仍要保留原快捷键入口。
            var previous = _tempWindowBindings[combo].ToList();
            _tempWindowBindings.Remove(combo);
            if (!Reapply())
            {
                _tempWindowBindings[combo] = previous;
                Reapply();
            }
            return;
        }

        bool allHidden = alive.All(Hider.IsWindowHidden);
        var openWindows = GetOpenWindows(includeIcons: false)
            .ToDictionary(window => window.Hwnd);
        foreach (var h in alive)
        {
            if (allHidden)
            {
                Hider.ShowSpecificWindow(h);
                if (!Hider.IsWindowHidden(h))
                    RemoveRecoverySnapshotFor(h);
            }
            else
            {
                if (Hider.HideWindow(h))
                {
                    if (openWindows.TryGetValue(h, out var window))
                    {
                        _recoveryIndividualWindows.Add(new RecoveryWindow
                        {
                            ProcessName = window.ProcessName,
                            ExePath = window.ExePath,
                            WindowClassName = window.WindowClassName,
                            TitleContains = string.IsNullOrWhiteSpace(window.Title) ? null : window.Title,
                        });
                    }
                }
            }
        }
        SaveRecoveryState();
    }

    public void Dispose()
    {
        // 先停止动态扫描，再注销快捷键和恢复窗口，避免退出过程中又纳入新窗口。
        _dynamicWindowTimer.Stop();
        LocalizationService.LanguageChanged -= OnLanguageChanged;
        HotKeys.Dispose();
        bool restored = Hider.ShowAll();
        if (restored)
            RecoveryStore.Clear();
        else
            SaveRecoveryState();
    }
}
