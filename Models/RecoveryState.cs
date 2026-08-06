namespace HideIt.Models;

/// <summary>
/// 异常退出后的尽力恢复状态；只记录稳定规则标识和窗口匹配快照，不把 HWND 当作永久配置。
/// </summary>
public sealed class RecoveryState
{
    /// <summary>上次退出前处于隐藏意图状态的受控程序配置 ID。</summary>
    public HashSet<string> HiddenAppIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>上次退出前处于隐藏意图状态的窗口规则 ID。</summary>
    public HashSet<string> HiddenWindowRuleIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>会话级隐藏窗口的稳定匹配快照；只用于下次启动的一次性尽力恢复。</summary>
    public List<RecoveryWindow> IndividualWindows { get; set; } = new();
}

/// <summary>会话级隐藏窗口的恢复匹配条件；窗口关闭或标题变化后允许恢复失败。</summary>
public sealed class RecoveryWindow
{
    /// <summary>不含“.exe”的进程名。</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>可选的 exe 完整路径，用于降低进程名重用导致的误匹配。</summary>
    public string? ExePath { get; set; }

    /// <summary>窗口类名，用于识别新的窗口实例。</summary>
    public string WindowClassName { get; set; } = "";

    /// <summary>隐藏时的标题快照；恢复时按不区分大小写的包含关系匹配。</summary>
    public string? TitleContains { get; set; }
}
