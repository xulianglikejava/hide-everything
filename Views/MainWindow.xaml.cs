using System.Windows;
using HideIt.Services;
using HideIt.ViewModels;

namespace HideIt.Views;

public partial class MainWindow : Window
{
    public MainWindow(AppController controller)
    {
        InitializeComponent();
        DataContext = new MainViewModel(controller);
        // DataGridColumn.Header 不是稳定的依赖属性，使用事件主动刷新两张表头以支持即时语言切换。
        LocalizationService.LanguageChanged += UpdateLocalizedHeaders;
        UpdateLocalizedHeaders();
        Closed += (_, _) => LocalizationService.LanguageChanged -= UpdateLocalizedHeaders;
    }

    /// <summary>按当前语言更新程序规则和窗口规则的表头，保留列顺序和数据绑定不变。</summary>
    private void UpdateLocalizedHeaders()
    {
        SetHeaders(Grid, "Main_Program", "Main_Enabled", "Main_HotKey");
        SetHeaders(WindowRulesGrid, "Main_Window", "Main_Enabled", "Main_HotKey", "Main_Match");
    }

    /// <summary>按列布局写入表头；窗口规则表多一个匹配条件列，使用独立索引映射。</summary>
    private static void SetHeaders(
        System.Windows.Controls.DataGrid grid,
        string primaryKey,
        string enabledKey,
        string hotKeyKey,
        string? matchKey = null)
    {
        if (grid.Columns.Count < 5)
            return;

        grid.Columns[1].Header = LocalizationService.Get(primaryKey);
        if (matchKey == null)
        {
            grid.Columns[2].Header = LocalizationService.Get(enabledKey);
            grid.Columns[3].Header = LocalizationService.Get(hotKeyKey);
        }
        else
        {
            grid.Columns[2].Header = LocalizationService.Get(matchKey);
            grid.Columns[3].Header = LocalizationService.Get(enabledKey);
            grid.Columns[4].Header = LocalizationService.Get(hotKeyKey);
        }
    }
}
