using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Fluid.Shared.Protocol;

namespace Fluid.App.Models;

public enum LayoutOrientation { Horizontal, Vertical }

public class AppSettings : INotifyPropertyChanged
{
    private bool _showCpu     = true;
    private bool _showGpu     = true;
    private bool _showRam     = true;
    private bool _showNetwork = true;
    private bool _showStorage = true;
    private bool _showDateTime = false;  // v1.16: new tile, off by default

    public bool ShowCpu      { get => _showCpu;      set => Set(ref _showCpu,      value); }
    public bool ShowGpu      { get => _showGpu;      set => Set(ref _showGpu,      value); }
    public bool ShowRam      { get => _showRam;      set => Set(ref _showRam,      value); }
    public bool ShowNetwork  { get => _showNetwork;  set => Set(ref _showNetwork,  value); }
    public bool ShowStorage  { get => _showStorage;  set => Set(ref _showStorage,  value); }
    public bool ShowDateTime { get => _showDateTime; set => Set(ref _showDateTime, value); }

    // v1.18: User-controlled tile display order. Default matches the original
    // hardcoded order (CPU, GPU, RAM, Network, Storage, Clock). Drag-reorder
    // in Settings rewrites this list. Stored as TileKind names (strings) for
    // JSON-stable serialization. Loader fills in any missing kinds at the end
    // so old settings.json files (which lack this field) keep working and
    // future-added tile kinds don't get silently dropped.
    // v1.23: DateTime (Clock) moved to the FRONT of the default order per
    // user request -- when the Clock tile is enabled it sits at the very top.
    // Drag-reorder in Settings still gives full control afterward; schema
    // migration v3 applies the same move once to existing settings files.
    public List<string> TileOrder { get; set; } = new()
    {
        nameof(TileKind.DateTime),
        nameof(TileKind.Cpu),
        nameof(TileKind.Gpu),
        nameof(TileKind.Ram),
        nameof(TileKind.Network),
        nameof(TileKind.Storage),
    };

    // Default to Vertical layout
    private LayoutOrientation _orientation = LayoutOrientation.Vertical;
    public LayoutOrientation Orientation { get => _orientation; set => Set(ref _orientation, value); }

    private bool _alwaysOnTop  = true;
    private bool _snapToEdges  = true;
    private bool _useFahrenheit;

    public bool AlwaysOnTop   { get => _alwaysOnTop;   set => Set(ref _alwaysOnTop,   value); }
    public bool SnapToEdges   { get => _snapToEdges;   set => Set(ref _snapToEdges,   value); }
    public bool UseFahrenheit { get => _useFahrenheit; set => Set(ref _useFahrenheit, value); }

    // v1.25.37: snap to other windows (outer edges). Controlled by the
    // master SnapToEdges toggle; this sub-option lets the user disable
    // window snapping while keeping screen-edge snapping (or vice versa).
    private bool _snapToWindows = true;
    public bool SnapToWindows { get => _snapToWindows; set => Set(ref _snapToWindows, value); }
    // Window titles (substring match, case-insensitive) to exclude from
    // snap targets. Editable in Utilities.
    public List<string> SnapBlocklist { get; set; } = new();

    // v1.23: launch the widget at Windows sign-in via an HKCU Run registry
    // value (per-user, no elevation needed -- which is why there is no UAC
    // shield on the checkbox). SettingsWindow owns the registry write; this
    // flag just persists the user's intent. The installer's "startup
    // shortcut" task is the older mechanism; unchecking this removes both
    // so startup behavior is never split across two switches.
    private bool _runAtStartup;
    public bool RunAtStartup { get => _runAtStartup; set => Set(ref _runAtStartup, value); }

    private int    _updateIntervalMs = 1500;
    private double _uiScale          = 1.0;
    private double _tileWidth        = 130;
    private double _tileHeight       = 110;
    private double _opacity          = 0.90;

    public int    UpdateIntervalMs { get => _updateIntervalMs; set => Set(ref _updateIntervalMs, value); }
    public double UiScale          { get => _uiScale;          set => Set(ref _uiScale,          value); }
    public double TileWidth        { get => _tileWidth;        set => Set(ref _tileWidth,        value); }
    public double TileHeight       { get => _tileHeight;       set => Set(ref _tileHeight,       value); }
    public double Opacity          { get => _opacity;          set => Set(ref _opacity,          value); }

