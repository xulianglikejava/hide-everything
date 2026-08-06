using System.Text.Json.Serialization;
using System.Windows.Input;

namespace HideIt.Models;

/// <summary>
/// Our own modifier bitmask, independent of Win32's MOD_* values so the on-disk
/// format never depends on platform constants.
/// </summary>
[Flags]
public enum Mod
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A keyboard shortcut: one or more modifiers plus a single virtual key.
/// Value-equal so combos shared by several apps collapse to one registration.
/// </summary>
public sealed class HotKeyCombo : IEquatable<HotKeyCombo>
{
    public Mod Modifiers { get; set; }

    /// <summary>Win32 virtual-key code (from <see cref="KeyInterop.VirtualKeyFromKey"/>).</summary>
    public int VirtualKey { get; set; }

    public HotKeyCombo() { }

    public HotKeyCombo(Mod modifiers, int virtualKey)
    {
        Modifiers = modifiers;
        VirtualKey = virtualKey;
    }

    [JsonIgnore]
    public string DisplayText => Display();

    public string Display()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(Mod.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(Mod.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(Mod.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(Mod.Win)) parts.Add("Win");
        parts.Add(KeyName());
        return string.Join(" + ", parts);
    }

    private string KeyName()
    {
        var key = KeyInterop.KeyFromVirtualKey(VirtualKey);
        return key == Key.None ? $"0x{VirtualKey:X2}" : key.ToString();
    }

    public bool Equals(HotKeyCombo? other) =>
        other is not null && Modifiers == other.Modifiers && VirtualKey == other.VirtualKey;

    public override bool Equals(object? obj) => Equals(obj as HotKeyCombo);

    public override int GetHashCode() => HashCode.Combine(Modifiers, VirtualKey);

    public override string ToString() => Display();
}
