using System.Diagnostics;

namespace HideIt.Services;

/// <summary>运行时隐藏窗口记录：保存句柄、待恢复的扩展样式、所属进程 ID 和隐藏前焦点。</summary>
public sealed record HiddenWin(IntPtr Hwnd, long OriginalExStyle, uint Pid)
{
    /// <summary>只有隐藏前该窗口处于前台时才记录，避免恢复时无条件抢占其他窗口焦点。</summary>
    public IntPtr PreviousForeground { get; init; }
}

/// <summary>
/// 按程序级和窗口级规则隐藏/展示真实顶层窗口。恢复使用缓存句柄，
/// 因为隐藏窗口不会再次通过可见窗口枚举；隐藏只改变窗口桌面可见性，不改变目标进程后台行为。
/// </summary>
public sealed class WindowHider
{
    private readonly Dictionary<string, List<HiddenWin>> _hidden =
        new(StringComparer.OrdinalIgnoreCase);

    // 持久窗口规则按规则 ID 保存当前隐藏句柄；规则本身仍保存在配置中，句柄只存在运行时。
    private readonly Dictionary<string, List<HiddenWin>> _ruleHidden =
        new(StringComparer.OrdinalIgnoreCase);

    // 会话级单独隐藏的窗口，按隐藏时间顺序保存，最后一个是最近隐藏的窗口。
    private readonly List<HiddenWin> _individual = new();

    /// <summary>窗口因权限、保护机制或兼容性原因无法稳定操作时通知上层展示提示。</summary>
    public event Action<string>? OperationFailed;

    /// <summary>判断程序级规则是否处于“隐藏意图”状态；即使目标暂时没有窗口也要保持为 true。</summary>
    public bool IsHidden(string processName) => _hidden.ContainsKey(processName);

    public bool HasIndividuallyHidden
    {
        get
        {
            RemoveStaleWindows(_individual);
            return _individual.Count > 0;
        }
    }

    /// <summary>返回最近一次会话隐藏窗口的句柄；仅供当前进程内的快捷键恢复使用。</summary>
    public IntPtr? LastIndividualHiddenWindow
    {
        get
        {
            RemoveStaleWindows(_individual);
            return _individual.Count == 0 ? null : _individual[^1].Hwnd;
        }
    }

    /// <summary>判断会话窗口是否仍被隐藏；查询前清理已退出或被复用的句柄。</summary>
    public bool IsWindowHidden(IntPtr hwnd)
    {
        RemoveStaleWindows(_individual);
        return _individual.Any(h => h.Hwnd == hwnd && IsLiveWindow(h));
    }

    /// <summary>清理退出目标留下的失效记录，并返回当前是否仍有可恢复的隐藏窗口。</summary>
    public bool HasHiddenWindows
    {
        get
        {
            CleanupStaleWindows();
            return _hidden.Values.Any(list => list.Count > 0)
                || _ruleHidden.Values.Any(list => list.Count > 0)
                || _individual.Count > 0;
        }
    }

    /// <summary>清理失效句柄后，判断程序级规则当前是否仍有真实隐藏窗口。</summary>
    public bool HasLiveHiddenWindows(string processName)
    {
        if (!_hidden.TryGetValue(processName, out var list)) return false;
        RemoveStaleWindows(list);
        return list.Count > 0;
    }

    /// <summary>判断某个持久窗口规则是否仍处于隐藏意图状态，目标窗口暂时不存在时也返回 true。</summary>
    public bool IsRuleHidden(string ruleId)
    {
        if (!_ruleHidden.TryGetValue(ruleId, out var list)) return false;

        // 目标程序重启后旧 HWND 会失效，但规则的隐藏意图必须保留，供新窗口自动重新匹配。
        // 句柄可能被 Windows 复用；同时校验 PID，避免误操作已属于其他进程的新窗口。
        RemoveStaleWindows(list);
        return true;
    }

    /// <summary>清理失效句柄后，判断窗口规则当前是否仍有真实隐藏窗口。</summary>
    public bool HasLiveRuleWindows(string ruleId)
    {
        if (!_ruleHidden.TryGetValue(ruleId, out var list)) return false;
        RemoveStaleWindows(list);
        return list.Count > 0;
    }