    // Font size offsets (-5 to +5 from defaults)
    private int _primaryFontSizeOffset   = 0;
    private int _secondaryFontSizeOffset = 0;
    private int _indicatorFontSizeOffset = 0;
    // v1.25.16: per-tile granular size offsets. The existing IndicatorFontSizeOffset
    // still applies globally to unit labels (KB/s, MHz, %, °C). These two are
    // additive on top, ONLY for their respective tile elements:
    //   ArrowFontSizeOffset       — Network ↓ / ↑ arrows
    //   DiskLabelFontSizeOffset   — Disk R: / W: labels
    private int _arrowFontSizeOffset = 0;
    private int _diskLabelFontSizeOffset = 0;
    public int PrimaryFontSizeOffset      { get => _primaryFontSizeOffset;      set => Set(ref _primaryFontSizeOffset,      value); }
    public int SecondaryFontSizeOffset    { get => _secondaryFontSizeOffset;    set => Set(ref _secondaryFontSizeOffset,    value); }
    public int IndicatorFontSizeOffset    { get => _indicatorFontSizeOffset;    set => Set(ref _indicatorFontSizeOffset,    value); }
    public int ArrowFontSizeOffset        { get => _arrowFontSizeOffset;        set => Set(ref _arrowFontSizeOffset,        value); }
    public int DiskLabelFontSizeOffset    { get => _diskLabelFontSizeOffset;    set => Set(ref _diskLabelFontSizeOffset,    value); }

    // v1.20.2: Muted text contrast slider. 1.0 = use the muted color as-is.
    // Above 1.0 blends muted toward text color (more visible). Below 1.0
    // blends toward background (more subdued). Reasonable range 0.5 - 1.6.
    // Applied at render time in ThemeApplier.Apply via blend of MutedTextColor
    // toward TextColor (when >1) or BackgroundColor (when <1).
    private double _mutedContrast = 1.0;
    public double MutedContrast { get => _mutedContrast; set => Set(ref _mutedContrast, value); }

    // v1.20.3: tile-internal label spacing sliders. These control how far the
    // arrow / letter prefix in the Network and Disk tiles sits from the
    // associated value. The column width in the tile's mini-grid is set from
    // these values at render time.
    //   NetworkArrowSpacing - width of the ↓/↑ column (default 16, range 8-40)
    //   DiskLabelSpacing    - width of the R:/W: column (default 16, range 8-40)
    private double _networkArrowSpacing = 16.0;
    private double _diskLabelSpacing    = 16.0;
    public double NetworkArrowSpacing { get => _networkArrowSpacing; set => Set(ref _networkArrowSpacing, value); }
    public double DiskLabelSpacing    { get => _diskLabelSpacing;    set => Set(ref _diskLabelSpacing,    value); }

    // v1.20.3: Network traffic activity indicator style. Cycles Off / Blink / Fade / Glow.
    //   "Off"   - no animation, default
    //   "Blink" - opacity pulses on the arrow when traffic is active
    //   "Fade"  - slow color ramp in/out
    //   "Glow"  - accent-colored drop shadow
    private string _networkTrafficIndicator = "Off";
    public string NetworkTrafficIndicator { get => _networkTrafficIndicator; set => Set(ref _networkTrafficIndicator, value); }

    // v1.20.3: selected physical disk for the Disk tile. Empty string = aggregate
    // all disks (legacy behavior). Otherwise the Disk tile shows stats for the
    // physical disk with this DeviceId (e.g. "0", "1") and displays the disk
    // model under the tile header.
    private string _selectedDiskId = "";
    public string SelectedDiskId { get => _selectedDiskId; set => Set(ref _selectedDiskId, value); }

