using System.Windows;

namespace HideIt.Views;

/// <summary>One-time welcome panel shown on first run.</summary>
public partial class OnboardingWindow : Window
{
    /// <summary>True if the user asked to open Settings on close.</summary>
    public bool OpenSettingsRequested { get; private set; }

    public OnboardingWindow()
    {
        InitializeComponent();
    }

    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSettingsRequested = true;
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
