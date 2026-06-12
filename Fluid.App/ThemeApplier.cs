using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Fluid.App.Models;

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

        // ---- Spyro (8) — Spyro 1 home worlds + Spyro + Sparx ----
        // v1.25.26: replaced the mixed 10-theme set with the canonical
        // Spyro 1 lineup. Colors traced to wiki descriptions and the F4F
        // crystal dragon statue colors (each home world has a canonical
        // crystallized dragon hue): Artisans green, Peace Keepers red,
        // and so on. Spyro himself is purple+gold; Sparx is the golden
        // yellow dragonfly health meter (gold->blue->green as Spyro
        // takes hits).
        new BuiltInTheme("Spyro Artisans", "Spyro", "#FF100E08", "#FF1E1C12", "#FF58B848", "#FFE4E8D0", "#FF7E8868", "Paper"), // warm stone castles + lush green meadows
        new BuiltInTheme("Spyro Peace Keepers", "Spyro", "#FF141008", "#FF261C10", "#FFD8A040", "#FFE8DCC0", "#FF907848", "Retro"), // desert canyon sandstone + military ochre tents
        new BuiltInTheme("Spyro Magic Crafters", "Spyro", "#FF080E1E", "#FF101C30", "#FF68C8F0", "#FFDCE8F8", "#FF7090B8", "Frosted"), // alpine ice peaks + crystalline sky blue
        new BuiltInTheme("Spyro Beast Makers", "Spyro", "#FF080C08", "#FF121814", "#FF48A838", "#FFC8D0C0", "#FF606850", "Brutalist"), // murky dark swamp + sickly moss green
        new BuiltInTheme("Spyro Dream Weavers", "Spyro", "#FF140820", "#FF201230", "#FFC060D0", "#FFE8D8F0", "#FF907898", "Holographic"), // surreal dream castles + mystical violet-pink
        new BuiltInTheme("Spyro Gnasty Gnorc", "Spyro", "#FF101008", "#FF1E1C10", "#FFB8B830", "#FFE0E0C0", "#FF808060", "Brutalist"), // industrial junkyard + Gnasty grimy yellow-green
        new BuiltInTheme("Spyro", "Spyro", "#FF14082A", "#FF22123E", "#FFE8B030", "#FFF0E0D0", "#FF8A7AB0", "Aurora"), // purple dragon body + golden horns and wings
        new BuiltInTheme("Spyro Sparx",          "Spyro", "#FF18140A", "#FF2A2410", "#FFFFD030", "#FFEFE8C0", "#FF9C9058", "Retro"),        // golden yellow dragonfly (full health)

        // ---- WoW (38) — 13 races + 8 continents + 17 iconic zones ----
        // v1.25.22: palettes redone from canonical art/screenshot research
        // rather than guesses. Races draw from heritage armor emblems + iconic
        // racial mounts/cities; continents pull dominant terrain palettes
        // from in-game screenshots; zones target signature colors called out
        // in community/wiki descriptions (e.g. Zangarmarsh "deep blues, teals,
        // soft neon greens"; Revendreth gothic crimson + ruby; Ardenweald
        // intense saturated blue with mossy greens; Grizzly Hills redwood
        // pine + snow + warm trapper lodge tones).

        // -- Races (13) -- v1.25.33 deep palette redo from canonical
        // research. Each race traced to Wowpedia city/heritage references:
        // Horde
        // Orc: Orgrimmar built on "red clay of Durotar", Warsong/Blackrock
        // heritage in red + dark iron spiked architecture, green skin.
        new BuiltInTheme("WoW Orc",         "WoW",  "#FF14060A", "#FF260C12", "#FFC0341E", "#FFE5C8B8", "#FF8F6450", "Brutalist"),  // Horde red over dark iron + Durotar clay
        // Tauren: Wowhead Heritage = "earth tones, browns, greens, creams"
        // + red war paint symbols, Mulgore green plains.
        new BuiltInTheme("WoW Tauren",      "WoW",  "#FF1A0F08", "#FF2E1C10", "#FFC85E2C", "#FFEFDCC0", "#FFA68056", "Paper"),       // earthen tan + red war paint
        // Troll: Darkspear heritage armor "orange and red tints", tiki
        // masks, jungle voodoo. NOT generic purple.
        new BuiltInTheme("WoW Troll", "WoW", "#FF081008", "#FF101C10", "#FF40B840", "#FFC8E0C0", "#FF608850", "Brutalist"), // Zul'Gurub/Amani jungle green + tribal moss
        // Undead/Forsaken: Heritage armor "clad in dark purple" + Lordaeron
        // crest + skulls. Forsaken canonical decay palette.
        new BuiltInTheme("WoW Undead", "WoW", "#FF080A0C", "#FF141618", "#FF68B048", "#FFC8D0C8", "#FF6E7870", "Ink"), // Tirisfal plague-blight green + Undercity ashen
        // Blood Elf: Silvermoon crimson + fel crystal green. v1.25.37:
        // accent changed from ruby to fel green -- the glowing green eyes
        // and Sunwell corruption IS the Blood Elf identity.
        new BuiltInTheme("WoW Blood Elf",   "WoW",  "#FF1A0608", "#FF2E0E12", "#FF33FF44", "#FFF0D0B0", "#FFA07060", "Sharp"),       // crimson Silvermoon + neon fel green
        // Goblin: Cartel/Bilgewater industrial yellow-green chemical hazard.
        new BuiltInTheme("WoW Goblin", "WoW", "#FF0A0E08", "#FF141C0E", "#FF40C838", "#FFD0E0C0", "#FF6E8858", "Retro"), // Kezan goblin-green industrial tropical
        // Pandaren: Pandaria jade + cream + bamboo. Existing was solid.
        new BuiltInTheme("WoW Pandaren",    "WoW",  "#FF0E1812", "#FF1A2A1E", "#FF3CB888", "#FFD8E8DC", "#FF7A968A", "Paper"),       // Pandaria jade green + cream
        // Alliance
        // Human: Stormwind "Lion's Heritage" rich blue + gold lion crest.
        new BuiltInTheme("WoW Human", "WoW", "#FF080E1A", "#FF101A2E", "#FFC8A030", "#FFE8E0D0", "#FF7080A0", "Sharp"), // Stormwind royal blue + lion gold banner
        // Dwarf: Bronzebeard Heritage forged in Great Forge. Bronze + molten
        // metal + Ironforge ember.
        new BuiltInTheme("WoW Dwarf", "WoW", "#FF120806", "#FF201008", "#FFE08028", "#FFF0D8B0", "#FF907050", "Retro"), // Ironforge molten copper + dark volcanic stone
        // Night Elf: v1.25.37 earthy Teldrassil bark with pale mint moonwell
        // glow. Previous purple amethyst replaced with the iconic teal/green.
        new BuiltInTheme("WoW Night Elf",        "WoW",  "#FF0A0806", "#FF18140E", "#FF90DDC0", "#FFD0D8CC", "#FF687060", "Aurora"),      // Teldrassil bark + pale mint moonwell
        new BuiltInTheme("WoW Night Elf Grove",  "WoW",  "#FF080A06", "#FF14180E", "#FF80B8CC", "#FFC8D8D0", "#FF607858", "Frosted"),    // mossy oak + lichen steel-blue
        // Gnome: Gnomeregan tinker workshop brass + cheerful invention.
        new BuiltInTheme("WoW Gnome", "WoW", "#FF0A100A", "#FF141C14", "#FF50D050", "#FFD0E0D0", "#FF608860", "Retro"), // Gnomeregan irradiated toxic green + mech
        // Draenei: Exodar crystal ship. Heritage = Telhamat (purple/maroon)
        // or Lost Embaari (blue/purple). Light-infused naaru crystal.
        new BuiltInTheme("WoW Draenei", "WoW", "#FF08081C", "#FF101430", "#FF5088E0", "#FFD8E0F8", "#FF6878A8", "Holographic"), // Exodar naaru crystal blue + holy light
        // Worgen: Gilneas Victorian Gothic — grey stone, black slate, dim
        // gas-lamp amber under perpetual rain.
        new BuiltInTheme("WoW Worgen",      "WoW",  "#FF0A0A0E", "#FF14141A", "#FFA09080", "#FFD8D0C8", "#FF74706A", "Ink"),         // Gilneas Victorian stone grey


        // -- Continents (8) -- v1.25.33 deep redo with per-continent identity.
        // Eastern Kingdoms: Alliance heartland, autumnal Elwynn forest +
        // Khaz Modan mountain stone. Old-world classic vanilla feel.
        // Distinct from Human race (which is pure Stormwind royal blue).
        new BuiltInTheme("WoW Eastern Kingdoms", "WoW", "#FF0C1014", "#FF181E26", "#FF6098B8", "#FFDDE5EA", "#FF788C9C", "Default"),    // old-world Alliance stone + sea
        // Kalimdor: night elf forest + tauren plains + orc Durotar red clay.
        // Verdant west continent. Existing palette is solid green plains.
        new BuiltInTheme("WoW Kalimdor",        "WoW", "#FF0E1408", "#FF1C2412", "#FF7AB840", "#FFDDEACA", "#FF7E906A", "Paper"),       // verdant forests + Mulgore plains
        // Northrend: icy continent, Lich King realm. Differentiate from
        // Icecrown Citadel (zone) by being lighter aurora blue, not saronite.
        new BuiltInTheme("WoW Northrend",       "WoW", "#FF080E1A", "#FF14202E", "#FF80C8E8", "#FFDDEEF5", "#FF7898B0", "Frosted"),     // frozen north aurora blue
        // Outland: shattered Draenor in Twisting Nether, signature hellish
        // red sky over fel-touched lands. Hellfire + fel undertone.
        new BuiltInTheme("WoW Outland",         "WoW", "#FF14080A", "#FF240E12", "#FFE85028", "#FFEFD2BC", "#FFA07868", "Brutalist"),   // Hellfire red + Twisting Nether
        // Pandaria: mist-shrouded continent of jade + Vale of Eternal Blossoms
        // golden lotus + cherry blossom. Asian-inspired hand-painted color.
        new BuiltInTheme("WoW Pandaria",        "WoW", "#FF0C1814", "#FF182820", "#FF48C098", "#FFDDEBE0", "#FF7A988A", "Paper"),       // jade green + golden vale
        // Draenor: raw uncorrupted savage Iron Horde world. Volcanic +
        // weathered iron + rust. Frostfire + Tanaan war-torn red-orange.
        new BuiltInTheme("WoW Draenor",         "WoW", "#FF180C06", "#FF2A1C10", "#FFE07028", "#FFEFD0B0", "#FF9C7058", "Retro"),       // Iron Horde rust + volcanic
        // Broken Isles: Legion expansion, fel-tinged arcane Suramar +
        // demonic corruption. Distinct from Night Elf lunar (which is
        // pure soft lavender) — Broken Isles leans Legion fel-purple.
        new BuiltInTheme("WoW Broken Isles",    "WoW", "#FF0A0818", "#FF161028", "#FFB04AE0", "#FFE8D8F0", "#FF8C70A8", "Aurora"),      // fel arcane purple corruption
        // Dragon Isles: home of dragonflights — Red (Alexstrasza fire),
        // Bronze (Nozdormu time gold), Black (Neltharion obsidian). Signature
        // continent = dragonfire amber + dragonscale shimmer.
        new BuiltInTheme("WoW Dragon Isles",    "WoW", "#FF180E04", "#FF2A1C0E", "#FFFFB438", "#FFEFE0BC", "#FFA88A60", "Holographic"), // dragonfire amber + bronze


        // -- Iconic zones (17) -- v1.25.33 deep redo from canonical
        // Wowpedia/community descriptions for each location.
        // Icecrown Citadel: "cathedral of blades and claws made entirely
        // of saronite" + Scourge necrotic glow. Black saronite + sickly
        // green Scourge eye, NOT just ice cyan (which is Northrend).
        new BuiltInTheme("WoW Icecrown Citadel",   "WoW",  "#FF080A12", "#FF101824", "#FF50E090", "#FFCCEAD2", "#FF608878", "Brutalist"),  // saronite black + Scourge green
        // Grizzly Hills: "stunning sinister pine forest", waterfalls,
        // Amberpine Lodge warm cabin amber + redwood pine. Solid.
        new BuiltInTheme("WoW Grizzly Hills",      "WoW",  "#FF0E1208", "#FF1C2410", "#FFC06028", "#FFEFDDC0", "#FF8A7058", "Paper"),     // redwood pine + Amberpine cabin amber
        // Nagrand: "vast openness, floating islands, Oshu'gun crystal".
        // Bright cyan sky + emerald grass + jade open feel.
        new BuiltInTheme("WoW Nagrand",            "WoW",  "#FF0E1A18", "#FF182C2A", "#FF60D8A8", "#FFDDEFE0", "#FF7CA08C", "Aurora"),     // Outland sky + jade grass + crystal
        // Kun-Lai Summit: "majestic mountains, autumnal plains, frigid
        // northern peaks". Warm autumn maple over snowy summit stone.
        new BuiltInTheme("WoW Kun-Lai Summit",     "WoW",  "#FF0E1218", "#FF1A1E28", "#FFE8703C", "#FFEFD8C8", "#FF947868", "Frosted"),   // autumnal maple over alpine stone
        // Howling Fjord: "stormy fjord, dark forests, treacherous cliffs,
        // vrykul Viking" Norse Nordic coast. Stormy slate.
        new BuiltInTheme("WoW Howling Fjord",      "WoW",  "#FF0A1014", "#FF181E26", "#FF5894A8", "#FFD8DFE5", "#FF6C7E88", "Default"),    // vrykul stormy fjord slate
        // Zangarmarsh: alien fungal swamp, bioluminescent giant mushrooms,
        // pre-Outland Sea of Zangar. Deep neon turquoise + spore mushroom.
        new BuiltInTheme("WoW Zangarmarsh",        "WoW",  "#FF0A1820", "#FF142836", "#FF30E0C0", "#FFD0EEE5", "#FF6E9A98", "Holographic"),// alien fungal turquoise + spore
        // Stormsong Valley: Kul Tiran rolling emerald fields + stormy sea
        // + Tidesage shrines. Coastal hayfield emerald.
        new BuiltInTheme("WoW Stormsong Valley",   "WoW",  "#FF0A1416", "#FF182428", "#FF48BC90", "#FFDDEDDC", "#FF749080", "Default"),    // Tidesage emerald + stormy coast
        // Ardenweald: "moonless night, glowing blue forests, faerie dust,
        // dream trees". Night fae bioluminescent.
        new BuiltInTheme("WoW Ardenweald",         "WoW",  "#FF08081E", "#FF121432", "#FF40E0E0", "#FFD8E8F5", "#FF7090B0", "Aurora"),     // night fae bioluminescent cyan
        // Revendreth: "gothic, scorched graveyards, vampiric, red sky
        // box, imposing castles". Gothic crimson.
        new BuiltInTheme("WoW Revendreth",         "WoW",  "#FF12060A", "#FF240C12", "#FFD42038", "#FFEFC8CC", "#FF8E5860", "Ink"),        // Venthyr gothic crimson
        // Suramar: "Nightborne arcane city, ley lines, nighthold". Pure
        // arcane violet over night sky. Distinct from race purples.
        new BuiltInTheme("WoW Suramar",            "WoW",  "#FF0A0824", "#FF161038", "#FFC880FF", "#FFE8DAFA", "#FF8C70B8", "Holographic"),// Nightborne arcane violet
        // Shadowmoon Valley (Draenor): "lush moor engulfed in eternal
        // night, lit by Pale Lady moon, relaxing bluish hue". Starlit ash blue.
        new BuiltInTheme("WoW Shadowmoon Valley",  "WoW",  "#FF06081C", "#FF101430", "#FF6098FF", "#FFD8DEF2", "#FF7080A8", "Aurora"),     // Draenor starlit Pale Lady blue
        // Stranglethorn Vale: dense tropical jungle, troll ziggurats,
        // Booty Bay. Lush emerald jungle canopy.
        new BuiltInTheme("WoW Stranglethorn Vale", "WoW",  "#FF061410", "#FF12241A", "#FF38B85C", "#FFD0E8D5", "#FF6E8C78", "Paper"),      // dense tropical jungle
        // Tirisfal Glades: "eternally gloomy and dark sky, tainted and
        // melancholy, derelict farmsteads". Forsaken sickly green-yellow.
        new BuiltInTheme("WoW Tirisfal Glades",    "WoW",  "#FF0C1208", "#FF181E12", "#FF8AAE38", "#FFD8DCB0", "#FF7C8458", "Ink"),         // Forsaken sickly plague yellow-green
        // Sholazar Basin: "tropical jungle in midst of Northrend, hot
        // springs, animals you'd find in Africa". Lush amber-gold tropics.
        new BuiltInTheme("WoW Sholazar Basin",     "WoW",  "#FF12160A", "#FF20280F", "#FFFFB028", "#FFEEDDB8", "#FF98855A", "Paper"),      // Titan tropical jungle amber
        // Vashj'ir: underwater zone, naga ruins, Lady Vashj's domain.
        // Deep ocean cyan with sun-rays. Solid.
        new BuiltInTheme("WoW Vashj'ir",           "WoW",  "#FF061620", "#FF122838", "#FF30A8D8", "#FFCCE0EE", "#FF6890A8", "Holographic"),// deep ocean naga cyan
        // Ashenvale: deep elven forest with "cypresses and ochres,
        // chestnuts and viridians" + "violets and purples" magic baked
        // in trees. NOT cyan (was wrong). Deep emerald + magic violet hint.
        new BuiltInTheme("WoW Ashenvale",          "WoW",  "#FF0A1410", "#FF142420", "#FF5CB880", "#FFDDEDDC", "#FF789480", "Paper"),     // night elf forest emerald + magic
        // Felwood: fel-corrupted dead forest, sickly toxic. Solid.
        new BuiltInTheme("WoW Felwood",            "WoW",  "#FF0E1408", "#FF1A2010", "#FF9FE848", "#FFE0E8C0", "#FF8C9A5A", "Brutalist"),  // fel-corrupted toxic neon green



        // ---- RuneScape (8) ----
        // v1.25.34: deep redo from canonical OSRS Wiki visual research.
        // Eight themes cover combat triangle (Bronze, Rune, Dragonhide,
        // Magic), iconic locations (Wilderness, Tutorial Island, Morytania),
        // and the Corrupted Gauntlet endgame minigame.
        //
        // Combat Bronze: starter armor + Lumbridge/Falador early-game
        // metalwork. Paper gold for that nostalgic newbie look.
        new BuiltInTheme("RuneScape Combat Bronze",      "RuneScape", "#FF14100A", "#FF22190E", "#FFE0982E", "#FFEFE0BC", "#FF94805A", "Paper"),       // starter bronze metalwork
        // Combat Rune: per Wiki "rune weapons are cyan in colour" +
        // "instantly recognizable teal/cyan hue, symbolizing a milestone".
        // The iconic mid-game F2P plate.
        new BuiltInTheme("RuneScape Rune Plate",         "RuneScape", "#FF0A1418", "#FF152428", "#FF40D8E0", "#FFD8EAEC", "#FF7098A0", "Sharp"),       // iconic rune cyan mid-game
        // Dragonhide Ranger: per Wiki "ranged armour made from green
        // dragonhide" — the iconic ranger green. Combat triangle ranged.
        new BuiltInTheme("RuneScape Dragonhide",         "RuneScape", "#FF0A1208", "#FF142010", "#FF6CB050", "#FFDDE8C8", "#FF788C68", "Paper"),       // green dragonhide ranger
        // Magic Robes: blue mage robes, Mage Arena. Combat triangle magic.
        new BuiltInTheme("RuneScape Magic Robes",        "RuneScape", "#FF080A1C", "#FF101232", "#FF6080FF", "#FFD8E0FA", "#FF7888B8", "Frosted"),     // mage robe arcane blue
        // Tutorial Island: nostalgic starter — bright grass meadow + sunny
        // yellow welcome. Where every OSRS journey begins.
        new BuiltInTheme("RuneScape Tutorial Island",    "RuneScape", "#FF18221A", "#FF283A28", "#FFFFD83A", "#FFF8FFE8", "#FF98AF7E", "Retro"),       // starter meadow + welcome gold
        // Wilderness: per Wiki "the area that is now the Wilderness was
        // formerly known as Forinthry" — burned to ash, lava-filled, PvP
        // death zone. Blood red over scorched earth.
        new BuiltInTheme("RuneScape Wilderness",         "RuneScape", "#FF120608", "#FF22090E", "#FFD42820", "#FFEFC8C0", "#FF94605A", "Brutalist"),   // scorched Forinthry blood red
        // Morytania: per Wiki "swamplands, Lord Drakan vampyre territory,
        // blood tithes, Castle Drakan". Gothic vampyric blood-purple swamp.
        new BuiltInTheme("RuneScape Morytania",          "RuneScape", "#FF0E0814", "#FF1E1024", "#FF9038B0", "#FFE0D2EC", "#FF8068A0", "Ink"),         // Drakan vampyre blood-purple
        // Corrupted Gauntlet: endgame Crystal Forest minigame, signature
        // pink-cyan corrupted crystal aesthetic.
        new BuiltInTheme("RuneScape Corrupted Gauntlet", "RuneScape", "#FF081820", "#FF142838", "#FF44E6F0", "#FFD0EAF0", "#FF6C8E9C", "Holographic"), // corrupted crystal cyan

        // ---- League of Legends (12) — Runeterra regions ----
        // v1.25.27: palettes redone from official Riot/Leaguepedia sources.
        // Demacia "blue, white, and gold" (per Leaguepedia + Petricite).
        // Noxus = blood red + black militaristic empire. Ionia = Spirit
        // Blossom soft pink + lilac + jade per Riot's published design.
        // Freljord = pale True Ice cyan. Shurima = desert gold sands.
        // Bilgewater = Blue Flame Isles teal + lantern orange. Piltover
        // Art Deco gold + cyan; Zaun Art Nouveau acidic toxic green
        // (smogged twilight undercity per Arcane). Targon celestial purple
        // + cosmic gold. Void eldritch purple. Ixtal jungle vine green.
        // Bandle City yordle warm yellow-orange whimsy.
        new BuiltInTheme("League Demacia", "League of Legends", "#FF0A1424", "#FF142238", "#FFB8942E", "#FFE8E8F0", "#FF7C8AAA", "Sharp"), // royal blue + subdued regal gold (less yellow)
        new BuiltInTheme("League Noxus",       "League of Legends", "#FF120808", "#FF221010", "#FFC81818", "#FFEEC8C8", "#FF8E5C5C", "Brutalist"),    // blood red + iron black
        new BuiltInTheme("League Ionia",       "League of Legends", "#FF120A18", "#FF1F1428", "#FFE890C0", "#FFEFDDEC", "#FF9888A0", "Aurora"),       // Spirit Blossom pink/lilac
        new BuiltInTheme("League Freljord",    "League of Legends", "#FF0A1A28", "#FF142C3C", "#FF98E8FF", "#FFE0F0F8", "#FF7C98AC", "Frosted"),      // True Ice pale cyan
        new BuiltInTheme("League Shurima",     "League of Legends", "#FF1A1408", "#FF2A2010", "#FFE8B83C", "#FFEFE0BC", "#FF9C8458", "Paper"),         // desert sun gold
        new BuiltInTheme("League Bilgewater",  "League of Legends", "#FF0A1820", "#FF142830", "#FFE0782C", "#FFE8DCC8", "#FF7E8C8C", "Retro"),         // teal harbor + lantern orange
        new BuiltInTheme("League Piltover",    "League of Legends", "#FF1A1208", "#FF2A2014", "#FFE0A028", "#FFEFDDC0", "#FF94805A", "Sharp"),         // Art Deco gold + cream
        new BuiltInTheme("League Zaun",        "League of Legends", "#FF0A0E08", "#FF141810", "#FF80E830", "#FFD8E8C0", "#FF788858", "Cyberpunk"),     // toxic chem-green undercity
        new BuiltInTheme("League Bandle City", "League of Legends", "#FF180E08", "#FF281A12", "#FFFFB838", "#FFEFDDC0", "#FF8E785A", "Paper"),         // yordle warm yellow-orange
        new BuiltInTheme("League Targon",      "League of Legends", "#FF0A0828", "#FF14123C", "#FFC0A8FF", "#FFE5DCFF", "#FF8478B0", "Holographic"),   // celestial cosmic purple
        new BuiltInTheme("League Void",        "League of Legends", "#FF0E081A", "#FF1A1028", "#FFC020E0", "#FFE0CCEE", "#FF8868A0", "Cyberpunk"),     // eldritch void purple-pink
        new BuiltInTheme("League Ixtal",       "League of Legends", "#FF081410", "#FF141E1A", "#FF40C868", "#FFD0E8D0", "#FF6E8870", "Default"),       // jungle vine green

        // ---- Fallout (14) — base 3 + 11 Nuka-Cola flavors ----
        // v1.25.32: Pip-Boy classic green #1bff80 (FO4) or #99CC00 (Vault
        // green) — community/wiki definitive. Vault-Tec branding is
        // canary yellow #FEF265 + dark blue #325886 per the Vault Boy
        // color scheme. Brotherhood of Steel = sandy tan power armor +
        // steel grey + iconic red sun emblem on grey.
        //
        // The 11 Nuka-Cola flavor palettes are drawn from canonical
        // wiki descriptions:
        // * Classic: brown cola ("color described as brown")
        // * Quantum: neon electric blue ("blue radioactive glow")
        // * Cherry: bright cherry red
        // * Quartz: white "with a hint of green" glow + "non-soluble
        //   sugar flakes to simulate a quartz-like appearance"
        // * Victory: "pinkish-red", "glowed yellow/orange",
        //   "patriotic colors" (warm orange-red glow)
        // * Dark: alcoholic rum, "darker in color, dark brown" / black
        // * Wild: root-beer flavored ("root-based beverage", caramel)
        // * Orange: "orange bottle, orange slice label"
        // * Grape: purple grape soda
        // * Cranberry: deep red cranberry (FO76)
        // * Nukashine: Quantum-infused moonshine (cloudy blue-white)
        new BuiltInTheme("Fallout Pip-Boy",            "Fallout", "#FF000A00", "#FF001400", "#FF1BFF80", "#FFC0FFD0", "#FF509C70", "Terminal"),     // Pip-Boy phosphor green
        new BuiltInTheme("Fallout Vault-Tec",          "Fallout", "#FF080F1A", "#FF14223A", "#FFFEF265", "#FFEFE8B8", "#FF7C8AAA", "Sharp"),         // Vault-Tec yellow + blue
        new BuiltInTheme("Fallout Brotherhood Steel",  "Fallout", "#FF14110A", "#FF221E14", "#FFC09858", "#FFEAE0C8", "#FF8C8068", "Brutalist"),     // BoS sandy power armor + steel
        new BuiltInTheme("Fallout Nuka-Cola Classic",  "Fallout", "#FF14080A", "#FF22120E", "#FFE82838", "#FFE8C8C8", "#FF8C6868", "Retro"),         // classic brown cola + red label
        new BuiltInTheme("Fallout Nuka-Cola Quantum",  "Fallout", "#FF00081E", "#FF001036", "#FF1EC8FF", "#FFC8E8FF", "#FF509CC0", "Holographic"),   // neon electric blue glow
        new BuiltInTheme("Fallout Nuka-Cherry",        "Fallout", "#FF180408", "#FF28080E", "#FFE82048", "#FFEFC8D0", "#FF945868", "Retro"),         // bright cherry red
        new BuiltInTheme("Fallout Nuka-Cola Quartz",   "Fallout", "#FF101418", "#FF1E2428", "#FFE0F0D8", "#FFEFEFE5", "#FF8A9090", "Frosted"),       // white "with hint of green" glow
        new BuiltInTheme("Fallout Nuka-Cola Victory",  "Fallout", "#FF18100A", "#FF281C12", "#FFFFA838", "#FFEFDDC0", "#FF94805A", "Retro"),         // patriotic orange-yellow glow
        new BuiltInTheme("Fallout Nuka-Cola Dark",     "Fallout", "#FF080608", "#FF14100E", "#FF8B5C29", "#FFE0D2B8", "#FF806858", "Ink"),           // dark rum brown/black
        new BuiltInTheme("Fallout Nuka-Cola Wild",     "Fallout", "#FF180E08", "#FF281A10", "#FF8B4C0D", "#FFE5D0B0", "#FF806050", "Paper"),         // root beer caramel
        new BuiltInTheme("Fallout Nuka-Cola Orange",   "Fallout", "#FF18100A", "#FF2A1A10", "#FFFF7820", "#FFEFD8C0", "#FF94785A", "Retro"),         // orange Fanta
        new BuiltInTheme("Fallout Nuka-Cola Grape",    "Fallout", "#FF100818", "#FF1E1228", "#FF9038D0", "#FFE0D0EE", "#FF7868A0", "Retro"),         // grape purple
        new BuiltInTheme("Fallout Nuka-Cola Cranberry", "Fallout",  "#FF140608", "#FF240A10", "#FFA82038", "#FFE8C8D0", "#FF806068", "Retro"),         // deep cranberry red
        new BuiltInTheme("Fallout Nukashine",          "Fallout", "#FF080E14", "#FF101A24", "#FF80B0E8", "#FFD8E0EC", "#FF7080A0", "Holographic"),   // cloudy moonshine blue-white

        // ---- Borderlands (3) ----
        // v1.25.29: signature "yellow + red + black logo" palette per
        // Gearbox's own brand color usage + hand-inked cel-shaded style.
        // Pandora = post-apocalyptic orange desert + tan dust. Hyperion
        // corporate = sterile cream-white + yellow (Handsome Jack brand).
        // Eridium = purple alien crystal (purple shards canonical).
        new BuiltInTheme("Borderlands Pandora",         "Borderlands", "#FF18100A", "#FF281A12", "#FFE08020", "#FFEFDDC0", "#FF9C7E5C", "Brutalist"),  // dusty orange desert
        new BuiltInTheme("Borderlands Hyperion Corp",   "Borderlands", "#FF14100A", "#FF221E14", "#FFFFD030", "#FFEFE8C8", "#FF94885A", "Sharp"),     // Hyperion cream + Jack yellow
        new BuiltInTheme("Borderlands Eridium Crystal", "Borderlands", "#FF0E0820", "#FF1A1230", "#FFB060FF", "#FFE8DCF8", "#FF8A6EB6", "Holographic"), // alien crystal purple

        // ---- Witcher (3) ----
        // v1.25.31: Witcher 3 community palette = "#4e636c steel-grey +
        // #b50c0f Witcher red + #eaeaea white (Geralt's hair) + #272727
        // black". Area reshade refs confirm Kaer Morhen = "cold dark
        // green spruce forest, dark nights" and Toussaint = warm
        // vineyard golden Mediterranean. Three: Wolf Medallion (Geralt's
        // silver/grey + white-wolf), Kaer Morhen (cold green spruce),
        // Toussaint (warm vineyard gold).
        new BuiltInTheme("Witcher Wolf Medallion",        "Witcher", "#FF0A0C10", "#FF18181C", "#FFB8B0A0", "#FFE5E0D8", "#FF888880", "Ink"),       // silver medallion + white wolf
        new BuiltInTheme("Witcher Kaer Morhen",           "Witcher", "#FF080F0E", "#FF141C18", "#FF4A8060", "#FFD0E0D0", "#FF708878", "Brutalist"),  // cold spruce forest northern
        new BuiltInTheme("Witcher Toussaint Vineyard",    "Witcher", "#FF180E08", "#FF2A1E12", "#FFE0A030", "#FFEFDDC0", "#FF94805A", "Paper"),     // warm Mediterranean vineyard gold

        // ---- Cyberpunk 2077 (3) ----
        // v1.25.29: Night City signature is "bright yellow + electric cyan
        // + deep black" per CDPR's own brand palette (the Samurai/V jacket
        // yellow). Pacifica Combat Zone is the gang-controlled slum,
        // graffiti red + decay. Arasaka corporate is signature blood-red
        // (Arasaka's brand is dark red + black militaristic).
        new BuiltInTheme("Cyberpunk 2077 Night City",          "Cyberpunk 2077", "#FF080A14", "#FF12162A", "#FFFCEE09", "#FFE8EFFA", "#FF7C8EAC", "Cyberpunk"),  // signature Samurai yellow
        new BuiltInTheme("Cyberpunk 2077 Pacifica Combat Zone", "Cyberpunk 2077", "#FF180E08", "#FF281A12", "#FFE83828", "#FFEFD2C0", "#FF947058", "Brutalist"),    // gang graffiti red + decay
        new BuiltInTheme("Cyberpunk 2077 Arasaka Corporate",   "Cyberpunk 2077", "#FF0A0808", "#FF181010", "#FFC8121C", "#FFE5D8D8", "#FF8A6868", "Sharp"),        // Arasaka blood red corporate

        // ---- Hades (3) — Tartarus / Asphodel / Elysium ----
        // v1.25.28: redone from "The Art of Hades" by Jen Zee + community
        // wiki descriptions. Tartarus: "cold grays to tangy greens,
        // nauseating decay" — slate grey-green + sulfur teal. Asphodel:
        // "fiery volcanic wasteland, rivers of magma, sea of fire" — deep
        // orange + lava red on black. Elysium: "verdant" — heroic gold
        // + green pastoral honor.
        new BuiltInTheme("Hades Tartarus",  "Hades", "#FF0C1414", "#FF182222", "#FF60C8B0", "#FFDDE8DC", "#FF7A8E84", "Brutalist"),  // cold gray + tangy green
        new BuiltInTheme("Hades Asphodel",  "Hades", "#FF180808", "#FF281414", "#FFFF6020", "#FFEFD0B8", "#FF9C6858", "Holographic"), // lava + magma
        new BuiltInTheme("Hades Elysium",   "Hades", "#FF0E1810", "#FF1A281C", "#FFE8C440", "#FFEFE0BC", "#FF8E9A6E", "Aurora"),     // verdant gold + hero green

        // ---- Helldivers (3) ----
        // v1.25.30: Super Earth flag canonical hex codes: #41639C blue +
        // #FFE710 yellow + white per the Helldivers wiki. The faction
        // identity is yellow+black armor with blue flag. Three themes:
        // Super Earth democracy (blue/yellow), Hellpod drop (combat
        // yellow + black urgency), Automaton front (red-eye foe).
        new BuiltInTheme("Helldivers Super Earth",     "Helldivers", "#FF0A1018", "#FF14223C", "#FF41639C", "#FFE8EFFA", "#FF7C8AAA", "Sharp"),       // SE flag blue + yellow
        new BuiltInTheme("Helldivers Hellpod Drop",    "Helldivers", "#FF121008", "#FF1F1C10", "#FFFFE710", "#FFEFEAC8", "#FF94905A", "Brutalist"),   // signature yellow+black
        new BuiltInTheme("Helldivers Automaton Front", "Helldivers", "#FF0A0608", "#FF180A0E", "#FFE82828", "#FFE8D8D8", "#FF8C7878", "Cyberpunk"),   // red-eye Automaton foe

        // ---- Doom (3) ----
        // v1.25.29: Doom Eternal HUD palette is #cb5e29 burnt orange +
        // brown leather (exact hex from community UI palette). Mars UAC
        // base is "gray and brown" sterile with red emergency accent.
        // Hellfire = pure hell red on near-black.
        new BuiltInTheme("Doom Slayer",         "Doom", "#FF180C08", "#FF281810", "#FFCB5E29", "#FFEFD8C0", "#FF947058", "Brutalist"),   // Eternal HUD orange #cb5e29
        new BuiltInTheme("Doom Mars UAC",       "Doom", "#FF14100E", "#FF221E1C", "#FFE03020", "#FFE5D8D5", "#FF8C7E78", "Sharp"),       // UAC base gray/brown + red alert
        new BuiltInTheme("Doom Hellfire",       "Doom", "#FF180404", "#FF280808", "#FFE83018", "#FFEFC8B8", "#FF945858", "Holographic"), // pure hell red

        // ---- Mass Effect (3) ----
        // v1.25.30: official ME palette is "#0eb9fe cyan + #e61809 red +
        // black + white" per community palette tags. N7 = signature
        // red stripe on black armor. Citadel/ME1 = exploration cyan
        // (the blue Normandy CIC, hologram displays). Reapers = the
        // indoctrination warning red on void.
        new BuiltInTheme("Mass Effect N7",                    "Mass Effect", "#FF0A0A0C", "#FF161616", "#FFE61809", "#FFE8E0DC", "#FF8A7C7C", "Sharp"),       // N7 red stripe + black armor
        new BuiltInTheme("Mass Effect Citadel Council",       "Mass Effect", "#FF080F1C", "#FF121C30", "#FF0EB9FE", "#FFD8E8F8", "#FF7090B0", "Holographic"),  // exploration cyan + Normandy CIC
        new BuiltInTheme("Mass Effect Reaper Indoctrination", "Mass Effect", "#FF080608", "#FF14080A", "#FFE82820", "#FFE5D2D0", "#FF8C6868", "Brutalist"),    // Reaper red void

        // ---- No Man's Sky (3) ----
        // v1.25.30: NMS planets are procedurally psychedelic — community
        // and pre-NEXT screenshots show signature pink/purple/cyan alien
        // grass and skies. Galaxy = exotic pink-purple lush biome.
        // Atlas Singularity = the red orb deity, pulsing scarlet on void.
        // Sentinel Patrol = green eye + chrome (the AI overseers' menacing
        // green scan glow).
        new BuiltInTheme("No Man's Sky Galaxy",            "No Man's Sky", "#FF180A24", "#FF281438", "#FFE860C0", "#FFEAD8F8", "#FF9C7CB0", "Aurora"),     // alien lush psychedelic pink
        new BuiltInTheme("No Man's Sky Atlas Singularity", "No Man's Sky", "#FF120406", "#FF22080A", "#FFFF2828", "#FFEFCAC8", "#FF8E5858", "Holographic"), // Atlas red pulsing orb
        new BuiltInTheme("No Man's Sky Sentinel Patrol",   "No Man's Sky", "#FF0A1010", "#FF182020", "#FF40E060", "#FFD8E5D8", "#FF788E78", "Cyberpunk"),   // Sentinel green eye + chrome

        // ---- Hollow Knight (3) — Hallownest core areas ----
        // v1.25.28: redone from official Hollow Knight wiki + the
        // community color-palette tag for City of Tears (deep blue
        // #1d2e65 + teal-grey #4d6f94 verbatim). Greenpath: "exuberant
        // green cavern, mossy valleys, acid lakes" — bright moss green.
        // Hallownest core: monochrome pale void white + dark slate.
        new BuiltInTheme("Hollow Knight Hallownest",    "Hollow Knight", "#FF080A10", "#FF12161F", "#FFD8E5E8", "#FFDCE0E5", "#FF6E7882", "Ink"),       // void monochrome
        new BuiltInTheme("Hollow Knight City of Tears", "Hollow Knight", "#FF08101E", "#FF14203A", "#FF6090C8", "#FFD0E0EE", "#FF7088A0", "Frosted"),   // perpetual rainfall blue
        new BuiltInTheme("Hollow Knight Greenpath",     "Hollow Knight", "#FF0A1810", "#FF14241A", "#FF60C840", "#FFD8E8C8", "#FF788E70", "Aurora"),    // mossy acid green

        // ---- Stardew Valley (3) ----
        // v1.25.31: Stardew's signature is warm pastel pixel art —
        // grass-green farm + sunset gold. Skull Cavern is the deep
        // mine with amber-orange torch glow on dark stone. Ginger
        // Island is the tropical paradise with turquoise sea + orange
        // sunset.
        new BuiltInTheme("Stardew Valley Farm",         "Stardew Valley", "#FF0E1A0A", "#FF182812", "#FFE8B040", "#FFEFE0BC", "#FF889872", "Paper"),     // farm meadow + sunset gold
        new BuiltInTheme("Stardew Valley Skull Cavern", "Stardew Valley", "#FF120C08", "#FF221A12", "#FFE8782C", "#FFEFDDC0", "#FF94785A", "Retro"),     // deep mine amber torchlight
        new BuiltInTheme("Stardew Valley Ginger Island", "Stardew Valley", "#FF081820", "#FF142C36", "#FF40C8C0", "#FFDDE8DC", "#FF7090A0", "Aurora"),    // tropical turquoise sea

        // ---- Minecraft (3) ----
        new BuiltInTheme("Minecraft Creeper",      "Minecraft", "#FF1E2B0E", "#FF2E3F1A", "#FF6FBE48", "#FFFAEFB8", "#FFA78C5A", "Retro"),
        new BuiltInTheme("Minecraft Nether Realm", "Minecraft", "#FF1A0808", "#FF2A1010", "#FFE85830", "#FFFAE0D0", "#FF9A625E", "Brutalist"),
        new BuiltInTheme("Minecraft The End",      "Minecraft", "#FF14101A", "#FF221A2A", "#FFE5DFC0", "#FFF0E8D0", "#FF9888A0", "Sharp"),

        // ---- Persona 5 (3) — Phantom Thieves / Mementos / Velvet Room ----
        // v1.25.28: Persona 5's signature palette is "#d92323 red + #0d0d0d
        // black + white" (canonical from multiple community references and
        // Atlus's own logo guidelines). Mementos is "entire area bathed in
        // red" per fan/dev commentary, deeper saturated rebellion red.
        // Velvet Room is the iconic blue prison cell (Igor's domain).
        new BuiltInTheme("Persona 5 Phantom Thieves", "Persona 5", "#FF0A0808", "#FF161010", "#FFD92323", "#FFEFEFEF", "#FF8C7878", "Sharp"),       // signature P5 red + black + white
        new BuiltInTheme("Persona 5 Mementos",        "Persona 5", "#FF180808", "#FF2A0E0E", "#FFB81020", "#FFE8C8C8", "#FF885858", "Brutalist"),    // tunnels bathed in red
        new BuiltInTheme("Persona 5 Velvet Room",     "Persona 5", "#FF080A1E", "#FF101230", "#FF4080E0", "#FFD8E0F2", "#FF7888A8", "Holographic"),  // Igor's blue prison

        // ---- DayZ (3) ----
        new BuiltInTheme("DayZ Chernarus Survivor",  "DayZ", "#FF1A1814", "#FF252118", "#FF8A7A3A", "#FFD5C8A8", "#FF796E5A", "Brutalist"),
        new BuiltInTheme("DayZ Livonia Forest",      "DayZ", "#FF14180E", "#FF1F261A", "#FF8A7038", "#FFE5DCC8", "#FF7D8668", "Paper"),
        new BuiltInTheme("DayZ Infected Encounter",  "DayZ", "#FF120A08", "#FF1F1410", "#FFC03830", "#FFE8D8D0", "#FF8A6C5E", "Brutalist"),

        // ---- Amnesia (3) ----
        new BuiltInTheme("Amnesia Dark Descent",          "Amnesia", "#FF0E0A06", "#FF181210", "#FFCAB04A", "#FFC8B098", "#FF766250", "Brutalist"),
        new BuiltInTheme("Amnesia Brennenburg Cellar",    "Amnesia", "#FF0A0808", "#FF141010", "#FF6A8868", "#FFC0B098", "#FF686250", "Brutalist"),
        new BuiltInTheme("Amnesia Daniel's Sanity Loss",  "Amnesia", "#FF1A0E0A", "#FF281610", "#FF982828", "#FFC8B098", "#FF826250", "Holographic"),

        // ---- Baldur's Gate 3 (3) ----
        new BuiltInTheme("Baldur's Gate 3 Underdark",            "Baldur's Gate 3", "#FF0E0820", "#FF1A1230", "#FF7BD4DE", "#FFE8DFF8", "#FFA08CC8", "Aurora"),
        new BuiltInTheme("Baldur's Gate 3 Shadow-Cursed Lands",  "Baldur's Gate 3", "#FF0E0E18", "#FF14142A", "#FF6A40CC", "#FFE0DEEC", "#FF7C809B", "Holographic"),
        new BuiltInTheme("Baldur's Gate 3 Avernus Hellfire",     "Baldur's Gate 3", "#FF180A08", "#FF281410", "#FFE85020", "#FFEFD8C8", "#FF976C5C", "Brutalist"),

        // ---- Crash Bandicoot (3) ----
        new BuiltInTheme("Crash Bandicoot N. Sanity",       "Crash Bandicoot", "#FF0A2818", "#FF143828", "#FFFF7A1A", "#FFFFEFD0", "#FF77C6E2", "Default"),
        new BuiltInTheme("Crash Bandicoot Cortex Castle",   "Crash Bandicoot", "#FF14081A", "#FF221028", "#FF40D848", "#FFE0EAD0", "#FF927CAC", "Sharp"),
        new BuiltInTheme("Crash Bandicoot Slippery Climb",  "Crash Bandicoot", "#FF1A1018", "#FF281828", "#FF40C8FF", "#FFE8DCEC", "#FF967FA1", "Aurora"),

        // ---- Spore (3) ----
        new BuiltInTheme("Spore Creature Stage",        "Spore", "#FF1A0E22", "#FF2A1832", "#FF8AFFAA", "#FFFCEEFC", "#FFFEC075", "Aurora"),
        new BuiltInTheme("Spore Cell Stage Primordial", "Spore", "#FF0A1418", "#FF12222A", "#FF40FFD8", "#FFE0F8F0", "#FF7CA6AE", "Aurora"),
        new BuiltInTheme("Spore Space Stage Galactic",  "Spore", "#FF0A0814", "#FF12101F", "#FFFFC448", "#FFEAE0F8", "#FF8A809E", "Holographic"),

        // ---- Stronghold 2 (3) ----
        new BuiltInTheme("Stronghold 2 Castle Keep",    "Stronghold 2", "#FF1C1812", "#FF2C271E", "#FFB81F1F", "#FFEDE2C8", "#FF978B74", "Default"),
        new BuiltInTheme("Stronghold 2 Siege Bombard",  "Stronghold 2", "#FF1A0E0A", "#FF281A12", "#FFC04848", "#FFE8DAC8", "#FF967A68", "Brutalist"),
        new BuiltInTheme("Stronghold 2 Royal Feast",    "Stronghold 2", "#FF1C1408", "#FF2C2010", "#FFE8C048", "#FFFAE8C8", "#FF9A825C", "Paper"),

        // ---- Valheim (3) ----
        new BuiltInTheme("Valheim Mistlands",      "Valheim", "#FF1A2028", "#FF252D38", "#FFD08A3A", "#FFE8DCC4", "#FF8A9197", "Frosted"),
        new BuiltInTheme("Valheim Black Forest",   "Valheim", "#FF0A1410", "#FF121F1A", "#FF98C868", "#FFD8E0D0", "#FF7A8A76", "Brutalist"),
        new BuiltInTheme("Valheim Plains Fuling",  "Valheim", "#FF1A1410", "#FF281E1A", "#FFE85838", "#FFE8DCC8", "#FF967A68", "Brutalist"),

        // ---- World of Tanks (3) ----
        new BuiltInTheme("World of Tanks Olive Drab",    "World of Tanks", "#FF1A1C10", "#FF252818", "#FFC8A050", "#FFE8E2C8", "#FF8A8B68", "Brutalist"),
        new BuiltInTheme("World of Tanks German Panzer", "World of Tanks", "#FF14140E", "#FF202018", "#FFC08038", "#FFDCDCC8", "#FF7A7A68", "Sharp"),
        new BuiltInTheme("World of Tanks Soviet Heavy",  "World of Tanks", "#FF180E08", "#FF281A10", "#FFE03838", "#FFE5DCC8", "#FF897968", "Brutalist"),
    };

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
        var bgColor = ParseColor(s.BackgroundColor, DarkBackground);
        if (bgColor.R > 0x80 || bgColor.G > 0x80 || bgColor.B > 0x80)
            res["SkinWidgetOpacity"] = 1.0;
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
