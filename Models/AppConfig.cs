using HideIt.Services;

namespace HideIt.Models;

/// <summary>根配置对象；保存到当前用户 AppData 下的 HideEverything\config.json。</summary>
public sealed class AppConfig
{
    /// <summary>界面语言标识；缺少或不受支持时由加载流程回退到简体中文。</summary>
    public string Language { get; set; } = LocalizationService.ChineseSimplified;

    public List<AppEntry> Apps { get; set; } = new();

    /// <summary>按稳定窗口属性匹配的持久窗口规则；旧配置缺少该字段时使用空列表。</summary>
    public List<WindowRule> WindowRules { get; set; } = new();

    public bool RunAtStartup { get; set; }

    /// <summary>
    /// 恢复全部隐藏窗口；默认使用 Ctrl+Alt+反引号（VK_OEM_3 = 0xC0）。
    /// 为 null 时关闭该兜底快捷键，缺少字段的旧配置继续使用默认值。
    /// </summary>
    public HotKeyCombo? PanicHotKey { get; set; } = new(Mod.Ctrl | Mod.Alt, 0xC0);

    /// <summary>
    /// 恢复窗口选择器最近隐藏的单个窗口；默认使用 Ctrl+Alt+Shift+S（VK 0x53）。
    /// 为 null 时关闭该快捷键。
    /// </summary>
    public HotKeyCombo? ShowLastHotKey { get; set; } = new(Mod.Ctrl | Mod.Alt | Mod.Shift, 0x53);

    /// <summary>首次引导页展示前为 false；展示后立即持久化为 true。</summary>
    public bool FirstRunComplete { get; set; }
}
