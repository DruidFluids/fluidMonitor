using System;
using System.IO;
using Microsoft.Win32;

namespace Fluid.App.Services;

/// <summary>
/// v1.23: run-at-startup management for the Settings toggle.
///
/// Mechanism: a per-user value under HKCU\...\Run -- writable without
/// elevation, which is why the Settings checkbox needs no UAC shield.
///
/// The installer has long offered an optional startup-folder shortcut
/// ({userstartup}\fluidMonitor.lnk). Both mechanisms are treated as "startup
/// enabled" when reading state, and BOTH are removed when disabling, so the
/// user can never end up with the checkbox off while the app still launches
/// at sign-in through the other path.
/// </summary>
public static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName  = "fluidMonitor";

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),
                     "fluidMonitor.lnk");

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            if (key?.GetValue(ValueName) is string v && v.Length > 0) return true;
        }
        catch { }
        try { if (File.Exists(ShortcutPath)) return true; } catch { }
        return false;
    }

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            // Environment.ProcessPath resolves to the single-file apphost exe
            // ({app}\app\fluidMonitor.exe for installed builds).
            var exe = Environment.ProcessPath
                      ?? throw new InvalidOperationException("Cannot resolve the application path.");
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath)
                            ?? throw new InvalidOperationException("Cannot open the HKCU Run key.");
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            catch { }
            // Also clear the installer's optional startup shortcut (see class doc).
            try { if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath); } catch { }
        }
    }
}
