using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Fluid.App.Models;
using Fluid.App.Services;

namespace Fluid.App;

public static class ThemeApplier
{
    public const double BasePrimaryFontSize   = 18.0;
    public const double BaseSecondaryFontSize = 11.0;
    public const double BaseUnitFontSize      = 12.0;
    public const double BaseIndicatorFontSize = 16.0;

    // Dark mode defaults
    public const string DarkBackground = "#E61E1E22";
    public const string DarkTile       = "#FF2A2A30";
    public const string DarkAccent     = "#FF00A8FF";
    public const string DarkText       = "#FFE8E8EC";
    public const string DarkMuted      = "#FF9A9AA8";

    // Light mode defaults
    public const string LightBackground = "#FFF0F0F5";
    public const string LightTile       = "#FFFFFFFF";
    public const string LightAccent     = "#FF0066CC";
    public const string LightText       = "#FF1C1C1E";
    public const string LightMuted      = "#FF6E6E73";

    public record ThemePreset(string Name, string Bg, string Tile, string Accent, string Text, string Muted);

    public static readonly ThemePreset[] Presets = new[]
    {
        new ThemePreset("Custom",           "",          "",          "",          "",          ""),
        new ThemePreset("Dark (default)",   "#E61E1E22", "#FF2A2A30", "#FF00A8FF", "#FFE8E8EC", "#FF9A9AA8"),
        new ThemePreset("Light (default)",  "#FFF0F0F5", "#FFFFFFFF", "#FF0066CC", "#FF1C1C1E", "#FF6E6E73"),
        new ThemePreset("Catppuccin Mocha", "#FF1E1E2E", "#FF313244", "#FF89B4FA", "#FFCDD6F4", "#FF6C7086"),
        new ThemePreset("One Dark",         "#FF282C34", "#FF21252B", "#FF61AFEF", "#FFABB2BF", "#FF5C6370"),
        new ThemePreset("Dracula",          "#FF282A36", "#FF44475A", "#FFBD93F9", "#FFF8F8F2", "#FF6272A4"),
        new ThemePreset("Tokyo Night",      "#FF1A1B2E", "#FF24283B", "#FF7AA2F7", "#FFC0CAF5", "#FF565F89"),
        new ThemePreset("Gruvbox",          "#FF282828", "#FF3C3836", "#FFD79921", "#FFEBDBB2", "#FFA89984"),
        new ThemePreset("Nord",             "#FF2E3440", "#FF3B4252", "#FF88C0D0", "#FFECEFF4", "#FF616E88"),
        new ThemePreset("Rosé Pine",        "#FF191724", "#FF1F1D2E", "#FFEB6F92", "#FFE0DEF4", "#FF6E6A86"),
        new ThemePreset("Kanagawa",         "#FF1F1F28", "#FF2A2A37", "#FF7E9CD8", "#FFDCD7BA", "#FF727169"),
        new ThemePreset("Everforest",       "#FF2D353B", "#FF343F44", "#FFA7C080", "#FFD3C6AA", "#FF859289"),
        new ThemePreset("Solarized Dark",   "#FF002B36", "#FF073642", "#FF268BD2", "#FFFDF6E3", "#FF657B83"),
        new ThemePreset("Monokai Pro",      "#FF2D2A2E", "#FF403E41", "#FFA9DC76", "#FFFCFCFA", "#FF727072"),
        new ThemePreset("Palenight",        "#FF292D3E", "#FF333747", "#FFC3E88D", "#FFEEEFFF", "#FF676E95"),
        new ThemePreset("Ayu Mirage",       "#FF1F2430", "#FF242B38", "#FFFFB454", "#FFCCCAC2", "#FF707A8C"),
        new ThemePreset("Poimandres",       "#FF1B1E28", "#FF252837", "#FF5DE4C7", "#FFE4F0FB", "#FF767C9D"),
        new ThemePreset("Horizon",          "#FF1C1E26", "#FF232530", "#FFE95678", "#FFECECEC", "#FF6C6F93"),
        new ThemePreset("Mellow",           "#FF1A1A19", "#FF252521", "#FFF0A868", "#FFDBDBB4", "#FF72726B"),

        // --- v1.12 additions: 20 most popular community themes ---
        // Catppuccin family (Latte/Frappe/Macchiato — Mocha already above). Canonical hex from
        // https://catppuccin.com/palette: base/surface0/blue/text/subtext0
        new ThemePreset("Catppuccin Latte",      "#FFEFF1F5", "#FFCCD0DA", "#FF1E66F5", "#FF4C4F69", "#FF6C6F85"),
        new ThemePreset("Catppuccin Frappé",     "#FF303446", "#FF414559", "#FF8CAAEE", "#FFC6D0F5", "#FFA5ADCE"),
        new ThemePreset("Catppuccin Macchiato",  "#FF24273A", "#FF363A4F", "#FF8AADF4", "#FFCAD3F5", "#FFA5ADCB"),

        // GitHub themes — most-installed in VS Code marketplace (~18M+ installs)
        new ThemePreset("GitHub Dark",           "#FF0D1117", "#FF161B22", "#FF58A6FF", "#FFC9D1D9", "#FF8B949E"),
        new ThemePreset("GitHub Light",          "#FFFFFFFF", "#FFF6F8FA", "#FF0969DA", "#FF1F2328", "#FF656D76"),
        new ThemePreset("GitHub Dark Dimmed",    "#FF22272E", "#FF2D333B", "#FF539BF5", "#FFADBAC7", "#FF768390"),

        // Light siblings of existing dark themes (currently only have "Light")
        new ThemePreset("Solarized Light",       "#FFFDF6E3", "#FFEEE8D5", "#FF268BD2", "#FF586E75", "#FF93A1A1"),
        new ThemePreset("Gruvbox Light",         "#FFFBF1C7", "#FFEBDBB2", "#FFB57614", "#FF3C3836", "#FF7C6F64"),
        new ThemePreset("Ayu Light",             "#FFFAFAFA", "#FFF2F2F2", "#FFFA8D3E", "#FF5C6166", "#FF8A9199"),
        new ThemePreset("Ayu Dark",              "#FF0B0E14", "#FF131721", "#FFE6B450", "#FFBFBDB6", "#FF565B66"),

        // Night Owl by Sarah Drasner — popular night-coding theme
        new ThemePreset("Night Owl",             "#FF011627", "#FF112233", "#FF82AAFF", "#FFD6DEEB", "#FF637777"),
        new ThemePreset("Light Owl",             "#FFFBFBFB", "#FFF0F0F0", "#FF2AA298", "#FF403F53", "#FF989FB1"),

        // Synthwave '84 by Robb Owen — iconic retro neon
        new ThemePreset("Synthwave '84",         "#FF241B2F", "#FF2A2139", "#FFFF7EDB", "#FFFFFFFF", "#FF848BBD"),

        // Atom One Light — counterpart to existing One Dark
        new ThemePreset("Atom One Light",        "#FFFAFAFA", "#FFEFEFEF", "#FF4078F2", "#FF383A42", "#FFA0A1A7"),

        // Cobalt2 by Wes Bos
        new ThemePreset("Cobalt2",               "#FF193549", "#FF1F4662", "#FFFFC600", "#FFFFFFFF", "#FF0088FF"),

        // Shades of Purple by Ahmad Awais
        new ThemePreset("Shades of Purple",      "#FF2D2B55", "#FF1E1E3F", "#FFFAD000", "#FFFFFFFF", "#FFA599E9"),

        // Material Darker — long-popular community theme
        new ThemePreset("Material Darker",       "#FF212121", "#FF2A2A2A", "#FFFF9800", "#FFEEFFFF", "#FF545454"),

        // Panda — calm pastel-on-dark
        new ThemePreset("Panda",                 "#FF292A2B", "#FF31353A", "#FFFF75B5", "#FFE6E6E6", "#FF676B79"),

        // Oceanic Next — classic
        new ThemePreset("Oceanic Next",          "#FF1B2B34", "#FF232E38", "#FF6699CC", "#FFCDD3DE", "#FF65737E"),

        // Snazzy Light — popular light-mode hipster pick
        new ThemePreset("Snazzy Light",          "#FFFFFFFF", "#FFF7F8F9", "#FFFF5C57", "#FF333333", "#FF888888"),

        // v1.16 — Navy + Copper (user request, based on a navy/gold-copper reference image).
        new ThemePreset("Navy & Copper",         "#FF0E2240", "#FF152D52", "#FFD4A14A", "#FFEFE6D3", "#FF8A9BB5"),

        // v1.19 -- Everforest Dark. Per user request, Background and Tile are
        // swapped relative to the published Everforest palette so Tile is the
        // darker of the two (background sits lighter, tiles read as inset).
        new ThemePreset("Everforest Dark",       "#FF374145", "#FF2D353B", "#FFA7C080", "#FFD3C6AA", "#FF859289"),
    };