    /// <summary>读取程序级规则当前持有的窗口句柄，供配置变更失败时恢复原有所有权。</summary>
    public IReadOnlyList<IntPtr> GetHiddenWindowHandles(string processName)
    {
        if (!_hidden.TryGetValue(processName, out var list)) return Array.Empty<IntPtr>();
        RemoveStaleWindows(list);
        return list.Select(window => window.Hwnd).Distinct().ToList();
    }

    /// <summary>读取窗口级规则当前持有的窗口句柄，供配置变更失败时恢复原有所有权。</summary>
    public IReadOnlyList<IntPtr> GetRuleHiddenWindowHandles(string ruleId)
    {
        if (!_ruleHidden.TryGetValue(ruleId, out var list)) return Array.Empty<IntPtr>();
        RemoveStaleWindows(list);
        return list.Select(window => window.Hwnd).Distinct().ToList();
    }

    /// <summary>返回当前隐藏集合，并在恢复或退出前先清理已失效的窗口实例。</summary>
    private IEnumerable<HiddenWin> AllHidden()
    {
        foreach (var list in _hidden.Values)
            RemoveStaleWindows(list);
        foreach (var list in _ruleHidden.Values)
            RemoveStaleWindows(list);
        RemoveStaleWindows(_individual);

        // 同一窗口可能被多个规则共同持有；恢复和退出时只处理一次实际窗口。
        return _hidden.Values.SelectMany(list => list)
            .Concat(_ruleHidden.Values.SelectMany(list => list))
            .Concat(_individual)
            .DistinctBy(window => (window.Hwnd, window.Pid));
    }

    /// <summary>集中清理程序退出、窗口关闭或 HWND 被 Windows 复用后留下的运行时记录。</summary>
    public void CleanupStaleWindows()
    {
        foreach (var list in _hidden.Values)
            RemoveStaleWindows(list);
        foreach (var list in _ruleHidden.Values)
            RemoveStaleWindows(list);
        RemoveStaleWindows(_individual);
    }

    /// <summary>恢复程序级隐藏意图；启动恢复时不保存旧 HWND，只从当前窗口重新建立运行时集合。</summary>
    public void RestoreHiddenProcess(string processName)
    {
        if (IsHidden(processName)) return;

        var list = new List<HiddenWin>();
        foreach (var hwnd in Native.GetTopLevelWindows(processName, includeHidden: true))
            AppendHiddenWindow(list, hwnd, recoverExistingHidden: true);
        _hidden[processName] = list;
    }

    /// <summary>恢复窗口规则的隐藏意图；当前窗口由动态扫描按持久匹配条件重新纳入。</summary>
    public void RestoreHiddenRule(string ruleId)
    {
        if (!_ruleHidden.ContainsKey(ruleId))
            _ruleHidden[ruleId] = new List<HiddenWin>();
    }

    /// <summary>恢复一个会话隐藏窗口并按隐藏前记录尝试交还焦点；若其他规则仍持有则继续保持隐藏。</summary>
    public void ShowSpecificWindow(IntPtr hwnd)
    {
        RemoveStaleWindows(_individual);
        int idx = _individual.FindIndex(h => h.Hwnd == hwnd);
        if (idx < 0) return;
        var h = _individual[idx];
        _individual.RemoveAt(idx);
        if (IsOwnedByOtherRule(h)) return;

        if (Restore(h))
            RestoreFocus(h);
        else
            _individual.Insert(idx, h);
    }

    /// <summary>按窗口句柄隐藏会话窗口；复用已有隐藏记录以避免重复修改原始样式。</summary>
    public bool HideWindow(IntPtr hwnd, bool recoverExistingHidden = false)
    {
        RemoveStaleWindows(_individual);
        if (_individual.Any(h => h.Hwnd == hwnd)) return false;
        return AppendHiddenWindow(_individual, hwnd, Native.GetForegroundWindow(), recoverExistingHidden);
    }

