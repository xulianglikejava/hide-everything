using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HideIt.Models;
using HideIt.Services;

namespace HideIt.ViewModels;

/// <summary>设置界面中的持久窗口规则行，负责把稳定匹配条件转换为易读文本。</summary>
public partial class WindowRuleVm : ObservableObject
{
    public WindowRule Model { get; }

    public WindowRuleVm(WindowRule model, ImageSource? icon)
    {
        Model = model;
        Icon = icon;
    }

    public ImageSource? Icon { get; }

    public string DisplayName => Model.DisplayName;

    public string ProcessName => Model.ProcessName;

    public string MatchDescription => string.IsNullOrWhiteSpace(Model.TitleContains)
        ? Model.WindowClassName
        : LocalizationService.Format(
            "WindowRule_MatchDescription",
            Model.WindowClassName,
            Model.TitleContains);

    public string ShortcutText => Model.HotKey?.Display()
        ?? LocalizationService.Get("Common_None");

    /// <summary>规则是否启用；状态修改必须经过控制器，避免留下无法恢复的隐藏窗口。</summary>
    public bool IsEnabled => Model.Enabled;

    /// <summary>快捷键或启用状态提交后刷新当前行。</summary>
    public void RefreshHotKey() => OnPropertyChanged(nameof(ShortcutText));

    public void RefreshEnabled() => OnPropertyChanged(nameof(IsEnabled));
}
