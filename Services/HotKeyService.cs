using System.Windows.Interop;
using HideIt.Models;

namespace HideIt.Services;

/// <summary>
/// Registers global hotkeys against a dedicated message-only window so they live
/// independently of any visible window (the settings window can be closed to tray).
/// </summary>
public sealed class HotKeyService : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, HotKeyCombo> _idToCombo = new();
    private int _nextId = 1;

    /// <summary>Raised on the UI thread when a registered combo is pressed.</summary>
    public event Action<HotKeyCombo>? Pressed;

    /// <summary>Raised when a combo could not be registered (already owned globally).</summary>
    public event Action<HotKeyCombo>? RegistrationFailed;

    public HotKeyService()
    {
        var p = new HwndSourceParameters("HideIt.HotKeyWindow")
        {
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3), // HWND_MESSAGE — message-only window
        };
        _source = new HwndSource(p);
        _source.AddHook(WndProc);
    }

    /// <summary>
    /// 安全应用快捷键集合：逐个注册新增组合，冲突项单独跳过并报告；已有注册不会被移除。
    /// 返回值表示目标集合是否完整注册成功。
    /// </summary>
    public bool RegisterAll(IEnumerable<HotKeyCombo> combos)
    {
        var wanted = combos.Distinct().ToList();
        var wantedSet = wanted.ToHashSet();
        var staged = new Dictionary<int, HotKeyCombo>();
        bool allRegistered = true;

        // wantedSet 用于判断旧注册是否仍被需要，staged 暂存本轮成功注册的组合。
        // 只为当前尚未注册的组合申请临时 ID，原有组合继续持有 Win32 注册。
        foreach (var combo in wanted)
        {
            if (_idToCombo.Values.Any(existing => existing.Equals(combo)))
                continue;

            uint mods = ToWin32Mods(combo.Modifiers) | Native.MOD_NOREPEAT;
            uint vk = (uint)combo.VirtualKey;
            int id = _nextId++;
            if (Native.RegisterHotKey(_source.Handle, id, mods, vk))
            {
                staged[id] = combo;
                continue;
            }

            // 冲突只影响当前组合，继续尝试其余组合，避免一个外部占用拖垮已有配置。
            allRegistered = false;
            RegistrationFailed?.Invoke(combo);
        }

        foreach (var pair in staged)
            _idToCombo[pair.Key] = pair.Value;

        if (!allRegistered)
        {
            // 只要有冲突就保留所有旧注册，避免修改新组合时让旧快捷键失效。
            return false;
        }

        // 新集合已确认可完整注册，再移除不再需要的旧组合。
        foreach (var pair in _idToCombo.ToList())
        {
            if (!wantedSet.Contains(pair.Value))
            {
                Native.UnregisterHotKey(_source.Handle, pair.Key);
                _idToCombo.Remove(pair.Key);
            }
        }

        return true;
    }

    public void UnregisterAll()
    {
        foreach (var id in _idToCombo.Keys)
            Native.UnregisterHotKey(_source.Handle, id);
        _idToCombo.Clear();
        _nextId = 1;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Native.WM_HOTKEY && _idToCombo.TryGetValue(wParam.ToInt32(), out var combo))
        {
            Pressed?.Invoke(combo);
            handled = true;
        }
        return IntPtr.Zero;
    }

    private static uint ToWin32Mods(Mod m)
    {
        uint r = 0;
        if (m.HasFlag(Mod.Alt)) r |= Native.MOD_ALT;
        if (m.HasFlag(Mod.Ctrl)) r |= Native.MOD_CONTROL;
        if (m.HasFlag(Mod.Shift)) r |= Native.MOD_SHIFT;
        if (m.HasFlag(Mod.Win)) r |= Native.MOD_WIN;
        return r;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
