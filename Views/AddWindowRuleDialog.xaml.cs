using System.Windows;
using HideIt.Models;
using HideIt.Services;

namespace HideIt.Views;

/// <summary>从当前真实顶层窗口捕获稳定属性，并生成新的持久窗口规则。</summary>
public partial class AddWindowRuleDialog : Window
{
    private readonly AppController _controller;

    /// <summary>用户确认后生成的规则；取消时保持为空。</summary>
    public WindowRule? Result { get; private set; }

    public AddWindowRuleDialog(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        Reload();
    }

    /// <summary>刷新窗口列表；只展示当前可被稳定识别的真实顶层窗口。</summary>
    private void Reload()
    {
        WindowList.ItemsSource = _controller.GetOpenWindows();
        if (WindowList.Items.Count > 0)
            WindowList.SelectedIndex = 0;
    }

    /// <summary>选中窗口时默认填入当前标题，用户可清空以仅按类名匹配。</summary>
    private void WindowList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (WindowList.SelectedItem is not OpenWindow window) return;

        TitleContainsBox.Text = window.Title;
        DetailsText.Text = LocalizationService.Format(
            "AddRule_Details",
            window.WindowClassName,
            window.ExePath ?? LocalizationService.Get("Common_PathUnavailable"));
    }

    /// <summary>将当前选择转换为稳定规则并保留 Alt+D 默认切换快捷键。</summary>
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (WindowList.SelectedItem is not OpenWindow window)
        {
            MessageBox.Show(this,
                LocalizationService.Get("AddRule_SelectPrompt"),
                LocalizationService.Get("AddRule_Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var titleContains = TitleContainsBox.Text.Trim();
        Result = new WindowRule
        {
            DisplayName = window.Title,
            ProcessName = window.ProcessName,
            ExePath = window.ExePath,
            WindowClassName = window.WindowClassName,
            TitleContains = titleContains.Length == 0 ? null : titleContains,
        };
        DialogResult = true;
    }
}
