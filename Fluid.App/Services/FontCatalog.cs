namespace Fluid.App.Services;

/// <summary>
/// Curated list of fonts that ship with Windows 10/11 by default.
/// Used by the Settings UI font dropdowns and by the dice button's
/// randomize-fonts feature. The "(default)" entry means "use the
/// current skin's SkinFontFamily" — no override.
///
/// We don't enumerate installed fonts via SystemFonts because that
/// can produce 200+ entries including symbol-only fonts and obscure
/// language packs that render badly in our small-text UI. A curated
/// list keeps the dropdown usable.
/// </summary>
public static class FontCatalog
{
    public const string DefaultEntry = "(skin default)";

    public static readonly string[] AllFonts = new[]
    {
        DefaultEntry,                  // index 0 = no override

        // --- System UI fonts ------------------------------------------
        "Segoe UI",
        "Segoe UI Variable",
        "Segoe UI Semibold",
        "Segoe UI Light",
        "Arial",
        "Tahoma",
        "Verdana",
        "Calibri",
        "Candara",

        // --- Serif ----------------------------------------------------
        "Georgia",
        "Cambria",
        "Constantia",
        "Palatino Linotype",
        "Times New Roman",
        "Book Antiqua",

        // --- Monospace ------------------------------------------------
        "Cascadia Mono",
        "Cascadia Code",
        "Consolas",
        "Courier New",
        "Lucida Console",

        // --- Display / character --------------------------------------
        "Impact",
        "Bahnschrift",
        "Bahnschrift SemiBold",
        "Bahnschrift Condensed",
        "Franklin Gothic Medium",
        "Century Gothic",
        "Trebuchet MS",
        "Comic Sans MS",
        "Lucida Sans",
        "Microsoft Sans Serif",

        // --- Handwritten / casual -------------------------------------
        "Ink Free",
        "Segoe Print",
        "Segoe Script",

        // --- Decorative -----------------------------------------------
        "Gabriola",
    };

    /// <summary>True when the user has explicitly picked a font (non-default).</summary>
    public static bool IsOverride(string? font)
        => !string.IsNullOrEmpty(font) && font != DefaultEntry;

    /// <summary>Pick a random font for the dice button. Excludes the default entry.</summary>
    public static string Random(System.Random rng)
        => AllFonts[rng.Next(1, AllFonts.Length)];
}