    // ==================================================================
    // v1.20: Built-in Themes (Colors + Skin atomic combos)
    //
    // A "Theme" combines a named color palette with a specific skin to
    // create a coherent visual identity. Applying a theme sets BOTH the
    // colors and the ActiveSkin in one operation.
    //
    // Themes are grouped by Franchise so the browse popup can show
    // collapsible group headers. Names are intentionally on-the-nose
    // ("WoW Icecrown Citadel", "Borderlands Pandora") so people can
    // recognize the source at a glance even without playing the game.
    // ==================================================================
    public record BuiltInTheme(
        string Name,
        string Franchise,
        string Bg,
        string Tile,
        string Accent,
        string Text,
        string Muted,
        string SkinName);

    public static readonly BuiltInTheme[] BuiltInThemes = new[]
    {
        // v1.25.37: explicit Default theme so the Preset Themes cycler
        // shows "Default" on first open (not the first franchise entry).
        new BuiltInTheme("Default", "", DarkBackground, DarkTile, DarkAccent, DarkText, DarkMuted, "Default"),

        // v1.0.7: Generic built-in themes (sampled from natural landscapes).
        // Franchise themes (WoW, Fallout, etc.) moved to downloadable packs
        // on GitHub — see Settings → Updates → Theme Packs.
            new BuiltInTheme("Evergreen", "", "#FF0C140C", "#FF1A261A", "#FF6C9848", "#FFD4DCC8", "#FF688860", "Default"), // deep forest + meadow green
            new BuiltInTheme("Sandstone", "", "#FF100E0A", "#FF1E1C16", "#FFB8A070", "#FFE0DCD0", "#FF807860", "Retro"), // warm desert rock
            new BuiltInTheme("Deep Current", "", "#FF0A1014", "#FF141E24", "#FF5898A0", "#FFD0DCE0", "#FF608080", "Frosted"), // cool teal depths
            new BuiltInTheme("Morning Dew", "", "#FF0C0E0A", "#FF1A1E18", "#FFA8B880", "#FFDCE0D4", "#FF788870", "Paper"), // soft pale green
            new BuiltInTheme("Hearthwood", "", "#FF100C08", "#FF201A14", "#FFB87848", "#FFE0D8CC", "#FF887058", "Retro"), // rustic cabin warmth
            new BuiltInTheme("Terracotta", "", "#FF0E0C0C", "#FF1C1A1A", "#FFA86850", "#FFDCD8D4", "#FF806860", "Brutalist"), // earthy clay red
            new BuiltInTheme("Tidestone", "", "#FF12100C", "#FF201E18", "#FF5898A0", "#FFDCD8CC", "#FF807868", "Sharp"), // rock meets sea
            new BuiltInTheme("Forest Gold", "", "#FF0C140E", "#FF1A2618", "#FFC8B870", "#FFD8E0D0", "#FF688860", "Default"), // sunlit canopy
            new BuiltInTheme("Inlet", "", "#FF0A1214", "#FF142022", "#FFB87848", "#FFD0DCE0", "#FF607880", "Frosted"), // deep water warm glow
            new BuiltInTheme("Canopy", "", "#FF0E100C", "#FF1C1E1A", "#FF4C8840", "#FFD8DCD0", "#FF788870", "Default"), // dense overhead green
            new BuiltInTheme("Sage", "", "#FF0E0C0A", "#FF1C1A18", "#FFA8C088", "#FFDCE0D4", "#FF787060", "Paper"), // muted green on stone
            new BuiltInTheme("Clay Coast", "", "#FF0A0E12", "#FF141C22", "#FFA86850", "#FFD0D8DC", "#FF607078", "Brutalist"), // cool water warm clay
            new BuiltInTheme("Dusk Harbor", "", "#FF100E12", "#FF1E1C22", "#FF68A0A8", "#FFD8D8E0", "#FF787080", "Holographic"), // purple-gray twilight
            new BuiltInTheme("Fern", "", "#FF0A120A", "#FF162016", "#FF78B060", "#FFD4E0CC", "#FF588850", "Default"), // bright natural green
            new BuiltInTheme("Driftwood", "", "#FF100E0A", "#FF1E1A16", "#FF889870", "#FFDCD8CC", "#FF787058", "Retro"), // warm muted sage
            new BuiltInTheme("Glacier", "", "#FF0C0E10", "#FF1A1E22", "#FF78A8C0", "#FFD8DCE4", "#FF687880", "Frosted"), // cool icy blue-gray
            new BuiltInTheme("Amber Trail", "", "#FF0E0A08", "#FF1E1610", "#FFC8A050", "#FFE0D8C8", "#FF806840", "Retro"), // rich brown + gold
    };

