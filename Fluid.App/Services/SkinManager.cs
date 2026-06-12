using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Markup;
using Fluid.App.Models;

namespace Fluid.App.Services;

public class SkinInfo
{
    public string Name        { get; set; } = "";
    public string Author      { get; set; } = "";
    public string Version     { get; set; } = "1.0";
    public string Description { get; set; } = "";
    public string FolderPath  { get; set; } = "";
    public bool   IsBuiltIn   { get; set; } = true;
}

public static class SkinManager
{
    public static readonly string UserSkinsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "fluidMonitor", "skins");

    // Only track the active dict — name is owned by AppSettings, not us
    private static ResourceDictionary? _activeSkinDict;

    public static readonly string[] BuiltInSkins =
        { "Default", "Minimal", "Sharp", "Glassmorphism", "Retro",
          "Terminal", "Holographic", "Brutalist", "Carbon", "Neon",
          "Frosted", "Cyberpunk", "Paper", "Ink", "Aurora", "Compact" };

    // ── Get all available skins (built-in + user installed) ───────────────
    public static List<SkinInfo> GetAllSkins()
    {
        var list = new List<SkinInfo>();

        foreach (var name in BuiltInSkins)
            list.Add(new SkinInfo { Name = name, Author = "DruidFluids",
                                    Version = "1.4", IsBuiltIn = true });

        if (!Directory.Exists(UserSkinsDir)) return list;

        foreach (var dir in Directory.GetDirectories(UserSkinsDir))
        {
            var xamlPath = Path.Combine(dir, "skin.xaml");
            if (!File.Exists(xamlPath)) continue;

            var info = new SkinInfo { Name = Path.GetFileName(dir), FolderPath = dir, IsBuiltIn = false };

            var jsonPath = Path.Combine(dir, "skin.json");
            if (File.Exists(jsonPath))
            {
                try
                {
                    var j = JsonSerializer.Deserialize<SkinInfo>(File.ReadAllText(jsonPath));
                    if (j != null)
                    {
                        info.Name        = j.Name.Length > 0 ? j.Name : info.Name;
                        info.Author      = j.Author;
                        info.Version     = j.Version;
                        info.Description = j.Description;
                    }
                }
                catch { }
            }
            list.Add(info);
        }
        return list;
    }

    // ── Apply a skin by name or folder path ───────────────────────────────
    public static bool ApplySkin(string nameOrPath, ResourceDictionary appResources)
    {
        try
        {
            ResourceDictionary dict;

            if (Array.IndexOf(BuiltInSkins, nameOrPath) >= 0)
            {
                var uri = new Uri($"pack://application:,,,/Styles/Skins/{nameOrPath}.xaml");
                dict = new ResourceDictionary { Source = uri };
            }
            else if (File.Exists(nameOrPath))
            {
                dict = new ResourceDictionary { Source = new Uri(nameOrPath, UriKind.Absolute) };
            }
            else if (File.Exists(Path.Combine(nameOrPath, "skin.xaml")))
            {
                dict = new ResourceDictionary
                    { Source = new Uri(Path.Combine(nameOrPath, "skin.xaml"), UriKind.Absolute) };
            }
            else return false;

            if (_activeSkinDict != null)
                appResources.MergedDictionaries.Remove(_activeSkinDict);

            appResources.MergedDictionaries.Add(dict);
            _activeSkinDict = dict;

            // After swap, re-resolve theme colors → skin slots so the new skin's
            // SkinTileBorderSource etc. get bound to current AppSettings colors.
            try { ThemeApplier.Apply(App.Current.Settings, appResources); } catch { }

            // v1.19d: rebuild the widget's tile ItemsControl content so each
            // tile re-instantiates from its DataTemplate against the new
            // resource dictionary. Pure DynamicResource binding updates do
            // not always propagate into templated tile visuals -- the tile's
            // Border/Background/Effect were captured at instantiation time
            // and the new skin's values need fresh template instances to
            // take effect. Calling RebuildVisibleTiles forces that.
            try
            {
                if (System.Windows.Application.Current.MainWindow is MainWindow __mw)
                    __mw.RebuildVisibleTiles();
            }
            catch { /* MainWindow may not exist during early startup */ }

            // v1.13: also seed the per-text-type font keys based on AppSettings
            // overrides. Falls back to the skin's SkinFontFamily when no override.
            try { ApplyFontOverrides(App.Current.Settings, appResources); } catch { }

            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// Set WidgetPrimaryFont / WidgetSecondaryFont / WidgetIndicatorFont resources
    /// based on AppSettings. Empty/null/"(skin default)" all mean "fall back to
    /// SkinFontFamily" (the value the active skin XAML supplies).
    /// Call after ApplySkin or whenever settings.PrimaryFont/SecondaryFont/IndicatorFont
    /// change, so the widget re-renders with the new fonts immediately.
    /// </summary>
    public static void ApplyFontOverrides(AppSettings s, ResourceDictionary appResources)
    {
        var skinDefault = appResources["SkinFontFamily"] as System.Windows.Media.FontFamily
                          ?? new System.Windows.Media.FontFamily("Segoe UI");
        appResources["WidgetPrimaryFont"]   = FontOrDefault(s.PrimaryFont,   skinDefault);
        appResources["WidgetSecondaryFont"] = FontOrDefault(s.SecondaryFont, skinDefault);
        appResources["WidgetIndicatorFont"] = FontOrDefault(s.IndicatorFont, skinDefault);
    }

    private static System.Windows.Media.FontFamily FontOrDefault(string? userPick, System.Windows.Media.FontFamily fallback)
    {
        if (string.IsNullOrWhiteSpace(userPick)) return fallback;
        if (userPick == FontCatalog.DefaultEntry) return fallback;
        try { return new System.Windows.Media.FontFamily(userPick); }
        catch { return fallback; }
    }

    /// <summary>
    /// v1.25.19: scan every available skin (built-in + user-installed) and
    /// return the unique set of font names referenced by their SkinFontFamily
    /// resource. Each skin's SkinFontFamily can be a comma-separated fallback
    /// chain (e.g. "Inter, Segoe UI, Arial"); every name in the chain is
    /// collected so users can pick any of them explicitly from the font
    /// dropdowns. Result is de-duped and stable-ordered (built-ins first,
    /// then user skins, in skin-discovery order).
    /// </summary>
    public static List<string> CollectSkinFontNames()
    {
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var skin in GetAllSkins())
        {
            try
            {
                ResourceDictionary dict;
                if (skin.IsBuiltIn)
                {
                    var uri = new Uri($"pack://application:,,,/Styles/Skins/{skin.Name}.xaml");
                    dict = new ResourceDictionary { Source = uri };
                }
                else
                {
                    var xamlPath = File.Exists(skin.FolderPath)
                        ? skin.FolderPath
                        : Path.Combine(skin.FolderPath, "skin.xaml");
                    if (!File.Exists(xamlPath)) continue;
                    dict = new ResourceDictionary { Source = new Uri(xamlPath, UriKind.Absolute) };
                }

                if (dict["SkinFontFamily"] is System.Windows.Media.FontFamily ff)
                {
                    foreach (var raw in (ff.Source ?? "").Split(','))
                    {
                        var name = raw.Trim();
                        if (string.IsNullOrEmpty(name)) continue;
                        if (seen.Add(name)) result.Add(name);
                    }
                }
            }
            catch { /* malformed skin -- skip silently */ }
        }
        return result;
    }

    // ── Install a skin from an external .xaml file or folder ─────────────
    // Validates XAML before copying so a bad skin can't crash the app on load
    public static string? InstallSkinFromPath(string path)
    {
        try
        {
            string xamlPath, name;
            if (Directory.Exists(path))
            {
                xamlPath = Path.Combine(path, "skin.xaml");
                name     = Path.GetFileName(path);
            }
            else if (File.Exists(path) && path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
            {
                xamlPath = path;
                name     = Path.GetFileNameWithoutExtension(path);
            }
            else return null;

            if (!File.Exists(xamlPath)) return null;

            // Validate XAML before installing — parse it as a ResourceDictionary
            using (var stream = File.OpenRead(xamlPath))
            {
                var obj = XamlReader.Load(stream);
                if (obj is not ResourceDictionary)
                    return null; // Not a valid skin file
            }

            var destDir = Path.Combine(UserSkinsDir, name);
            Directory.CreateDirectory(destDir);
            File.Copy(xamlPath, Path.Combine(destDir, "skin.xaml"), overwrite: true);

            var jsonSrc = Path.Combine(Path.GetDirectoryName(xamlPath)!, "skin.json");
            if (File.Exists(jsonSrc))
                File.Copy(jsonSrc, Path.Combine(destDir, "skin.json"), overwrite: true);

            return destDir;
        }
        catch { return null; }
    }
}