    /// <summary>隐藏当前匹配某条持久窗口规则的窗口，并记录运行时句柄。</summary>
    public void HideRule(string ruleId, IEnumerable<IntPtr> handles)
    {
        if (IsRuleHidden(ruleId)) return;

        var list = new List<HiddenWin>();
        IntPtr previousForeground = Native.GetForegroundWindow();
        foreach (var hwnd in handles.Distinct())
            AppendHiddenWindow(list, hwnd, previousForeground);
        // 即使当前没有匹配窗口也记录隐藏意图，目标程序启动或窗口重建后由动态扫描补齐。
        _ruleHidden[ruleId] = list;
    }

    /// <summary>
    /// 将新出现的程序级窗口纳入已有隐藏集合；调用方必须先确认该程序规则处于隐藏状态。
    /// 只接收真实顶层窗口，重复句柄不会重复记录。
    /// </summary>
    public void HideNewWindows(string processName, IEnumerable<IntPtr> handles)
    {
        if (!_hidden.TryGetValue(processName, out var hidden)) return;

        // 目标程序退出后清掉旧句柄，但不删除隐藏意图，以便重启后继续生效。
        RemoveStaleWindows(hidden);
        IntPtr previousForeground = Native.GetForegroundWindow();
        foreach (var hwnd in handles.Distinct())
        {
            if (!Native.IsWindow(hwnd)) continue;

            // 新窗口通常不在缓存中；共享已有记录时不重复修改窗口扩展样式。
            AppendHiddenWindow(hidden, hwnd, previousForeground);
        }
    }

    /// <summary>
    /// 将新出现且符合窗口级规则的窗口追加到隐藏集合；规则未处于隐藏状态时不执行任何操作。
    /// </summary>
    public void HideNewRuleWindows(
        string ruleId,
        IEnumerable<IntPtr> handles,
        bool recoverExistingHidden = false)
    {
        if (!_ruleHidden.TryGetValue(ruleId, out var hidden)) return;

        // 保留规则的隐藏意图，只清理已经退出的窗口实例。
        RemoveStaleWindows(hidden);
        IntPtr previousForeground = Native.GetForegroundWindow();
        foreach (var hwnd in handles.Distinct())
        {
            if (!Native.IsWindow(hwnd)) continue;

            // 同一窗口可能同时满足多次扫描，按共享隐藏记录去重，避免重复修改窗口样式。
            AppendHiddenWindow(hidden, hwnd, previousForeground, recoverExistingHidden);
        }
    }

    /// <summary>恢复某条持久窗口规则当前隐藏的窗口；失败时保留规则所有权以便重试。</summary>
    public bool ShowRule(string ruleId)
    {
        if (!_ruleHidden.TryGetValue(ruleId, out var list)) return true;

        // 先移除当前规则所有权，再判断窗口是否仍被其他规则持有。
        _ruleHidden.Remove(ruleId);
        HiddenWin? focus = null;
        var failed = new List<HiddenWin>();
        foreach (var w in list)
        {
            if (!IsOwnedByOtherRule(w))
            {
                if (Restore(w))
                {
                    if (focus == null && w.PreviousForeground != IntPtr.Zero)
                        focus = w;
                }
                else
                {
                    failed.Add(w);
                }
            }
        }
        if (failed.Count > 0)
            _ruleHidden[ruleId] = failed;
        if (focus != null)
            RestoreFocus(focus);
        return !_ruleHidden.ContainsKey(ruleId);
    }

    /// <summary>恢复最近一次会话隐藏窗口并尝试交还焦点；若其他规则仍持有则不提前展示。</summary>
    public void ShowLastWindow()
    {
        if (_individual.Count == 0) return;
        var h = _individual[^1];
        _individual.RemoveAt(_individual.Count - 1);
        if (IsOwnedByOtherRule(h)) return;

        if (Restore(h))
            RestoreFocus(h);
        else
            _individual.Add(h);
    }

    /// <summary>隐藏程序当前真实顶层窗口，并在没有窗口时保留后续自动纳入所需的隐藏意图。</summary>
    public void Hide(string processName)
    {
        if (IsHidden(processName)) return;

        var list = new List<HiddenWin>();
        IntPtr previousForeground = Native.GetForegroundWindow();
        foreach (var hwnd in Native.GetTopLevelWindows(processName))
            AppendHiddenWindow(list, hwnd, previousForeground);

        // 目标窗口可能仍被另一条规则隐藏而无法被可见窗口枚举发现，回滚时要重新挂回共享所有权。
        foreach (var hwnd in FindLiveHiddenWindowsForProcess(processName))
            AppendHiddenWindow(list, hwnd, previousForeground);

        // 没有窗口时也保留隐藏意图，后续新建的真实窗口才能自动纳入。
        _hidden[processName] = list;
    }