    // v1.24: how the Disk tile labels the selected disk under its header.
    //   "Letter" (default) -> drive letters, e.g. "C:" / "C: D:"
    //   "Model"            -> the disk model string
    //   "Both"             -> "C: · Samsung SSD 980 PRO 2TB"
    private string _diskLabelStyle = "Letter";
    public string DiskLabelStyle { get => _diskLabelStyle; set => Set(ref _diskLabelStyle, value); }

    // v1.25.1: CPU temp hint dismissal. When the user explicitly dismisses the
    // "Turn on temp" affordance, we suppress all visual indicators (pill,
    // bottom accent stripe, settings pulse). _CpuTempDismissChoice records
    // *how* they want the CPU tile to render in that state:
    //   "LoadOnly" -> tile still visible, shows only the load % (no temp slot)
    //   "HideTile" -> the whole CPU tile is hidden via App.ShowCpu
    // Set to "" (default) when not dismissed. Setting CpuTempHintDismissed=false
    // (e.g. after the user enables the driver) restores normal behavior.
    private bool _cpuTempHintDismissed;
    public bool CpuTempHintDismissed { get => _cpuTempHintDismissed; set => Set(ref _cpuTempHintDismissed, value); }

    private string _cpuTempDismissChoice = "";
    public string CpuTempDismissChoice { get => _cpuTempDismissChoice; set => Set(ref _cpuTempDismissChoice, value); }

    // v1.13: per-text-type font families. Empty string = use skin/theme default.
    // SyncFonts: when true, Primary font is mirrored to Secondary + Indicator on change.
    // RandomizeFontsOnDice: when true, the dice button also rolls a random font triple.
    private string _primaryFont   = "";
    private string _secondaryFont = "";
    private string _indicatorFont = "";
    private bool   _syncFonts            = true;
    private bool   _randomizeFontsOnDice = false;
    public string PrimaryFont          { get => _primaryFont;          set => Set(ref _primaryFont,          value); }
    public string SecondaryFont        { get => _secondaryFont;        set => Set(ref _secondaryFont,        value); }
    public string IndicatorFont        { get => _indicatorFont;        set => Set(ref _indicatorFont,        value); }
    public bool   SyncFonts            { get => _syncFonts;            set => Set(ref _syncFonts,            value); }
    public bool   RandomizeFontsOnDice { get => _randomizeFontsOnDice; set => Set(ref _randomizeFontsOnDice, value); }

    // Click-through hotkey — e.g. "Ctrl+Shift+F12"
    private string _clickThroughHotkey = "";
    public string ClickThroughHotkey { get => _clickThroughHotkey; set => Set(ref _clickThroughHotkey, value); }

    // Click-through state
    private bool _clickThrough;
    public bool ClickThrough { get => _clickThrough; set => Set(ref _clickThrough, value); }

    // Colors
    private string _backgroundColor = "#E61E1E22";
    private string _tileColor       = "#FF2A2A30";
    private string _accentColor     = "#FF00A8FF";
    private string _textColor       = "#FFE8E8EC";
    private string _mutedTextColor  = "#FF9A9AA8";

    public string BackgroundColor { get => _backgroundColor; set => Set(ref _backgroundColor, value); }
    public string TileColor       { get => _tileColor;       set => Set(ref _tileColor,       value); }
    public string AccentColor     { get => _accentColor;     set => Set(ref _accentColor,     value); }
    public string TextColor       { get => _textColor;       set => Set(ref _textColor,       value); }
    public string MutedTextColor  { get => _mutedTextColor;  set => Set(ref _mutedTextColor,  value); }

    // Remote devices (max 5)
    public List<Fluid.Shared.Protocol.RemoteDevice> RemoteDevices { get; set; } = new();

    // Null = local, otherwise Guid of active remote device

    // Network adapter to monitor — empty = sum all adapters
    private string _networkAdapterName = "";
    public string NetworkAdapterName { get => _networkAdapterName; set => Set(ref _networkAdapterName, value); }

    // Per-tile warnings (one entry per TileKind, initialized with defaults)

    private bool _isDarkMode = true;
    public bool IsDarkMode { get => _isDarkMode; set => Set(ref _isDarkMode, value); }

