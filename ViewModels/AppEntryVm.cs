using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using HideIt.Models;
using HideIt.Services;

namespace HideIt.ViewModels;

/// <summary>Row view-model wrapping an <see cref="AppEntry"/> for the settings grid.</summary>
public partial class AppEntryVm : ObservableObject
{
    public AppEntry Model { get; }

    public AppEntryVm(AppEntry model, ImageSource? icon)
    {
        Model = model;
        Icon = icon;
    }

    public ImageSource? Icon { get; }

    public string DisplayName => Model.DisplayName;

    public string ProcessName => Model.ProcessName;

    public string ShortcutText => Model.HotKey?.Display()
        ?? LocalizationService.Get("Common_None");

    /// <summary>规则是否启用；设置界面用单向绑定展示，由控制器负责提交状态变更。</summary>
    public bool IsEnabled => Model.Enabled;

    /// <summary>快捷键或启用状态变更后刷新当前数据行的显示值。</summary>
    public void RefreshHotKey() => OnPropertyChanged(nameof(ShortcutText));

    /// <summary>通知界面重新读取规则启用状态，避免绕过控制器直接改模型。</summary>
    public void RefreshEnabled() => OnPropertyChanged(nameof(IsEnabled));
}