    /// <summary>展示程序已缓存的窗口；失败时保留程序所有权以便重试。</summary>
    public bool Show(string processName)
    {
        if (!_hidden.TryGetValue(processName, out var list)) return true;

        // 先移除当前程序所有权，再判断窗口是否仍被其他规则持有。
        _hidden.Remove(processName);
        HiddenWin? focus = null;
        var failed = new List<HiddenWin>();
        foreach (var w in list)
        {
            if (!IsOwnedByOtherRule(w))
            {
                if (Restore(w))
                {
                    if (focus == null && w.PreviousForeground != IntPtr.Zero)
                        focus = w;
                }
                else
                {
                    failed.Add(w);
                }
            }
        }

        if (focus != null)
            RestoreFocus(focus);
        if (failed.Count > 0)
            _hidden[processName] = failed;
        return !_hidden.ContainsKey(processName);
    }

    public void Toggle(string processName)
    {
        if (IsHidden(processName)) Show(processName);
        else Hide(processName);
    }

    /// <summary>恢复全部隐藏窗口；失败的窗口保留在控制集合中，供重试和异常恢复继续使用。</summary>
    public bool ShowAll()
    {
        // 复制去重后的快照后逐个恢复；恢复成功才移除所有权，避免失败后留下无控制入口的隐藏窗口。
        var all = AllHidden().ToList();
        HiddenWin? focus = null;
        foreach (var w in all)
        {
            if (!Restore(w)) continue;

            if (focus == null && w.PreviousForeground != IntPtr.Zero)
                focus = w;
            RemoveOwnership(w);
        }

        if (focus != null)
            RestoreFocus(focus);

        // 没有真实窗口的隐藏意图也属于“恢复全部”的范围；失败记录则因仍有元素而保留。
        foreach (var key in _hidden.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).ToList())
            _hidden.Remove(key);
        foreach (var key in _ruleHidden.Where(pair => pair.Value.Count == 0).Select(pair => pair.Key).ToList())
            _ruleHidden.Remove(key);