    // Custom tile labels — empty string means use auto-detected hardware name
    private string _cpuCustomName  = "";
    private string _gpuCustomName  = "";
    public string CpuCustomName  { get => _cpuCustomName;  set => Set(ref _cpuCustomName,  value); }
    public string GpuCustomName  { get => _gpuCustomName;  set => Set(ref _gpuCustomName,  value); }

    public List<TileWarning> Warnings { get; set; } = new()
    {
        new TileWarning { Kind = TileKind.Cpu, Metric = WarnMetric.Temperature, Threshold = 85 },
        new TileWarning { Kind = TileKind.Gpu, Metric = WarnMetric.Temperature, Threshold = 85 },
    };

    // Game Mode
    private bool   _gameModeEnabled      = false;
    private string _gameModeHotkey       = "";
    private string _gameModePosition     = "TopRight";
    private double _gameModeOpacity      = 0.7;
    private string _gameModeOrientation  = "Current";
    private bool   _gameModeClickThrough = true;
    private bool   _gameModeShowCpu      = true;
    private bool   _gameModeShowGpu      = true;
    private bool   _gameModeShowRam      = true;
    private bool   _gameModeShowNetwork  = true;
    private bool   _gameModeShowStorage  = true;
    // v1.21: Clock tile toggle for game mode. Off by default (matches the
    // tile's own default and the minimal-overlay intent of game mode).
    private bool   _gameModeShowDateTime = false;

    public bool   GameModeEnabled      { get => _gameModeEnabled;      set => Set(ref _gameModeEnabled,      value); }
    public string GameModeHotkey       { get => _gameModeHotkey;       set => Set(ref _gameModeHotkey,       value); }
    public string GameModePosition     { get => _gameModePosition;     set => Set(ref _gameModePosition,     value); }
    public double GameModeOpacity      { get => _gameModeOpacity;      set => Set(ref _gameModeOpacity,      value); }
    public string GameModeOrientation  { get => _gameModeOrientation;  set => Set(ref _gameModeOrientation,  value); }
    public bool   GameModeClickThrough { get => _gameModeClickThrough; set => Set(ref _gameModeClickThrough, value); }
    public bool   GameModeShowCpu      { get => _gameModeShowCpu;      set => Set(ref _gameModeShowCpu,      value); }
    public bool   GameModeShowGpu      { get => _gameModeShowGpu;      set => Set(ref _gameModeShowGpu,      value); }
    public bool   GameModeShowRam      { get => _gameModeShowRam;      set => Set(ref _gameModeShowRam,      value); }
    public bool   GameModeShowNetwork  { get => _gameModeShowNetwork;  set => Set(ref _gameModeShowNetwork,  value); }
    public bool   GameModeShowStorage  { get => _gameModeShowStorage;  set => Set(ref _gameModeShowStorage,  value); }
    public bool   GameModeShowDateTime { get => _gameModeShowDateTime; set => Set(ref _gameModeShowDateTime, value); }

    public double WindowLeft { get; set; } = 100;
    public double WindowTop  { get; set; } = 100;

    // v1.13+ : User-saved presets (up to 5 slots) capturing FULL state (colors + skin + fonts).
    // v1.19: Renamed in spirit -- these are "preset combos", NOT just colors.
    // The +button next to Colors no longer saves to this list. Colors-only saves
    // go to CustomColors below.
    public List<UserColorPreset> UserPresets { get; set; } = new();

    // v1.20: Colors-only custom entries. The Colors cycler dropdown shows
    // built-in named themes plus everything in this list. The + button saves
    // current colors here (NOT to UserPresets). Imported colors from share
    // codes are added here tagged IsImported=true so the dropdown can show
    // a "(i)" badge and let the user see where they came from.
    //
    // Built-in themes live in ThemeApplier.Presets (immutable). Anything in
    // this list is user-deletable via the X button in the cycler dropdown.
    public List<CustomColor> CustomColors { get; set; } = new();

    // v1.20: Custom Themes loaded from .fluidtheme JSON files. A Theme is
    // colors + skin combined (unlike CustomColor which is colors-only).
    // The Themes cycler shows ThemeApplier.BuiltInThemes plus everything here.
    public List<CustomTheme> CustomThemes { get; set; } = new();

