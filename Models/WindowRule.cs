namespace HideIt.Models;

/// <summary>
/// 一个受控窗口的持久匹配规则；只保存稳定属性，不保存会随窗口重建而变化的 HWND。
/// </summary>
public sealed class WindowRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>规则显示名称，通常使用创建规则时的窗口标题。</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>不含“.exe”的进程名，作为 exe 路径不可用时的后备匹配条件。</summary>
    public string ProcessName { get; set; } = "";

    /// <summary>创建规则时读取到的 exe 完整路径；路径比较忽略大小写。</summary>
    public string? ExePath { get; set; }

    /// <summary>窗口类名是识别同一类受控窗口的主要稳定条件。</summary>
    public string WindowClassName { get; set; } = "";

    /// <summary>可选的标题包含文本；为空时不使用标题进行筛选。</summary>
    public string? TitleContains { get; set; }

    /// <summary>规则是否参与快捷键注册和窗口切换。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>新建窗口规则默认使用 Alt+D，用户可在设置界面改为其他全局快捷键。</summary>
    public HotKeyCombo? HotKey { get; set; } = new(Mod.Alt, 0x44);
}
