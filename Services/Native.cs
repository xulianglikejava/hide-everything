using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace HideIt.Services;

/// <summary>集中声明窗口隐藏、权限读取、快捷键和窗口枚举所需的 Win32 API。</summary>
internal static class Native
{
    // ---- Constants ----
    public const int GWL_EXSTYLE = -20;

    public const long WS_EX_TOOLWINDOW = 0x00000080; // removes from taskbar AND Alt+Tab
    public const long WS_EX_APPWINDOW = 0x00040000;  // forces onto taskbar
    public const long WS_EX_NOACTIVATE = 0x08000000; // floating icon: never steal focus

    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_SHOWNA = 8;

    public const uint GW_OWNER = 4;

    public const uint MOD_ALT = 0x1;
    public const uint MOD_CONTROL = 0x2;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_WIN = 0x8;
    public const uint MOD_NOREPEAT = 0x4000;

    public const int DWMWA_CLOAKED = 14;

    public const int WM_HOTKEY = 0x0312;

    // 仅查询进程和令牌权限所需的最小访问权限，避免请求可绕过权限隔离的句柄。
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TOKEN_INFORMATION_CLASS_ELEVATION = 20;

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    // ---- Hotkeys ----
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ---- Window show/hide + focus ----
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    // ---- Enumeration ----
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    [DllImport("user32.dll")]
    public static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hWnd, int dwAttribute, out int pvAttribute, int cbAttribute);

    // ---- Extended styles: Ptr variants on 64-bit, plain on 32-bit ----
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    /// <summary>读取窗口扩展样式并区分“样式值为零”和“权限读取失败”。</summary>
    public static bool TryGetExStyle(IntPtr hWnd, out long exStyle)
    {
        Marshal.SetLastPInvokeError(0);
        IntPtr value = IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, GWL_EXSTYLE)
            : new IntPtr(GetWindowLong32(hWnd, GWL_EXSTYLE));
        int error = Marshal.GetLastPInvokeError();
        if (value == IntPtr.Zero && error != 0)
        {
            exStyle = 0;
            return false;
        }

        exStyle = value.ToInt64();
        return true;
    }

    /// <summary>尝试更新窗口扩展样式；失败时返回 false，调用方不得继续强制隐藏。</summary>
    public static bool TrySetExStyle(IntPtr hWnd, long exStyle)
    {
        Marshal.SetLastPInvokeError(0);
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hWnd, GWL_EXSTYLE, new IntPtr(exStyle));
        else
            SetWindowLong32(hWnd, GWL_EXSTYLE, (int)exStyle);

        return Marshal.GetLastPInvokeError() == 0;
    }

    /// <summary>读取指定进程是否以提升权限运行；无法读取时返回 false 并由调用方提示用户。</summary>
    public static bool TryGetProcessElevated(uint processId, out bool elevated)
    {
        elevated = false;
        IntPtr process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == IntPtr.Zero) return false;

        try
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out IntPtr token))
                return false;

            try
            {
                int size = Marshal.SizeOf<TOKEN_ELEVATION>();
                var buffer = new TOKEN_ELEVATION();
                if (!GetTokenInformation(token, TOKEN_INFORMATION_CLASS_ELEVATION,
                        ref buffer, size, out int returned) || returned < size)
                    return false;

                elevated = buffer.TokenIsElevated != 0;
                return true;
            }
            finally
            {
                CloseNativeHandle(token);
            }
        }
        finally
        {
            CloseNativeHandle(process);
        }
    }

    /// <summary>读取当前工具权限级别；程序清单保持 asInvoker，不在这里主动请求管理员权限。</summary>
    public static bool IsCurrentProcessElevated()
    {
        IntPtr process = GetCurrentProcess();
        if (!OpenProcessToken(process, TOKEN_QUERY, out IntPtr token))
            return false;

        try
        {
            int size = Marshal.SizeOf<TOKEN_ELEVATION>();
            var buffer = new TOKEN_ELEVATION();
            return GetTokenInformation(token, TOKEN_INFORMATION_CLASS_ELEVATION,
                ref buffer, size, out int returned) && returned >= size && buffer.TokenIsElevated != 0;
        }
        finally
        {
            CloseNativeHandle(token);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_ELEVATION
    {
        public uint TokenIsElevated;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        ref TOKEN_ELEVATION tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseNativeHandle(IntPtr hObject);

    /// <summary>返回指定受控程序的真实顶层窗口；恢复阶段额外包含仍存在但已被隐藏的窗口。</summary>
    public static List<IntPtr> GetTopLevelWindows(string processName, bool includeHidden = false)
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try { pids.Add((uint)p.Id); }
            catch { /* process exited */ }
            finally { p.Dispose(); }
        }

        var result = new List<IntPtr>();
        if (pids.Count == 0) return result;

        EnumWindows((hWnd, _) =>
        {
            GetWindowThreadProcessId(hWnd, out uint pid);
            if (pids.Contains(pid) && (IsRealAppWindow(hWnd) || includeHidden && IsHiddenTopLevelWindow(hWnd)))
                result.Add(hWnd);
            return true;
        }, IntPtr.Zero);

        return result;
    }

    public static uint GetWindowPid(IntPtr hWnd)
    {
        GetWindowThreadProcessId(hWnd, out uint pid);
        return pid;
    }

    public static string GetWindowTitle(IntPtr hWnd)
    {
        int len = GetWindowTextLength(hWnd);
        if (len == 0) return "";
        var sb = new StringBuilder(len + 1);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }

    /// <summary>读取窗口类名；读取失败时返回空字符串，让调用方按后备条件处理。</summary>
    public static string GetWindowClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        return GetClassName(hWnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    /// <summary>枚举真实顶层窗口；恢复阶段包含没有可见状态但仍存在的受控窗口。</summary>
    public readonly record struct WindowInfo(IntPtr Hwnd, string Title, uint Pid);

    /// <summary>枚举所有可选的真实顶层窗口，必要时包含异常退出后仍隐藏的窗口。</summary>
    public static List<WindowInfo> GetAllRealWindows(bool includeHidden = false)
    {
        var result = new List<WindowInfo>();
        EnumWindows((hWnd, _) =>
        {
            if (IsRealAppWindow(hWnd) || includeHidden && IsHiddenTopLevelWindow(hWnd))
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                result.Add(new WindowInfo(hWnd, GetWindowTitle(hWnd), pid));
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    /// <summary>
    /// 识别异常退出后仍存在的隐藏窗口；只接受无所有者且有标题的顶层窗口，不放宽到任意子窗口。
    /// </summary>
    private static bool IsHiddenTopLevelWindow(IntPtr hWnd)
    {
        if (IsWindowVisible(hWnd)) return false;
        if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return false;
        if (GetWindowTextLength(hWnd) == 0) return false;
        // 工具隐藏时会加入 TOOLWINDOW；只恢复带该标记的不可见窗口，降低误匹配风险。
        return TryGetExStyle(hWnd, out long exStyle)
            && (exStyle & WS_EX_TOOLWINDOW) != 0;
    }

    /// <summary>判断窗口是否为可供用户选择的可见真实顶层窗口。</summary>
    public static bool IsRealAppWindow(IntPtr hWnd)
    {
        if (!IsWindowVisible(hWnd)) return false;
        if (GetWindow(hWnd, GW_OWNER) != IntPtr.Zero) return false;
        if (GetWindowTextLength(hWnd) == 0) return false;
        if (!TryGetExStyle(hWnd, out long exStyle)) return false;
        if ((exStyle & WS_EX_TOOLWINDOW) != 0) return false;
        if (DwmGetWindowAttribute(hWnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
            return false;
        return true;
    }
}
