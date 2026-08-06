using System.Windows;
using System.Windows.Input;
using HideIt.Models;
using HideIt.Services;

namespace HideIt.Views;

/// <summary>Captures a key combo. Requires at least one modifier; rejects bare keys.</summary>
public partial class HotKeyCaptureDialog : Window
{
    /// <summary>The captured combo, or null if the user cleared the shortcut.</summary>
    public HotKeyCombo? Result { get; private set; }

    private HotKeyCombo? _pending;

    public HotKeyCaptureDialog()
    {
        InitializeComponent();
    }

    private void CaptureBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;

        // Alt combos arrive as Key.System with the real key in SystemKey.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (IsModifierKey(key))
        {
            DisplayText.Text = LocalizationService.Get("HotKey_Press");
            HintText.Text = string.Empty;
            _pending = null;
            return;
        }

        var mods = MapModifiers(Keyboard.Modifiers);
        if (mods == Mod.None)
        {
            HintText.Text = LocalizationService.Get("HotKey_ModifierRequired");
            _pending = null;
            DisplayText.Text = LocalizationService.Get("HotKey_Press");
            return;
        }

        int vk = KeyInterop.VirtualKeyFromKey(key);
        _pending = new HotKeyCombo(mods, vk);
        DisplayText.Text = _pending.Display();
        HintText.Text = string.Empty;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System or Key.None;

    private static Mod MapModifiers(ModifierKeys m)
    {
        var r = Mod.None;
        if (m.HasFlag(ModifierKeys.Control)) r |= Mod.Ctrl;
        if (m.HasFlag(ModifierKeys.Alt)) r |= Mod.Alt;
        if (m.HasFlag(ModifierKeys.Shift)) r |= Mod.Shift;
        if (m.HasFlag(ModifierKeys.Windows)) r |= Mod.Win;
        return r;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_pending == null)
        {
            HintText.Text = LocalizationService.Get("HotKey_ValidRequired");
            return;
        }
        Result = _pending;
        DialogResult = true;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Result = null;
        DialogResult = true;
    }
}