    /// <summary>
    /// Returns built-in themes merged with any downloaded theme packs.
    /// Downloaded packs are loaded from %AppData%\fluidMonitor\themes\.
    /// </summary>
    public static List<BuiltInTheme> GetAllThemes()
    {
        var all = new List<BuiltInTheme>(BuiltInThemes);
        foreach (var t in ThemePackService.LoadAllInstalledThemes())
        {
            all.Add(new BuiltInTheme(
                t.Name, "", t.Bg, t.Tile, t.Accent, t.Text, t.Muted, t.Category));
        }
        return all;
    }

    /// <summary>Number of themes available to download (not yet installed).</summary>
    public static int AvailableDownloadCount { get; set; }

    /// <summary>Find the matching preset for current settings, or return the Custom entry (index 0).</summary>
    public static ThemePreset MatchPreset(AppSettings s, IEnumerable<ThemePreset>? allPresets = null)
    {
        var list = allPresets ?? Presets;
        foreach (var p in list)
        {
            if (string.IsNullOrEmpty(p.Bg)) continue;
            if (string.Equals(s.BackgroundColor, p.Bg,     StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.TileColor,       p.Tile,   StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.AccentColor,     p.Accent, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.TextColor,       p.Text,   StringComparison.OrdinalIgnoreCase) &&
                string.Equals(s.MutedTextColor,  p.Muted,  StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return Presets[0]; // Custom
    }

    public static ThemePreset UserPresetToThemePreset(UserColorPreset u)
        => new ThemePreset($"{u.Name} (preset)", u.BackgroundColor, u.TileColor, u.AccentColor, u.TextColor, u.MutedTextColor);

    /// <summary>Apply dark or light mode defaults to AppSettings colors (without overwriting user customization).</summary>
    public static void ApplyPreset(AppSettings s, ThemePreset preset)
    {
        s.BackgroundColor = preset.Bg;
        s.TileColor       = preset.Tile;
        s.AccentColor     = preset.Accent;
        s.TextColor       = preset.Text;
        s.MutedTextColor  = preset.Muted;
        var bg = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(preset.Bg);
        s.IsDarkMode = (bg.R + bg.G + bg.B) < 384;
    }

    public static void ApplyModeDefaults(AppSettings s)
    {
        if (s.IsDarkMode)
        {
            s.BackgroundColor = DarkBackground;
            s.TileColor       = DarkTile;
            s.AccentColor     = DarkAccent;
            s.TextColor       = DarkText;
            s.MutedTextColor  = DarkMuted;
        }
        else
        {
            s.BackgroundColor = LightBackground;
            s.TileColor       = LightTile;
            s.AccentColor     = LightAccent;
            s.TextColor       = LightText;
            s.MutedTextColor  = LightMuted;
            // v1.25.43: light mode needs full opacity — semi-transparent
            // light colors over a dark wallpaper look muddy/unreadable.
            s.Opacity = 1.0;
        }
    }

    public static void Apply(AppSettings s, ResourceDictionary res)
    {
        SetBrush(res, "BackgroundBrush", s.BackgroundColor, DarkBackground);
        SetBrush(res, "TileBrush",         s.TileColor,       DarkTile);
        // v1.25.43: force SkinWidgetOpacity to 1.0 for light palettes.
        // Light colors are unreadable when the skin opacity makes them
        // semi-transparent over a dark wallpaper.
        // v1.0.4: full light-background fix. When background is light,
        // skins may contain dark BackgroundBrush/TileBrush that override
        // the theme colors via merged dictionary precedence. Remove them.
        var bgColor = ParseColor(s.BackgroundColor, DarkBackground);
        bool isLightBg = (0.299 * bgColor.R + 0.587 * bgColor.G + 0.114 * bgColor.B) > 128;
        if (isLightBg)
        {
            foreach (var md in res.MergedDictionaries)
            {
                foreach (var key in new[] { "BackgroundBrush", "TileBrush", "SkinTileBackground", "SkinWidgetOpacity" })
                {
                    if (md.Contains(key))
                        md.Remove(key);
                }
            }
            res["SkinWidgetOpacity"] = 1.0;
            if (System.Windows.Application.Current?.MainWindow != null)
                System.Windows.Application.Current.MainWindow.Opacity = 1.0;
        }
        // v1.25.37: always sync SkinTileBackground with TileBrush.
        // Previously only synced for SolidColorBrush skins, which meant
        // light mode tiles stayed dark on skins with non-solid backgrounds.
        SetBrush(res, "SkinTileBackground", s.TileColor, DarkTile);
        SetBrush(res, "AccentBrush",     s.AccentColor,     DarkAccent);
        // v1.21: raw accent Color for effects (Glow traffic indicator)
        res["AccentGlowColor"] = ParseColor(s.AccentColor, DarkAccent);
        SetBrush(res, "TextBrush",       s.TextColor,       DarkText);
        // v1.20.2: apply MutedContrast slider. >1.0 blends muted toward text
        // (more visible), <1.0 blends toward background (more subdued).
        // 1.0 = use the muted color as-is.
        var effectiveMuted = AdjustMutedContrast(s.MutedTextColor, s.TextColor, s.BackgroundColor, s.MutedContrast);
        SetBrush(res, "MutedTextBrush",  effectiveMuted,    DarkMuted);

        res["TileWidth"]  = s.TileWidth;
        res["TileHeight"] = s.TileHeight;

        res["PrimaryFontSize"]        = BasePrimaryFontSize   + s.PrimaryFontSizeOffset;
        res["SecondaryFontSize"]      = BaseSecondaryFontSize + s.SecondaryFontSizeOffset;
        res["UnitFontSize"]           = BaseUnitFontSize      + s.PrimaryFontSizeOffset;
        res["IndicatorFontSize"]      = BaseIndicatorFontSize + s.IndicatorFontSizeOffset;
        res["SmallIndicatorFontSize"] = Math.Max(8.0, BaseIndicatorFontSize + s.IndicatorFontSizeOffset - 3);
        // v1.25.16: per-tile granular sizes. Defaults to whatever
        // IndicatorFontSize / SmallIndicatorFontSize resolved to; the
        // user-driven offsets are additive on top.
        res["ArrowFontSize"]          = BaseIndicatorFontSize + s.IndicatorFontSizeOffset + s.ArrowFontSizeOffset;
        res["DiskLabelFontSize"]      = Math.Max(8.0, BaseIndicatorFontSize + s.IndicatorFontSizeOffset - 3 + s.DiskLabelFontSizeOffset);
        // v1.25.20: clock date line. +2pt above Secondary so the date reads
        // as a real date, not a footnote.
        res["ClockDateFontSize"]      = BaseSecondaryFontSize + s.SecondaryFontSizeOffset + 2;

        // Resolve skin color slots (SkinTileBorderSource etc) from current theme colors.
        ApplySkinSlots(res, s.BackgroundColor, s.TileColor, s.AccentColor, s.TextColor, s.MutedTextColor);
    }

    public static void ApplyPopout(Fluid.Shared.Protocol.PopoutSettings p, ResourceDictionary res)
    {
        SetBrush(res, "BackgroundBrush", p.BackgroundColor, DarkBackground);
        SetBrush(res, "TileBrush",         p.TileColor,       DarkTile);
        if (res.Contains("SkinTileBackground") &&
            res["SkinTileBackground"] is SolidColorBrush)
            SetBrush(res, "SkinTileBackground", p.TileColor, DarkTile);
        SetBrush(res, "AccentBrush",     p.AccentColor,     DarkAccent);
        res["AccentGlowColor"] = ParseColor(p.AccentColor, DarkAccent);  // v1.21
        SetBrush(res, "TextBrush",       p.TextColor,       DarkText);
        SetBrush(res, "MutedTextBrush",  p.MutedTextColor,  DarkMuted);

        res["TileWidth"]  = p.TileWidth;
        res["TileHeight"] = p.TileHeight;

        res["PrimaryFontSize"]        = BasePrimaryFontSize   + p.PrimaryFontSizeOffset;
        res["SecondaryFontSize"]      = BaseSecondaryFontSize + p.SecondaryFontSizeOffset;
        res["UnitFontSize"]           = BaseUnitFontSize      + p.PrimaryFontSizeOffset;
        res["IndicatorFontSize"]      = BaseIndicatorFontSize;
        res["SmallIndicatorFontSize"] = Math.Max(8.0, BaseIndicatorFontSize - 3);
        // v1.25.16: popouts don't expose arrow/disk-label size sliders, so
        // they get the unmodified base sizes (matches the indicator default).
        res["ArrowFontSize"]          = BaseIndicatorFontSize;
        res["DiskLabelFontSize"]      = Math.Max(8.0, BaseIndicatorFontSize - 3);
        res["ClockDateFontSize"]      = BaseSecondaryFontSize + 2;

        ApplySkinSlots(res, p.BackgroundColor, p.TileColor, p.AccentColor, p.TextColor, p.MutedTextColor);
    }

    private static void SetBrush(ResourceDictionary res, string key, string hex, string fallbackHex)
    {
        Color color;
        try   { color = (Color)ColorConverter.ConvertFromString(hex); }
        catch { color = (Color)ColorConverter.ConvertFromString(fallbackHex); }

        if (res.Contains(key) && res[key] is SolidColorBrush existing && !existing.IsFrozen)
            existing.Color = color;
        else
            res[key] = new SolidColorBrush(color);
    }

    // ─── Skin color slot resolution ──────────────────────────────────────
    // Skins declare color slots as string keys (e.g., SkinTileBorderSource = "Muted")
    // plus alpha multipliers. This method walks the dictionary and synthesizes the
    // actual brushes from current theme colors so swapping themes restyles every skin.
    private static void ApplySkinSlots(ResourceDictionary res,
                                       string bgHex, string tileHex,
                                       string accentHex, string textHex, string mutedHex)
    {
        // Parse theme colors once (fall back to dark defaults on parse error)
        var bg     = ParseColor(bgHex,     DarkBackground);
        var tile   = ParseColor(tileHex,   DarkTile);
        var accent = ParseColor(accentHex, DarkAccent);
        var text   = ParseColor(textHex,   DarkText);
        var muted  = ParseColor(mutedHex,  DarkMuted);

        Color Resolve(string source) => source switch
        {
            "Background" => bg,
            "Tile"       => tile,
            "Accent"     => accent,
            "Text"       => text,
            "Muted"      => muted,
            "Transparent" => Colors.Transparent,
            _            => muted, // safest fallback for "unknown"
        };

        // Slot driver pairs: (brush key, source key, alpha key, fallback color)
        ApplySolidSlot(res, "SkinTileBorderBrush",   "SkinTileBorderSource",   "SkinTileBorderAlpha",   Resolve, muted);
        ApplySolidSlot(res, "SkinWidgetBorderBrush", "SkinWidgetBorderSource", "SkinWidgetBorderAlpha", Resolve, Colors.Transparent);

        // Standalone Color resources (not wrapped in a brush): SkinSheenColor, SkinHeaderBarColor
        ApplyColorSlot(res, "SkinSheenColor",     "SkinSheenSource",     "SkinSheenAlpha",     Resolve, Colors.Transparent);
        ApplyColorSlot(res, "SkinHeaderBarColor", "SkinHeaderBarSource", "SkinHeaderBarAlpha", Resolve, Colors.Transparent);

        // Drop shadow effect — rebuild whole effect so Color updates (Effect color can't be live-set on a frozen effect)
        ApplyShadowSlot(res, Resolve);

        // Tile background — synthesize gradient if skin opts in with SkinTileBackgroundKind="Glass"
        ApplyTileBackgroundSlot(res, accent, bg, tile);
    }

    private static void ApplySolidSlot(ResourceDictionary res, string brushKey,
                                       string srcKey, string alphaKey,
                                       Func<string, Color> resolve, Color fallback)
    {
        if (!res.Contains(srcKey))
        {
            // v1.23.1: skin didn't declare this slot -> remove the app-level
            // value a PREVIOUS skin wrote here, so lookup falls through to the
            // active skin's own merged-dict value or the Theme.xaml default.
            // (Built-in skins declare every slot; this protects user skins.)
            res.Remove(brushKey);
            return;
        }
        var src   = res[srcKey] as string ?? "";
        var alpha = res.Contains(alphaKey) && res[alphaKey] is double a ? a : 1.0;
        var c = resolve(src);
        c.A = (byte)Math.Clamp(c.A * alpha, 0.0, 255.0);
        if (res.Contains(brushKey) && res[brushKey] is SolidColorBrush existing && !existing.IsFrozen)
            existing.Color = c;
        else
            res[brushKey] = new SolidColorBrush(c);
    }

    private static void ApplyColorSlot(ResourceDictionary res, string colorKey,
                                       string srcKey, string alphaKey,
                                       Func<string, Color> resolve, Color fallback)
    {
        if (!res.Contains(srcKey))
        {
            res.Remove(colorKey); // v1.23.1: see ApplySolidSlot
            return;
        }
        var src   = res[srcKey] as string ?? "";
        var alpha = res.Contains(alphaKey) && res[alphaKey] is double a ? a : 1.0;
        var c = resolve(src);
        c.A = (byte)Math.Clamp(c.A * alpha, 0.0, 255.0);
        res[colorKey] = c;
    }

    private static void ApplyShadowSlot(ResourceDictionary res, Func<string, Color> resolve)
    {
        if (!res.Contains("SkinShadowSource"))
        {
            // v1.23.1: undeclared -> clear any app-level effect a previous skin
            // synthesized (e.g. Aurora's blur-40 accent glow), otherwise the
            // "gradient of light" follows the user to every later skin.
            res.Remove("SkinTileEffect");
            return;
        }
        var src   = res["SkinShadowSource"] as string ?? "";
        if (src == "None")
        {
            res["SkinTileEffect"] = null;
            return;
        }
        var alpha   = res.Contains("SkinShadowAlpha")  && res["SkinShadowAlpha"]  is double a  ? a  : 0.4;
        var depth   = res.Contains("SkinShadowDepth")  && res["SkinShadowDepth"]  is double d  ? d  : 2.0;
        var blur    = res.Contains("SkinShadowBlur")   && res["SkinShadowBlur"]   is double b  ? b  : 10.0;
        var c = resolve(src);
        res["SkinTileEffect"] = new System.Windows.Media.Effects.DropShadowEffect
        {
            ShadowDepth = depth,
            BlurRadius  = blur,
            Opacity     = alpha,
            Color       = Color.FromRgb(c.R, c.G, c.B),
        };
    }

    private static void ApplyTileBackgroundSlot(ResourceDictionary res, Color accent, Color bg, Color tile)
    {
        // Glass kind: synthesize a vertical gradient from accent (top) → background (mid) → tile (bottom),
        // with the alpha pattern that gave the original Glassmorphism look (55/33/22).
        var kind = res.Contains("SkinTileBackgroundKind") ? res["SkinTileBackgroundKind"] as string ?? "" : "";
        if (kind != "Glass")
        {
            // v1.23: the synthesized gradient below is written DIRECTLY into the
            // app-level dictionary, which shadows every later skin's own
            // SkinTileBackground (skins live in MergedDictionaries, and direct
            // entries always win). Without this Remove, switching from a Glass
            // skin to any non-Glass skin kept the gradient forever. Remove only
            // touches the direct entry — the new skin's merged value (or the
            // Theme.xaml default) shows through again.
            res.Remove("SkinTileBackground");
            return;
        }

        var top    = Color.FromArgb(0x55, accent.R, accent.G, accent.B);
        var mid    = Color.FromArgb(0x33, bg.R,     bg.G,     bg.B);
        var bottom = Color.FromArgb(0x22, tile.R,   tile.G,   tile.B);

        res["SkinTileBackground"] = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint   = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(top,    0.0),
                new GradientStop(mid,    0.5),
                new GradientStop(bottom, 1.0),
            }
        };
    }

    private static Color ParseColor(string hex, string fallbackHex)
    {
        try   { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return (Color)ColorConverter.ConvertFromString(fallbackHex); }
    }

    /// <summary>
    /// v1.20.2: Adjust muted text contrast at render time.
    /// contrast == 1.0 returns mutedHex unchanged.
    /// contrast >  1.0 blends muted toward textHex (more visible).
    /// contrast <  1.0 blends muted toward bgHex (more subdued).
    /// The blend factor scales linearly with the distance from 1.0, so 1.5
    /// is a 50% blend toward text, 0.5 is a 50% blend toward background.
    /// Returns a hex string. On parse failure, returns mutedHex as-is.
    /// </summary>
    public static string AdjustMutedContrast(string mutedHex, string textHex, string bgHex, double contrast)
    {
        if (Math.Abs(contrast - 1.0) < 0.01) return mutedHex;
        try
        {
            var m = (Color)ColorConverter.ConvertFromString(mutedHex);
            var t = (Color)ColorConverter.ConvertFromString(textHex);
            var b = (Color)ColorConverter.ConvertFromString(bgHex);
            // Clamp contrast to a sane range so a user moving the slider extreme
            // can't produce an invisible or paint-thick result.
            contrast = Math.Max(0.4, Math.Min(1.8, contrast));
            byte target_r, target_g, target_b;
            double factor;
            if (contrast > 1.0)
            {
                // Blend toward text. Factor 0 = unchanged, 1 = full text.
                factor = Math.Min(0.85, contrast - 1.0);   // cap so muted never fully becomes text
                target_r = t.R; target_g = t.G; target_b = t.B;
            }
            else
            {
                factor = Math.Min(0.85, 1.0 - contrast);
                target_r = b.R; target_g = b.G; target_b = b.B;
            }
            int nr = (int)Math.Round(m.R + (target_r - m.R) * factor);
            int ng = (int)Math.Round(m.G + (target_g - m.G) * factor);
            int nb = (int)Math.Round(m.B + (target_b - m.B) * factor);
            return $"#{m.A:X2}{Math.Clamp(nr,0,255):X2}{Math.Clamp(ng,0,255):X2}{Math.Clamp(nb,0,255):X2}";
        }
        catch
        {
            return mutedHex;
        }
    }
}
