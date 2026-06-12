using System;
using System.Text;
using System.Text.Json;
using Fluid.App.Models;

namespace Fluid.App.Services;

/// <summary>
/// Encodes/decodes the appearance section of AppSettings to/from a portable
/// string that users can copy, paste, and share. The string format is:
///
///     fluid:v1:&lt;base64-of-json&gt;
///
/// Prefix identifies the format and version so future schema changes can
/// reject or migrate old codes. Base64 makes the payload safe to paste into
/// Discord/Slack/email without escaping.
///
/// The exported payload contains EVERYTHING in the Settings Appearance section:
/// colors, skin, fonts, sync flags, size sliders, opacity, font size offsets.
/// Per user request -- "B+C, full Appearance section."
///
/// IMPORTANT: applying an imported code overwrites the current appearance.
/// Callers should push an undo snapshot before applying so the user can undo.
/// </summary>
public static class AppearanceShareCodec
{
    private const string Prefix = "fluid:v1:";

    /// <summary>The shape of the JSON payload inside the base64.</summary>
    public sealed class Payload
    {
        public int    SchemaVersion { get; set; } = 1;

        // Colors
        public string BackgroundColor { get; set; } = "";
        public string TileColor       { get; set; } = "";
        public string AccentColor     { get; set; } = "";
        public string TextColor       { get; set; } = "";
        public string MutedTextColor  { get; set; } = "";
        public bool   IsDarkMode      { get; set; } = true;

        // v1.19: when the exporter's current colors EXACTLY match a named
        // entry (built-in or CustomColor), this carries the name so the
        // receiver can add the same name to their CustomColors list (tagged
        // as imported). Empty when colors are custom/uncategorized.
        public string ColorPresetName { get; set; } = "";

        // Skin
        public string ActiveSkin      { get; set; } = "";

        // Fonts
        public string PrimaryFont          { get; set; } = "";
        public string SecondaryFont        { get; set; } = "";
        public string IndicatorFont        { get; set; } = "";
        public bool   SyncFonts            { get; set; } = true;
        public bool   RandomizeFontsOnDice { get; set; } = false;

        // Sizes (option B+C: include the slider state too)
        public double UiScale        { get; set; } = 1.0;
        public double TileWidth      { get; set; } = 130;
        public double TileHeight     { get; set; } = 110;
        public double Opacity        { get; set; } = 0.90;
        public int    PrimaryFontSizeOffset   { get; set; } = 0;
        public int    SecondaryFontSizeOffset { get; set; } = 0;
        public int    IndicatorFontSizeOffset { get; set; } = 0;
    }

    public static string Export(AppSettings s)
    {
        var p = new Payload
        {
            BackgroundColor = s.BackgroundColor ?? "",
            TileColor       = s.TileColor       ?? "",
            AccentColor     = s.AccentColor     ?? "",
            TextColor       = s.TextColor       ?? "",
            MutedTextColor  = s.MutedTextColor  ?? "",
            IsDarkMode      = s.IsDarkMode,
            ColorPresetName = ResolveCurrentColorName(s),  // v1.19
            ActiveSkin      = s.ActiveSkin      ?? "",
            PrimaryFont     = s.PrimaryFont     ?? "",
            SecondaryFont   = s.SecondaryFont   ?? "",
            IndicatorFont   = s.IndicatorFont   ?? "",
            SyncFonts            = s.SyncFonts,
            RandomizeFontsOnDice = s.RandomizeFontsOnDice,
            UiScale          = s.UiScale,
            TileWidth        = s.TileWidth,
            TileHeight       = s.TileHeight,
            Opacity          = s.Opacity,
            PrimaryFontSizeOffset   = s.PrimaryFontSizeOffset,
            SecondaryFontSizeOffset = s.SecondaryFontSizeOffset,
            IndicatorFontSizeOffset = s.IndicatorFontSizeOffset,
        };
        var json = JsonSerializer.Serialize(p);
        var b64  = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return Prefix + b64;
    }

