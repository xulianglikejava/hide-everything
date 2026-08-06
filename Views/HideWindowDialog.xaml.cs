using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using HideIt.Services;

namespace HideIt.Views;

/// <summary>列出当前真实顶层窗口，让用户只隐藏指定窗口而不是整个受控程序。</summary>
public partial class HideWindowDialog : Window
{
    /// <summary>Row wrapper that carries the checkbox state for one open window.</summary>
    public sealed class Row
    {
        public required OpenWindow Win { get; init; }
        public bool IsChecked { get; set; }
        public string Title => Win.Title;
        public string ProcessName => Win.ProcessName;
        public ImageSource? Icon => Win.Icon;

        /// <summary>该窗口已有的会话快捷键（例如“Ctrl+Alt+C”），没有则为空。</summary>
        public string Shortcut { get; init; } = "";
        public bool HasShortcut => Shortcut.Length > 0;
    }

    private readonly AppController _controller;

    /// <summary>Handles of the windows the user chose to hide.</summary>
    public List<IntPtr> Result { get; private set; } = new();

    public HideWindowDialog(AppController controller)
    {
        InitializeComponent();
        _controller = controller;
        Reload();
    }

    private void Reload() =>
        List.ItemsSource = _controller.GetOpenWindows()
            .Select(w => new Row
            {
                Win = w,
                Shortcut = _controller.GetTempBindingFor(w.Hwnd)?.Display() ?? "",
            })
            .ToList();

    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();

    private List<IntPtr> CheckedHandles() =>
        (List.ItemsSource as IEnumerable<Row>)!
            .Where(r => r.IsChecked)
            .Select(r => r.Win.Hwnd)
            .ToList();

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        Result = CheckedHandles();
        if (Result.Count == 0)
        {
            MessageBox.Show(this,
                LocalizationService.Get("HideWindow_SelectPrompt"),
                LocalizationService.Get("HideWindow_Title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void Assign_Click(object sender, RoutedEventArgs e)
    {
        var handles = CheckedHandles();
        if (handles.Count == 0)
        {
            MessageBox.Show(this,
                LocalizationService.Get("HideWindow_AssignPrompt"),
                LocalizationService.Get("HideWindow_Assign"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Warn if any chosen window already has a shortcut — assigning replaces it.
        var existing = handles
            .Select(_controller.GetTempBindingFor)
            .Where(c => c != null)
            .Select(c => c!.Display())
            .Distinct()
            .ToList();
        if (existing.Count > 0)
        {
            var current = string.Join(", ", existing);
            var answer = MessageBox.Show(this,
                LocalizationService.Format("HideWindow_ExistingMessage", current),
                LocalizationService.Get("HideWindow_ExistingTitle"),
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        var capture = new HotKeyCaptureDialog { Owner = this };
        if (capture.ShowDialog() != true || capture.Result == null)
            return; // cancelled or cleared

        bool ok = _controller.AddTempWindowBinding(capture.Result, handles);
        if (ok)
        {
            MessageBox.Show(this,
                LocalizationService.Format("HideWindow_AssignedMessage", capture.Result.Display()),
                LocalizationService.Get("HideWindow_AssignedTitle"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = false; // closes the dialog; nothing for the caller to hide
        }
        else
        {
            MessageBox.Show(this,
                LocalizationService.Format("HideWindow_ConflictMessage", capture.Result.Display()),
                LocalizationService.Get("HideWindow_ConflictTitle"),
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