    // v1.20: name of the currently-active theme in the Preset Themes cycler.
    // Empty string means "no theme selected" (the user is in mix-and-match mode
    // with manual color/skin choices). Set when the user picks a theme from
    // the cycler; cleared when the user manually changes a color or skin to
    // something that doesn't match any theme.
    public string ActiveTheme { get; set; } = "";

    public bool   RemoteMonitoringEnabled { get; set; } = false;

    // v1.19: default skin is now "Default" (was "Minimal" since v1.10ish).
    // The Reset All flow uses reflection to copy defaults from a fresh
    // AppSettings, so changing this value alone propagates the reset target.
    public string ActiveSkin              { get; set; } = "Default";

    // v1.19: schema migration marker. When the app loads settings.json with
    // SchemaVersion < CurrentSchemaVersion, SettingsService runs the
    // appropriate migration (e.g. v1->v2 wipes UserPresets per user request
    // because the preset semantics changed). Then it stamps the current
    // schema version so the migration runs exactly once.
    //
    // v1.21: the property default must be the OLDEST schema version, not the
    // current one. A pre-v1.19 settings.json has no SchemaVersion property at
    // all, so the deserializer keeps the default -- with the old default of
    // CurrentSchemaVersion those files skipped every migration they were
    // written for. SettingsService.Load stamps CurrentSchemaVersion on the
    // fresh-install (no file) path, which is the only case that should start
    // at current.
    public const int CurrentSchemaVersion = 3;
    public int SchemaVersion { get; set; } = 1;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

public class UserColorPreset
{
    public string Name           { get; set; } = "";
    public string BackgroundColor { get; set; } = "";
    public string TileColor      { get; set; } = "";
    public string AccentColor    { get; set; } = "";
    public string TextColor      { get; set; } = "";
    public string MutedTextColor { get; set; } = "";

    // v1.13: Extended preset fields. Backward-compatible -- old presets that
    // were saved before v1.13 will have these as empty strings, which means
    // "don't override the current value when this preset is applied."
    // Loading code in LoadUserPreset must treat empty as "skip this field".
    public string ActiveSkin     { get; set; } = "";
    public string PrimaryFont    { get; set; } = "";
    public string SecondaryFont  { get; set; } = "";
    public string IndicatorFont  { get; set; } = "";

    public bool IsEmpty => string.IsNullOrEmpty(BackgroundColor);
}

// v1.19: a user-saved or imported color entry that appears in the Colors
// cycler dropdown. Separate from UserColorPreset because this is ONLY about
// colors -- no skin, no fonts. The cycler dropdown shows built-ins + these.
public class CustomColor
{
    public string Name            { get; set; } = "";
    public string BackgroundColor { get; set; } = "";
    public string TileColor       { get; set; } = "";
    public string AccentColor     { get; set; } = "";
    public string TextColor       { get; set; } = "";
    public string MutedTextColor  { get; set; } = "";

    // v1.19: import provenance. When a user imports a share code that
    // references a color name not in their list, we add it here with
    // IsImported=true and ImportedFrom set to the export's name or a
    // short identifier. The dropdown shows "(i)" next to imported entries
    // and the tooltip explains where it came from.
    public bool   IsImported   { get; set; } = false;
    public string ImportedFrom { get; set; } = "";
}

// v1.20: a user theme = colors + skin (no fonts). Loaded from .fluidtheme
// JSON files. Lives in AppSettings.CustomThemes. Differs from BuiltInTheme
// in that it's persisted in settings.json and user-managed.
public class CustomTheme
{
    public string Name            { get; set; } = "";
    public string Franchise       { get; set; } = "";  // optional grouping
    public string BackgroundColor { get; set; } = "";
    public string TileColor       { get; set; } = "";
    public string AccentColor     { get; set; } = "";
    public string TextColor       { get; set; } = "";
    public string MutedTextColor  { get; set; } = "";
    public string SkinName        { get; set; } = "Default";
}

// TODO v1.5: Consolidate GameMode* flat properties into a nested GameModeSettings object
// following the same pattern as PopoutSettings. Currently kept flat for serialization
// compatibility with v1.4 settings files.
