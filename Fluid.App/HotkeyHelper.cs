using System;
using System.Windows.Input;

namespace Fluid.App;

public static class HotkeyHelper
{
    private const uint MOD_ALT     = 0x0001;
    private const uint MOD_CTRL    = 0x0002;
    private const uint MOD_SHIFT   = 0x0004;
    private const uint MOD_WIN     = 0x0008;

    /// <summary>Convert a Key + ModifierKeys combo to a display string like "Ctrl+Shift+F12"</summary>
    public static string FormatCombo(ModifierKeys mods, Key key)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt))     parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift))   parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }

    /// <summary>Parse a combo string back to Win32 mod flags and virtual key code.</summary>
    public static void ParseCombo(string combo, out uint mod, out uint vk)
    {
        mod = 0; vk = 0;
        if (string.IsNullOrEmpty(combo)) return;

        var parts = combo.Split('+');
        string? keyPart = null;

        foreach (var part in parts)
        {
            switch (part.Trim().ToUpperInvariant())
            {
                case "CTRL":    case "CONTROL": mod |= MOD_CTRL;  break;
                case "ALT":                     mod |= MOD_ALT;   break;
                case "SHIFT":                   mod |= MOD_SHIFT; break;
                case "WIN":     case "WINDOWS": mod |= MOD_WIN;   break;
                default: keyPart = part.Trim(); break;
            }
        }

        if (keyPart == null) return;

        if (Enum.TryParse<Key>(keyPart, true, out var key))
        {
            var ki = KeyInterop.VirtualKeyFromKey(key);
            vk = (uint)ki;
        }
    }
}
