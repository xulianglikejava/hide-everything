namespace HideIt.Models;

/// <summary>一个受控程序配置：记录进程匹配信息和进程组切换快捷键。</summary>
public sealed class AppEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>不含“.exe”的进程名（例如“chrome”）；匹配时忽略大小写。</summary>
    public string ProcessName { get; set; } = "";

    public string DisplayName { get; set; } = "";

    /// <summary>可选的 exe 完整路径，用于显示图标并为后续启动能力保留信息。</summary>
    public string? ExePath { get; set; }

    /// <summary>规则是否参与快捷键注册和进程组切换；旧配置缺少该字段时默认启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>新建受控程序默认绑定 Alt+D（VK_D = 0x44）；用户仍可修改或清除。</summary>
    public HotKeyCombo? HotKey { get; set; } = new(Mod.Alt, 0x44);
}
