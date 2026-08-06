using System.IO;
using System.Windows;
using HideIt.Models;
using HideIt.Services;
using Microsoft.Win32;

namespace HideIt.Views;

/// <summary>从当前运行程序列表或 exe 文件选择受控程序，并返回带默认快捷键的新配置。</summary>
public partial class AddAppDialog : Window
{
    private readonly ProcessCatalog _catalog;

    /// <summary>用户确认后生成的受控程序配置；取消时保持为空。</summary>
    public AppEntry? Result { get; private set; }

    /// <summary>加载当前可选程序；列表只展示拥有真实主窗口的运行中程序。</summary>
    public AddAppDialog(ProcessCatalog catalog)
    {
        InitializeComponent();
        _catalog = catalog;
        AppList.ItemsSource = _catalog.GetRunningAppsWithWindows().ToList();
    }

    /// <summary>确认当前选择，并保留 AppEntry 的 Alt+D 默认老板键。</summary>
    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (AppList.SelectedItem is RunningApp app)
        {
            Result = new AppEntry
            {
                // 新建程序默认绑定 Alt+D，用户可以在保存后通过设置修改。
                ProcessName = app.ProcessName,
                DisplayName = app.DisplayName,
                ExePath = app.ExePath,
            };
            DialogResult = true;
            return;
        }
        MessageBox.Show(this,
            LocalizationService.Get("AddApp_SelectPrompt"),
            LocalizationService.Get("AddApp_Title"),
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void AppList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (AppList.SelectedItem is RunningApp)
            Ok_Click(sender, e);
    }

    /// <summary>从文件选择器读取 exe 路径，并生成同样使用 Alt+D 的配置。</summary>
    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = LocalizationService.Get("AddApp_SelectTitle"),
            Filter = LocalizationService.Get("AddApp_FileFilter"),
            CheckFileExists = true,
        };
        if (dlg.ShowDialog(this) != true) return;

        var path = dlg.FileName;
        var name = Path.GetFileNameWithoutExtension(path);
        Result = new AppEntry
        {
            // 浏览 exe 与运行中程序采用相同的默认老板键规则。
            ProcessName = name,
            DisplayName = name,
            ExePath = path,
        };
        DialogResult = true;
    }
}