        return !HasHiddenWindows;
    }

    // ---- 共享的隐藏/恢复基础操作 ----
    /// <summary>确认缓存句柄仍属于原进程，防止目标重启后的句柄复用造成误恢复。</summary>
    private static bool IsLiveWindow(HiddenWin window) =>
        Native.IsWindow(window.Hwnd) && Native.GetWindowPid(window.Hwnd) == window.Pid;

    /// <summary>移除已经退出或被 Windows 复用的窗口，避免失效记录触发持续错误提示。</summary>
    private void RemoveStaleWindows(List<HiddenWin> windows)
    {
        foreach (var window in windows.Where(w => !IsLiveWindow(w)).ToList())
            windows.Remove(window);
    }

    /// <summary>
    /// 为窗口集合追加共享隐藏记录；已有规则持有同一窗口时只增加所有权，不再次修改样式。
    /// </summary>
    private bool AppendHiddenWindow(
        List<HiddenWin> owner,
        IntPtr hwnd,
        IntPtr previousForeground = default,
        bool recoverExistingHidden = false)
    {
        if (!Native.IsWindow(hwnd)) return false;

        uint pid = Native.GetWindowPid(hwnd);
        var existing = FindLiveHiddenWindow(hwnd, pid);
        if (existing != null)
        {
            // 规则仍要求隐藏时，目标程序自行再次展示旧窗口也要重新隐藏，但不能重复记录样式。
            if (Native.IsWindowVisible(hwnd))
            {
                if (!TryHideExistingWindow(existing))
                    return false;
            }
            if (!owner.Any(w => ReferenceEquals(w, existing)))
                owner.Add(existing);
            return true;
        }

        var hidden = HideOne(hwnd, previousForeground, recoverExistingHidden);
        if (hidden == null) return false;
        owner.Add(hidden);
        return true;
    }

    /// <summary>在程序级、窗口级和会话集合中查找仍属于同一 HWND/PID 的共享隐藏记录。</summary>
    private HiddenWin? FindLiveHiddenWindow(IntPtr hwnd, uint pid)
    {
        foreach (var window in _hidden.Values.SelectMany(list => list)
                     .Concat(_ruleHidden.Values.SelectMany(list => list))
                     .Concat(_individual))
        {
            if (window.Hwnd == hwnd && window.Pid == pid && IsLiveWindow(window))
                return window;
        }

        return null;
    }

    /// <summary>查找仍被其他规则隐藏且属于指定进程的窗口，避免事务回滚时丢失所有权。</summary>
    private IEnumerable<IntPtr> FindLiveHiddenWindowsForProcess(string processName)
    {
        var seen = new HashSet<IntPtr>();
        foreach (var window in _hidden.Values.SelectMany(list => list)
                     .Concat(_ruleHidden.Values.SelectMany(list => list))
                     .Concat(_individual))
        {
            if (!IsLiveWindow(window) || !seen.Add(window.Hwnd)) continue;

            bool matchesProcess = false;
            try
            {
                using var process = Process.GetProcessById((int)window.Pid);
                matchesProcess = string.Equals(
                    process.ProcessName,
                    processName,
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                // 进程刚退出或权限不足时跳过，动态扫描会在下次窗口出现后重新建立所有权。
            }

            // C# 不允许在含 catch 的 try 块中 yield，先记录匹配结果再返回窗口句柄。
            if (matchesProcess)
                yield return window.Hwnd;
        }
    }

    /// <summary>判断窗口是否仍由其他规则持有，持有期间不能恢复可见状态。</summary>
    private bool IsOwnedByOtherRule(HiddenWin window) =>
        _hidden.Values.SelectMany(list => list)
            .Concat(_ruleHidden.Values.SelectMany(list => list))
            .Concat(_individual)
            .Any(owner => ReferenceEquals(owner, window));

    /// <summary>对已有隐藏记录重新隐藏窗口；失败时恢复原始样式并把原因交给界面提示。</summary>
    private bool TryHideExistingWindow(HiddenWin window)
    {
        if (!CanControlProcess(window.Pid, window.Hwnd))
            return false;

        if (!Native.TryGetExStyle(window.Hwnd, out long currentStyle))
        {
            ReportFailure(LocalizationService.Get("WindowOperation_ReadStyleFailed"), window.Hwnd);
            return false;
        }

        long hiddenStyle = (currentStyle | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW;
        if (!Native.TrySetExStyle(window.Hwnd, hiddenStyle))
        {
            ReportFailure(LocalizationService.Get("WindowOperation_WriteStyleFailed"), window.Hwnd);
            return false;
        }

        Native.ShowWindow(window.Hwnd, Native.SW_HIDE);
        if (Native.IsWindowVisible(window.Hwnd))
        {
            Native.TrySetExStyle(window.Hwnd, window.OriginalExStyle);
            ReportFailure(LocalizationService.Get("WindowOperation_HideFailed"), window.Hwnd);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 读取权限并隐藏一个窗口；任何一步失败都只提示用户，不结束、暂停或改变目标进程。
    /// </summary>
    private HiddenWin? HideOne(
        IntPtr hwnd,
        IntPtr previousForeground,
        bool recoverExistingHidden = false)
    {
        if (!Native.IsWindow(hwnd)) return null;

        uint pid = Native.GetWindowPid(hwnd);
        if (!CanControlProcess(pid, hwnd))
            return null;

        if (!Native.TryGetExStyle(hwnd, out long ex))
        {
            ReportFailure(LocalizationService.Get("WindowOperation_ReadStyleFailed"), hwnd);
            return null;
        }

        if (recoverExistingHidden && !Native.IsWindowVisible(hwnd))
        {
            // 崩溃后原始样式已不可得；普通真实窗口隐藏前通常没有 TOOLWINDOW，按反向变换尽力恢复。
            long bestEffortOriginalStyle = (ex | Native.WS_EX_APPWINDOW) & ~Native.WS_EX_TOOLWINDOW;
            return new HiddenWin(hwnd, bestEffortOriginalStyle, pid);
        }

        long hiddenStyle = (ex | Native.WS_EX_TOOLWINDOW) & ~Native.WS_EX_APPWINDOW;
        if (!Native.TrySetExStyle(hwnd, hiddenStyle))
        {
            ReportFailure(LocalizationService.Get("WindowOperation_WriteStyleFailed"), hwnd);
            return null;
        }

        Native.ShowWindow(hwnd, Native.SW_HIDE);
        if (Native.IsWindowVisible(hwnd))
        {
            // 只回滚本次已成功修改的样式，避免对无法隐藏的特殊窗口留下半修改状态。
            Native.TrySetExStyle(hwnd, ex);
            ReportFailure(LocalizationService.Get("WindowOperation_HideFailed"), hwnd);
            return null;
        }

        return new HiddenWin(hwnd, ex, pid)
        {
            // 只有当前窗口就是前台窗口时才在恢复时尝试交还焦点。
            PreviousForeground = previousForeground == hwnd ? hwnd : IntPtr.Zero,
        };
    }

    /// <summary>判断目标进程权限；普通权限工具遇到提升目标时只提示手动提升，不绕过 UIPI。</summary>
    private bool CanControlProcess(uint pid, IntPtr hwnd)
    {
        if (!Native.TryGetProcessElevated(pid, out bool targetElevated))
        {
            ReportFailure(LocalizationService.Get("WindowOperation_ReadPermissionFailed"), hwnd);
            return false;
        }

        if (targetElevated && !Native.IsCurrentProcessElevated())
        {
            ReportFailure(LocalizationService.Get("WindowOperation_Elevated"), hwnd);
            return false;
        }

        return true;
    }

    /// <summary>恢复窗口样式和可见性；失败时保留运行时所有权以便后续重试。</summary>
    private bool Restore(HiddenWin w)
    {
        if (!IsLiveWindow(w))
            return true;

        if (!Native.TrySetExStyle(w.Hwnd, w.OriginalExStyle))
        {
            ReportFailure(LocalizationService.Get("WindowOperation_RestoreStyleFailed"), w.Hwnd);
            return false;
        }

        // 先无激活展示，最后只对隐藏前的前台窗口尝试交还焦点，避免其他窗口被无条件抢焦点。
        Native.ShowWindow(w.Hwnd, Native.SW_SHOWNA);
        if (!Native.IsWindowVisible(w.Hwnd))
        {
            ReportFailure(LocalizationService.Get("WindowOperation_RestoreFailed"), w.Hwnd);
            return false;
        }

        return true;
    }

    /// <summary>仅对确实记录过的前台窗口交还焦点，不让恢复操作无条件抢占其他应用。</summary>
    private static void RestoreFocus(HiddenWin window)
    {
        if (window.PreviousForeground != IntPtr.Zero && IsLiveWindow(window))
            Native.SetForegroundWindow(window.Hwnd);
    }

    /// <summary>移除某个已成功恢复窗口在所有规则中的共享所有权。</summary>
    private void RemoveOwnership(HiddenWin window)
    {
        foreach (var pair in _hidden.ToList())
        {
            pair.Value.RemoveAll(owner => ReferenceEquals(owner, window));
            if (pair.Value.Count == 0)
                _hidden.Remove(pair.Key);
        }

        foreach (var pair in _ruleHidden.ToList())
        {
            pair.Value.RemoveAll(owner => ReferenceEquals(owner, window));
            if (pair.Value.Count == 0)
                _ruleHidden.Remove(pair.Key);
        }

        _individual.RemoveAll(owner => ReferenceEquals(owner, window));
    }

    /// <summary>向上层发布可读失败提示；事件处理器异常不能阻止窗口恢复流程。</summary>
    private void ReportFailure(string message, IntPtr hwnd)
    {
        try
        {
            OperationFailed?.Invoke(message);
            Logger.Write($"窗口操作失败 HWND=0x{hwnd.ToInt64():X}: {message}");
        }
        catch (Exception ex)
        {
            Logger.LogException("发布窗口操作失败", ex);
        }
    }
}