    // v1.19: If the live colors EXACTLY match a known named preset (built-in
    // or user CustomColor), return that name so it travels with the share
    // code. Empty otherwise -- importer sees an unnamed color set.
    // Matching all 5 hex values is intentional: any manual tweak after
    // selecting a preset means the colors no longer represent that preset.
    private static string ResolveCurrentColorName(AppSettings s)
    {
        bool Match(string bg, string tile, string acc, string text, string muted) =>
            string.Equals(bg,    s.BackgroundColor, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(tile,  s.TileColor,       StringComparison.OrdinalIgnoreCase) &&
            string.Equals(acc,   s.AccentColor,     StringComparison.OrdinalIgnoreCase) &&
            string.Equals(text,  s.TextColor,       StringComparison.OrdinalIgnoreCase) &&
            string.Equals(muted, s.MutedTextColor,  StringComparison.OrdinalIgnoreCase);

        foreach (var p in ThemeApplier.Presets)
            if (!string.IsNullOrEmpty(p.Bg) && Match(p.Bg, p.Tile, p.Accent, p.Text, p.Muted))
                return p.Name;
        foreach (var c in s.CustomColors)
            if (Match(c.BackgroundColor, c.TileColor, c.AccentColor, c.TextColor, c.MutedTextColor))
                return c.Name;
        return "";
    }

    /// <summary>Returns true and writes to s on success; false on any parse/format error.</summary>
    public static bool TryImport(string code, AppSettings s, out string errorMessage)
    {
        errorMessage = "";
        if (string.IsNullOrWhiteSpace(code))
        {
            errorMessage = "Code is empty.";
            return false;
        }
        code = code.Trim();
        if (!code.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"Code does not start with '{Prefix}'.";
            return false;
        }
        var b64 = code.Substring(Prefix.Length);
        Payload? p;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            p = JsonSerializer.Deserialize<Payload>(json);
        }
        catch (Exception ex)
        {
            errorMessage = "Code is corrupted or not valid: " + ex.Message;
            return false;
        }
        if (p == null)
        {
            errorMessage = "Code decoded to nothing.";
            return false;
        }
        if (p.SchemaVersion != 1)
        {
            errorMessage = $"Code is from a future or incompatible schema (v{p.SchemaVersion}).";
            return false;
        }

        // Apply to settings -- treat empty strings as "leave alone"
        if (!string.IsNullOrEmpty(p.BackgroundColor)) s.BackgroundColor = p.BackgroundColor;
        if (!string.IsNullOrEmpty(p.TileColor))       s.TileColor       = p.TileColor;
        if (!string.IsNullOrEmpty(p.AccentColor))     s.AccentColor     = p.AccentColor;
        if (!string.IsNullOrEmpty(p.TextColor))       s.TextColor       = p.TextColor;
        if (!string.IsNullOrEmpty(p.MutedTextColor))  s.MutedTextColor  = p.MutedTextColor;
        s.IsDarkMode = p.IsDarkMode;
        if (!string.IsNullOrEmpty(p.ActiveSkin))      s.ActiveSkin      = p.ActiveSkin;
        s.PrimaryFont   = p.PrimaryFont   ?? "";
        s.SecondaryFont = p.SecondaryFont ?? "";
        s.IndicatorFont = p.IndicatorFont ?? "";
        s.SyncFonts            = p.SyncFonts;
        s.RandomizeFontsOnDice = p.RandomizeFontsOnDice;
        if (p.UiScale     > 0) s.UiScale     = p.UiScale;
        if (p.TileWidth  > 20) s.TileWidth   = p.TileWidth;
        if (p.TileHeight > 20) s.TileHeight  = p.TileHeight;
        if (p.Opacity    > 0 && p.Opacity <= 1) s.Opacity = p.Opacity;
        s.PrimaryFontSizeOffset   = Math.Clamp(p.PrimaryFontSizeOffset,   -5, 5);
        s.SecondaryFontSizeOffset = Math.Clamp(p.SecondaryFontSizeOffset, -5, 5);
        s.IndicatorFontSizeOffset = Math.Clamp(p.IndicatorFontSizeOffset, -5, 5);

        // v1.19: if the share code names a color preset AND that name doesn't
        // already exist in the receiver's built-ins or CustomColors, add it
        // to the receiver's CustomColors tagged IsImported=true so they can
        // see "(i)" in the cycler dropdown and a tooltip explaining where it
        // came from. This works even if the receiver hasn't created any
        // custom colors yet -- we just append to an empty list.
        if (!string.IsNullOrEmpty(p.ColorPresetName))
        {
            bool builtinMatch = false;
            foreach (var preset in ThemeApplier.Presets)
                if (string.Equals(preset.Name, p.ColorPresetName, StringComparison.Ordinal)) { builtinMatch = true; break; }
            bool customMatch = false;
            foreach (var cc in s.CustomColors)
                if (string.Equals(cc.Name, p.ColorPresetName, StringComparison.Ordinal)) { customMatch = true; break; }
            if (!builtinMatch && !customMatch)
            {
                s.CustomColors.Add(new CustomColor
                {
                    Name            = p.ColorPresetName,
                    BackgroundColor = p.BackgroundColor,
                    TileColor       = p.TileColor,
                    AccentColor     = p.AccentColor,
                    TextColor       = p.TextColor,
                    MutedTextColor  = p.MutedTextColor,
                    IsImported      = true,
                    ImportedFrom    = "share code",
                });
            }
        }
        return true;
    }

    /// <summary>Produce a random valid appearance code for testing purposes.</summary>
    public static string MakeRandom(Random rng, System.Collections.Generic.IList<string> skinNames, System.Collections.Generic.IList<ThemeApplier.ThemePreset> themes)
    {
        // Pick a random non-Custom theme and skin
        var theme = themes[rng.Next(1, themes.Count)];
        var skin  = skinNames.Count > 0 ? skinNames[rng.Next(skinNames.Count)] : "Default";
        var primary = FontCatalog.Random(rng);
        var secondary = FontCatalog.Random(rng);
        var indicator = FontCatalog.Random(rng);
        var p = new Payload
        {
            BackgroundColor = theme.Bg,
            TileColor       = theme.Tile,
            AccentColor     = theme.Accent,
            TextColor       = theme.Text,
            MutedTextColor  = theme.Muted,
            IsDarkMode      = rng.Next(2) == 0,
            ActiveSkin      = skin,
            PrimaryFont     = primary == FontCatalog.DefaultEntry ? "" : primary,
            SecondaryFont   = secondary == FontCatalog.DefaultEntry ? "" : secondary,
            IndicatorFont   = indicator == FontCatalog.DefaultEntry ? "" : indicator,
            SyncFonts            = rng.Next(2) == 0,
            RandomizeFontsOnDice = rng.Next(2) == 0,
            UiScale          = 1.0,
            TileWidth        = 110 + rng.Next(40),
            TileHeight       = 90  + rng.Next(40),
            Opacity          = 0.5 + rng.NextDouble() * 0.5,
            PrimaryFontSizeOffset   = rng.Next(-2, 3),
            SecondaryFontSizeOffset = rng.Next(-2, 3),
            IndicatorFontSizeOffset = rng.Next(-2, 3),
        };
        var json = JsonSerializer.Serialize(p);
        var b64  = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        return Prefix + b64;
    }
}
