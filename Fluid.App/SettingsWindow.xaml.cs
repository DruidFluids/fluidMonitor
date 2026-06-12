using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Reflection;
using Fluid.App.Models;
using Fluid.App.Services;
using Fluid.Shared.Protocol;

namespace Fluid.App;

public partial class SettingsWindow : Window
{
    private bool _loading = true;
    private string? _activeSwatchKey;
    private RemoteDevice? _activeDevice; // null = Local
    private bool IsRemote => _activeDevice != null;
    private List<DeviceStatusItem> _deviceItems = new();
    private DispatcherTimer? _statusTimer;
    private UpdateService.UpdateInfo? _pendingUpdate;

    // --- v1.14: appearance undo stack (v1.20.3: depth 5) -----------------------------
    // Captures the pre-mutation state of theme + skin + fonts whenever the user
    // changes their appearance (dice, theme cycler, skin cycler, font pick,
    // preset load). Clicking Undo restores the top of the stack. Capped at 2
    // entries because the UI only exposes one Undo button and the use case is
    // "I clicked dice too fast and lost a combo I liked" -- not full history.
    private sealed record AppearanceSnapshot(
        string BackgroundColor, string TileColor, string AccentColor,
        string TextColor,       string MutedTextColor,
        string ActiveSkin,
        string PrimaryFont, string SecondaryFont, string IndicatorFont,
        string ActiveTheme);  // v1.21: undo restores the Preset Theme selection too
    private readonly LinkedList<AppearanceSnapshot> _undoStack = new();
    private const int UndoStackDepth = 5;
    private bool _applyingUndo = false; // prevents the undo apply from re-snapshotting itself
    private bool _suppressSnapshot = false; // set during batched mutations (dice roll, font sync) so only ONE snapshot lands on the stack per user action

    private AppearanceSnapshot CurrentAppearanceSnapshot()
    {
        var s = App.Current.Settings;
        return new AppearanceSnapshot(
            s.BackgroundColor ?? "", s.TileColor ?? "", s.AccentColor ?? "",
            s.TextColor       ?? "", s.MutedTextColor ?? "",
            s.ActiveSkin      ?? "",
            s.PrimaryFont     ?? "", s.SecondaryFont ?? "", s.IndicatorFont ?? "",
            s.ActiveTheme     ?? "");
    }

    private void PushUndoSnapshot()
    {
        if (_applyingUndo || _loading || _suppressSnapshot) return;
        var snap = CurrentAppearanceSnapshot();
        // Skip pushing duplicates of the current top -- avoids stack bloat when
        // a single user action triggers multiple internal apply calls.
        if (_undoStack.First != null && _undoStack.First.Value == snap) return;
        _undoStack.AddFirst(snap);
        while (_undoStack.Count > UndoStackDepth) _undoStack.RemoveLast();
        UpdateUndoButtonVisibility();
    }

    private void UpdateUndoButtonVisibility()
    {
        if (UndoBtn == null) return;
        if (_undoStack.Count == 0)
        {
            UndoBtn.Visibility = Visibility.Hidden;
            return;
        }
        UndoBtn.Visibility = Visibility.Visible;
        // v1.16: tint the undo button background with the accent of the
        // snapshot it would restore. Gives a visual preview of "where I'd
        // go back to" without having to click.
        try
        {
            var snap = _undoStack.First!.Value;
            if (!string.IsNullOrEmpty(snap.AccentColor))
            {
                var c = (System.Windows.Media.Color)
                        System.Windows.Media.ColorConverter.ConvertFromString(snap.AccentColor);
                // Soft tint -- ~40% opacity so the icon stays readable
                var tinted = System.Windows.Media.Color.FromArgb(0x66, c.R, c.G, c.B);
                UndoBtn.Background = new System.Windows.Media.SolidColorBrush(tinted);
                // v1.25.39: same tint on the theme undo button
            }
        }
        catch
        {
            UndoBtn.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
        }
    }

    private void OnUndoAppearance(object sender, RoutedEventArgs e)
    {
        if (_undoStack.Count == 0) return;
        var snap = _undoStack.First!.Value;
        _undoStack.RemoveFirst();
        _applyingUndo = true;
        try
        {
            var s = App.Current.Settings;
            s.BackgroundColor = snap.BackgroundColor;
            s.TileColor       = snap.TileColor;
            s.AccentColor     = snap.AccentColor;
            s.TextColor       = snap.TextColor;
            s.MutedTextColor  = snap.MutedTextColor;
            s.PrimaryFont     = snap.PrimaryFont;
            s.SecondaryFont   = snap.SecondaryFont;
            s.IndicatorFont   = snap.IndicatorFont;
            s.ActiveTheme     = snap.ActiveTheme;  // v1.21

            // Skin change goes through the cycler so the UI label updates
            if (!string.IsNullOrEmpty(snap.ActiveSkin) && snap.ActiveSkin != s.ActiveSkin)
            {
                s.ActiveSkin = snap.ActiveSkin;
                var idx = _skins.FindIndex(sk => sk.Name == snap.ActiveSkin);
                if (idx >= 0) { _skinIndex = idx; UpdateSkinCycler(); ApplyCurrentSkin(); }
            }

            SettingsService.Save(s);
            ThemeApplier.Apply(s, Application.Current.Resources);
            SkinManager.ApplyFontOverrides(s, Application.Current.Resources);

            // Reflect into the Settings UI controls without re-triggering handlers
            _loading = true;
            BackgroundColorBox.Text = s.BackgroundColor; TileColorBox.Text = s.TileColor;
            AccentColorBox.Text = s.AccentColor; TextColorBox.Text = s.TextColor;
            MutedTextColorBox.Text = s.MutedTextColor;
            DarkModeBtn.IsChecked = s.IsDarkMode; LightModeBtn.IsChecked = !s.IsDarkMode;
            PrimaryFontCombo.SelectedItem   = string.IsNullOrEmpty(s.PrimaryFont)   ? FontCatalog.DefaultEntry : s.PrimaryFont;
            SecondaryFontCombo.SelectedItem = string.IsNullOrEmpty(s.SecondaryFont) ? FontCatalog.DefaultEntry : s.SecondaryFont;
            IndicatorFontCombo.SelectedItem = string.IsNullOrEmpty(s.IndicatorFont) ? FontCatalog.DefaultEntry : s.IndicatorFont;
            _loading = false;
            UpdateSwatches();
            _themeIndex = FindCurrentThemeIndex();
            UpdateThemeCycler();
            LoadThemePresetCycler(); // v1.25.39: update Preset Themes name on undo
            RebuildUserPresetSlots();
        }
        finally { _applyingUndo = false; }
        UpdateUndoButtonVisibility();
    }
    // -----------------------------------------------------------------------

    public SettingsWindow()
    {
        InitializeComponent();
        // v1.25.4: the fixed MaxHeight=900 forced scrolling even on tall
        // monitors. Cap to the actual work area instead (minus a margin for
        // the taskbar/chrome) so the window grows to fit all content without
        // a scrollbar whenever the screen allows; small screens still scroll.
        MaxHeight = System.Windows.SystemParameters.WorkArea.Height - 40;
        BuildDeviceSelector();
        LoadFromSettings();
        _loading = false;
        LoadRemoteMonitoringSection();
        LoadSkinCycler();
        LoadThemePresetCycler();   // v1.20
        LoadDiskCombo();           // v1.20.3
        RefreshDiskLabelStyleBtn(); // v1.24
        RefreshCpuTempRow();        // v1.25
        RefreshDeviceList();
        UpdateSwatches();
        LoadTileDeviceCombo();
        InitSliderDefaultMarkers(); // v1.25.45
        LoadNetworkAdapterCombo();
        StartStatusRefreshTimer();
        LoadUpdateSection();

        PickerControl.ColorApplied += OnPickerApplied;
        PickerControl.Cancelled    += () => ColorPopup.IsOpen = false;
    }

    private void LoadFromSettings()
    {
        var s = App.Current.Settings;
        var p = _activeDevice?.Popout; // null when Local selected

        // Tile visibility
        var showCpu  = p?.ShowCpu     ?? s.ShowCpu;
        var showGpu  = p?.ShowGpu     ?? s.ShowGpu;
        var showRam  = p?.ShowRam     ?? s.ShowRam;
        var showNet  = p?.ShowNetwork ?? s.ShowNetwork;
        var showDisk = p?.ShowStorage ?? s.ShowStorage;
        CpuCheck.IsChecked = showCpu; GpuCheck.IsChecked = showGpu;
        RamCheck.IsChecked = showRam; NetworkCheck.IsChecked = showNet;
        StorageCheck.IsChecked = showDisk;
        // v1.16: DateTime tile -- local only (not on remote popouts).
        DateTimeCheck.IsChecked = (p == null) && s.ShowDateTime;
        DateTimeCheck.IsEnabled = (p == null); // grey out for remote

        // v1.18: drag-reorder wiring + position assignment from TileOrder
        InitTileDragDrop();
        PositionTileChecks();


        // Layout (local only)
        HorizToggle.IsChecked = s.Orientation == LayoutOrientation.Horizontal;
        VertToggle.IsChecked  = s.Orientation == LayoutOrientation.Vertical;

        // Behavior (local only)
        TopmostCheck.IsChecked    = s.AlwaysOnTop;
        SnapCheck.IsChecked       = p?.SnapToEdges ?? s.SnapToEdges;
        SnapWindowsCheck.IsChecked = s.SnapToWindows;
        SnapWindowsCheck.Visibility = s.SnapToEdges ? Visibility.Visible : Visibility.Collapsed;
        // v1.23: startup state is read from the machine (Run key OR the
        // installer's startup-folder shortcut), not just the settings flag,
        // so the checkbox always tells the truth about what will happen at
        // sign-in even if one mechanism was set outside this window.
        StartupCheck.IsChecked    = StartupManager.IsEnabled();
        s.RunAtStartup            = StartupCheck.IsChecked == true;
        // v1.25.37: temperature unit rocker (replaces FahrenheitCheck)
        CelsiusToggle.IsChecked    = !s.UseFahrenheit;
        FahrenheitToggle.IsChecked =  s.UseFahrenheit;

        // v1.25.49: visibility handled by RefreshCpuTempRow() which checks
        // installed + dismissed states together.

        ClickThroughCheck.IsChecked = s.ClickThrough;
        HotkeyBox.Text = s.ClickThroughHotkey;

        // Sliders
        OpacitySlider.Value  = p?.Opacity          ?? s.Opacity;
        OpacityLabel.Text    = $"{(int)((p?.Opacity ?? s.Opacity) * 100)}%";
        IntervalSlider.Value = s.UpdateIntervalMs;
        IntervalLabel.Text   = $"{s.UpdateIntervalMs} ms";
        ScaleSlider.Value    = s.UiScale;
        ScaleLabel.Text      = $"{s.UiScale:0.00}x";
        WidthSlider.Value    = p?.TileWidth  ?? s.TileWidth;
        WidthLabel.Text      = $"{(p?.TileWidth  ?? s.TileWidth):0}px";
        HeightSlider.Value   = p?.TileHeight ?? s.TileHeight;
        HeightLabel.Text     = $"{(p?.TileHeight ?? s.TileHeight):0}px";

        PrimaryFontSlider.Value   = p?.PrimaryFontSizeOffset   ?? s.PrimaryFontSizeOffset;
        PrimaryFontLabel.Text     = $"{PrimaryFontSlider.Value:+0;-0;0}pt";
        SecondaryFontSlider.Value = p?.SecondaryFontSizeOffset ?? s.SecondaryFontSizeOffset;
        SecondaryFontLabel.Text   = $"{SecondaryFontSlider.Value:+0;-0;0}pt";
        IndicatorFontSlider.Value = s.IndicatorFontSizeOffset;
        IndicatorFontLabel.Text   = $"{s.IndicatorFontSizeOffset:+0;-0;0}pt";

        // v1.20.2: Muted contrast slider init (no label -- the slider is sized
        // tight under the Muted swatch, with no room for a value label).
        MutedContrastSlider.Value = s.MutedContrast > 0 ? s.MutedContrast : 1.0;

        // v1.20.3: traffic indicator button label
        UpdateTrafficIndicatorButtonLabel();

        // v1.20.3: network arrow spacing + disk label spacing sliders
        NetArrowSpacingSlider.Value  = s.NetworkArrowSpacing > 0 ? s.NetworkArrowSpacing : 16;
        NetArrowSpacingLabel.Text    = $"{(int)NetArrowSpacingSlider.Value}px";
        DiskLabelSpacingSlider.Value = s.DiskLabelSpacing    > 0 ? s.DiskLabelSpacing    : 16;
        DiskLabelSpacingLabel.Text   = $"{(int)DiskLabelSpacingSlider.Value}px";
        // v1.25.16: per-tile font-size sliders
        ArrowFontSizeSlider.Value     = s.ArrowFontSizeOffset;
        ArrowFontSizeLabel.Text       = $"{s.ArrowFontSizeOffset:+0;-0;0}pt";
        DiskLabelFontSizeSlider.Value = s.DiskLabelFontSizeOffset;
        DiskLabelFontSizeLabel.Text   = $"{s.DiskLabelFontSizeOffset:+0;-0;0}pt";

        // v1.13: Populate font-family combos and select current values.
        // Per-popout (remote device) fonts not supported yet — these are global.
        if (PrimaryFontCombo.Items.Count == 0)
        {
            foreach (var f in FontCatalog.AllFonts)
            {
                PrimaryFontCombo.Items.Add(f);
                SecondaryFontCombo.Items.Add(f);
                IndicatorFontCombo.Items.Add(f);
            }
            // v1.25.19: append any font names referenced by skin SkinFontFamily
            // chains that aren't already in the curated catalog. This makes
            // skin-only fonts like "Inter" explicitly selectable from the
            // dropdown -- otherwise the user has no way to pick them by name.
            // Case-insensitive so "Cascadia Mono" doesn't duplicate.
            var already = new System.Collections.Generic.HashSet<string>(
                FontCatalog.AllFonts, System.StringComparer.OrdinalIgnoreCase);
            foreach (var name in SkinManager.CollectSkinFontNames())
            {
                if (already.Add(name))
                {
                    PrimaryFontCombo.Items.Add(name);
                    SecondaryFontCombo.Items.Add(name);
                    IndicatorFontCombo.Items.Add(name);
                }
            }
        }
        // v1.25.15: the "(skin default)" placeholder now also shows the
        // actual font the active skin is using, e.g. "(skin default — Cascadia Mono)".
        // Re-evaluates whenever the skin changes via RefreshFontComboPlaceholders().
        RefreshFontComboPlaceholders();
        PrimaryFontCombo.SelectedItem   = ResolveFontComboSelection(s.PrimaryFont);
        SecondaryFontCombo.SelectedItem = ResolveFontComboSelection(s.SecondaryFont);
        IndicatorFontCombo.SelectedItem = ResolveFontComboSelection(s.IndicatorFont);
        SyncFontsCheck.IsChecked        = s.SyncFonts;
        RandomizeFontsCheck.IsChecked   = s.RandomizeFontsOnDice;

        // v1.14: keep undo button hidden until first appearance change creates a snapshot
        UpdateUndoButtonVisibility();

        // Colors
        BackgroundColorBox.Text = p?.BackgroundColor ?? s.BackgroundColor;
        TileColorBox.Text       = p?.TileColor       ?? s.TileColor;
        AccentColorBox.Text     = p?.AccentColor     ?? s.AccentColor;
        TextColorBox.Text       = p?.TextColor       ?? s.TextColor;
        MutedTextColorBox.Text  = p?.MutedTextColor  ?? s.MutedTextColor;

        // Tile labels
        var cpuCustom = p?.CpuCustomName ?? s.CpuCustomName;
        var gpuCustom = p?.GpuCustomName ?? s.GpuCustomName;
        var cpuIsCustom = !string.IsNullOrEmpty(cpuCustom);
        var gpuIsCustom = !string.IsNullOrEmpty(gpuCustom);
        CpuAutoBtn.IsChecked       = !cpuIsCustom;
        CpuCustomBtn.IsChecked     = cpuIsCustom;
        CpuCustomNameBox.IsEnabled = cpuIsCustom;
        CpuCustomNameBox.Text      = cpuIsCustom ? cpuCustom : App.Current.SensorState.CpuTile.SubHeader;
        GpuAutoBtn.IsChecked       = !gpuIsCustom;
        GpuCustomBtn.IsChecked     = gpuIsCustom;
        GpuCustomNameBox.IsEnabled = gpuIsCustom;
        GpuCustomNameBox.Text      = gpuIsCustom ? gpuCustom : App.Current.SensorState.GpuTile.SubHeader;

        // Dark/light mode buttons
        DarkModeBtn.IsChecked  =  s.IsDarkMode;
        LightModeBtn.IsChecked = !s.IsDarkMode;

        // Sync cycler AFTER all color values are loaded
        SyncPresetCombo();
    }

    private void UpdateSwatches()
    {
        var s = App.Current.Settings;
        var p = _activeDevice?.Popout;
        SetSwatch(SwBg,     SwBgLabel,     SwBgHex,     p?.BackgroundColor ?? s.BackgroundColor);
        SetSwatch(SwTile,   SwTileLabel,   SwTileHex,   p?.TileColor       ?? s.TileColor);
        SetSwatch(SwAccent, SwAccentLabel, SwAccentHex, p?.AccentColor     ?? s.AccentColor);
        SetSwatch(SwText,   SwTextLabel,   SwTextHex,   p?.TextColor       ?? s.TextColor);
        SetSwatch(SwMuted,  SwMutedLabel,  SwMutedHex,  p?.MutedTextColor  ?? s.MutedTextColor);

        // v1.25.37: highlight the active swatch (the one whose color picker
        // is open). All others get transparent border.
        UpdateSwatchHighlight();
    }

    private void UpdateSwatchHighlight()
    {
        var swatches = new[] { (SwBg, "BackgroundColor"), (SwTile, "TileColor"),
                               (SwAccent, "AccentColor"), (SwText, "TextColor"),
                               (SwMuted, "MutedTextColor") };
        foreach (var (btn, key) in swatches)
        {
            if (key == _activeSwatchKey)
            {
                btn.BorderBrush = (Brush)Application.Current.Resources["TextBrush"];
                btn.BorderThickness = new Thickness(2);
            }
            else
            {
                // v1.25.39: faint muted border instead of transparent so dark
                // swatches are visible against dark backgrounds.
                var muted = Application.Current.Resources["MutedTextBrush"] as SolidColorBrush;
                var c = muted?.Color ?? Colors.Gray;
                btn.BorderBrush = new SolidColorBrush(Color.FromArgb(100, c.R, c.G, c.B));
                btn.BorderThickness = new Thickness(1);
            }
        }
    }

    private void SetSwatch(Button btn, TextBlock label, string hex)
    {
        SetSwatch(btn, label, null, hex);
    }

    private void SetSwatch(Button btn, TextBlock label, TextBlock? hexLabel, string hex)
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            btn.Background = new SolidColorBrush(c);
            // v1.25.37: derive hex from the parsed color so the label always
            // matches what the color picker sees (avoids alpha prefix drift
            // and rounding mismatches between settings strings and rendered color).
            if (hexLabel != null)
                hexLabel.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
        catch { btn.Background = Brushes.Gray; }
    }

    // ------------------------------------------------------------------
    // Tiles
    // ------------------------------------------------------------------

    private void BuildDeviceSelector()
    {
        var devices = App.Current.Settings.RemoteDevices;
        DeviceSelectorBar.Visibility = devices.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        // v1.21: removed a dead cast expression here that would have thrown
        // InvalidCastException if ItemsSource were ever assigned.
        var host = SettingsDeviceSelector;
        host.Items.Clear();

        AddSelectorItem(host, "Local", null, true);
        foreach (var d in devices)
        {
            var connected = App.Current.DeviceManager?.IsConnected(d.Id) == true;
            AddSelectorItem(host, d.Name, d, connected);
        }
    }

    private void AddSelectorItem(ItemsControl host, string label, RemoteDevice? device, bool connected)
    {
        var isActive = device == _activeDevice;

        if (host.Items.Count > 0)
        {
            host.Items.Add(new Border
            {
                Width = 1,
                Background = (Brush)Application.Current.Resources["MutedTextBrush"],
                Opacity = 0.3,
                Margin = new Thickness(8, 2, 8, 2)
            });
        }

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 6, Height = 6,
            Margin = new Thickness(0, 0, 5, 0),
            Fill = new SolidColorBrush(connected
                ? Color.FromRgb(0x3D, 0xC9, 0x8A)
                : Color.FromRgb(0x45, 0x47, 0x5A)),
            VerticalAlignment = VerticalAlignment.Center
        };

        var text = new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = isActive
                ? (Brush)Application.Current.Resources["TextBrush"]
                : (Brush)Application.Current.Resources["MutedTextBrush"],
            VerticalAlignment = VerticalAlignment.Center
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(dot);
        row.Children.Add(text);

        var btn = new Button
        {
            Content = row,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = Cursors.Hand,
            Tag = device
        };
        btn.Click += (_, _) => SwitchSettingsDevice(device);
        host.Items.Add(btn);
    }

    private void SwitchSettingsDevice(RemoteDevice? device)
    {
        _activeDevice = device;
        BuildDeviceSelector();

        // Show/hide sections that don't apply to remote devices
        var isRemote = device != null;
        // Remote monitoring section only for Local
        // (handled by hiding in XAML via x:Name — we'll just reload)
        _loading = true;
        LoadFromSettings();
        _loading = false;
        UpdateSwatches();
        LoadTileDeviceCombo();

        // Show/hide Remote Devices section (only for Local)
        if (RemoteDevicesSection != null)
            RemoteDevicesSection.Visibility = isRemote ? Visibility.Collapsed : Visibility.Visible;
        if (RemoteMonitoringSection != null)
            RemoteMonitoringSection.Visibility = isRemote ? Visibility.Collapsed : Visibility.Visible;
        if (NetworkSection != null)
            NetworkSection.Visibility = isRemote ? Visibility.Collapsed : Visibility.Visible;
    }

    // ------------------------------------------------------------------
    // Tile label customization
    // ------------------------------------------------------------------
    // ── Appearance: Skin cycler, Theme cycler, Presets, Color picker ─────
    // ── Skin cycler ───────────────────────────────────────────────────────
    private List<SkinInfo> _skins = new();
    private int _skinIndex = 0;

    private void LoadSkinCycler()
    {
        _skins = SkinManager.GetAllSkins();
        _skinIndex = Math.Max(0, _skins.FindIndex(s => s.Name == App.Current.Settings.ActiveSkin));
        UpdateSkinCycler();
    }

    private void UpdateSkinCycler()
    {
        if (_skins.Count == 0) return;
        SkinNameLabel.Text = _skins[_skinIndex].Name;
    }

    private void OnSkinPrev(object sender, RoutedEventArgs e)
    {
        _skinIndex = (_skinIndex - 1 + _skins.Count) % _skins.Count;
        UpdateSkinCycler();
        ApplyCurrentSkin();
        ClearActiveTheme();  // v1.21
    }

    private void OnSkinNext(object sender, RoutedEventArgs e)
    {
        _skinIndex = (_skinIndex + 1) % _skins.Count;
        UpdateSkinCycler();
        ApplyCurrentSkin();
        ClearActiveTheme();  // v1.21
    }

    // --- v1.20: Dice button on Skins row randomizes SKIN ONLY now.
    // The new Preset Themes row has its own dice (OnRandomizeThemePreset)
    // that picks a Theme = colors + skin together. The previous "dice
    // randomizes both" behavior is now split across the two rows for
    // independent control.
    // v1.25.37: per user, this dice now rolls skin AND a random color
    // palette (independent picks -- a mashup). Distinct from the Theme
    // dice, which applies a DESIGNED colors+skin combo. The Colors row
    // deliberately has no dice of its own.
    // If RandomizeFontsOnDice is set, this still rolls fonts (since fonts
    // aren't a separate cycler row).
    private static readonly System.Random _diceRng = new System.Random();
    private void OnRandomizeAppearance(object sender, RoutedEventArgs e)
    {
        PushUndoSnapshot();
        _suppressSnapshot = true;
        try
        {
            var s = App.Current.Settings;
            var res = Application.Current.Resources;
            var prePrimary   = (res["WidgetPrimaryFont"]   as FontFamily)?.Source ?? s.PrimaryFont;
            var preSecondary = (res["WidgetSecondaryFont"] as FontFamily)?.Source ?? s.SecondaryFont;
            var preIndicator = (res["WidgetIndicatorFont"] as FontFamily)?.Source ?? s.IndicatorFont;

            if (_skins.Count > 0)
            {
                _skinIndex = _diceRng.Next(0, _skins.Count);
                UpdateSkinCycler();
                ApplyCurrentSkin();
                ClearActiveTheme();
                // v1.25.59: pump the render queue so WPF processes the skin
                // resource change before the theme change hits. Prevents the
                // rendering pipeline from freezing on rapid resource swaps.
                Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
            }

            var allPalettes = AllPresets();
            if (allPalettes.Count > 1)
            {
                _themeIndex = _diceRng.Next(1, allPalettes.Count);
                UpdateThemeCycler();
                ApplyCurrentCyclerTheme();
            }

            if (s.RandomizeFontsOnDice)
            {
                var primary   = FontCatalog.Random(_diceRng);
                var secondary = s.SyncFonts ? primary : FontCatalog.Random(_diceRng);
                var indicator = s.SyncFonts ? primary : FontCatalog.Random(_diceRng);
                _loading = true;
                try
                {
                    PrimaryFontCombo.SelectedItem   = primary;
                    SecondaryFontCombo.SelectedItem = secondary;
                    IndicatorFontCombo.SelectedItem = indicator;
                }
                finally { _loading = false; }
                s.PrimaryFont   = primary   == FontCatalog.DefaultEntry ? "" : primary;
                s.SecondaryFont = secondary == FontCatalog.DefaultEntry ? "" : secondary;
                s.IndicatorFont = indicator == FontCatalog.DefaultEntry ? "" : indicator;
                SettingsService.Save(s);
                SkinManager.ApplyFontOverrides(s, Application.Current.Resources);
            }

            if (!s.RandomizeFontsOnDice)
            {
                s.PrimaryFont   = prePrimary;
                s.SecondaryFont = preSecondary;
                s.IndicatorFont = preIndicator;
                SettingsService.Save(s);
                SkinManager.ApplyFontOverrides(s, Application.Current.Resources);
                _loading = true;
                try
                {
                    PrimaryFontCombo.SelectedItem   = string.IsNullOrEmpty(prePrimary)   ? FontCatalog.DefaultEntry : prePrimary;
                    SecondaryFontCombo.SelectedItem = string.IsNullOrEmpty(preSecondary) ? FontCatalog.DefaultEntry : preSecondary;
                    IndicatorFontCombo.SelectedItem = string.IsNullOrEmpty(preIndicator) ? FontCatalog.DefaultEntry : preIndicator;
                }
                finally { _loading = false; }
            }
        }
        catch (System.Exception ex)
        {
            // v1.25.58: recover from rendering crashes during rapid resource swaps
            System.Diagnostics.Debug.WriteLine($"Dice crash recovered: {ex.Message}");
            try { ThemeApplier.Apply(App.Current.Settings, Application.Current.Resources); UpdateSwatches(); } catch { }
        }
        finally { _suppressSnapshot = false; }
        // v1.25.58: deferred full refresh for UI consistency after dice
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,
            new Action(() => { try { Fmt.InvalidateAllDeferred(); UpdateSwatches(); } catch { } }));
    }
    // --------------------------------------------------------------------

    // v1.25.37: right-click on the Skin dice randomizes SKIN ONLY
    // (no color change). Left-click remains skin + colors.
    private void OnRandomizeSkinOnly(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Right) return;
        e.Handled = true;
        PushUndoSnapshot();
        _suppressSnapshot = true;
        try
        {
            if (_skins.Count > 0)
            {
                _skinIndex = _diceRng.Next(0, _skins.Count);
                UpdateSkinCycler();

                // Capture resolved fonts before skin apply (same fix as left-click)
                var s = App.Current.Settings;
                var res = Application.Current.Resources;
                var prePrimary   = (res["WidgetPrimaryFont"]   as FontFamily)?.Source ?? s.PrimaryFont;
                var preSecondary = (res["WidgetSecondaryFont"] as FontFamily)?.Source ?? s.SecondaryFont;
                var preIndicator = (res["WidgetIndicatorFont"] as FontFamily)?.Source ?? s.IndicatorFont;

                ApplyCurrentSkin();
                ClearActiveTheme();

                // Restore fonts unconditionally (skin-only = never touch fonts)
                s.PrimaryFont   = prePrimary;
                s.SecondaryFont = preSecondary;
                s.IndicatorFont = preIndicator;
                SettingsService.Save(s);
                SkinManager.ApplyFontOverrides(s, Application.Current.Resources);
            }
        }
        finally { _suppressSnapshot = false; }
        Fmt.InvalidateAllDeferred();
    }

    // --- v1.20: Preset Themes cycler ---
    // A Theme = colors (5 hex) + skin (string). Applying a theme sets BOTH
    // atomically -- the user gets a coherent gaming-franchise look in one click.
    // The cycler iterates through ThemeApplier.BuiltInThemes (89 entries grouped
    // by franchise) plus AppSettings.CustomThemes (loaded from .fluidtheme JSON).
    private List<ThemeApplier.BuiltInTheme> _themePresets = new();
    private int _themePresetIndex = 0;

    // v1.21: a manual color or skin change means the user has left the
    // selected Preset Theme. AppSettings documents that ActiveTheme should be
    // cleared in that case, but nothing ever did -- the Preset Themes cycler
    // showed a stale theme name on reopen. Called from every manual
    // color/skin mutation path. (Save happens in each caller.)
    private void ClearActiveTheme()
    {
        App.Current.Settings.ActiveTheme = "";
    }

    private void LoadThemePresetCycler()
    {
        // Combine built-in themes with custom (.fluidtheme) ones
        _themePresets = ThemeApplier.GetAllThemes();
        foreach (var ct in App.Current.Settings.CustomThemes)
        {
            _themePresets.Add(new ThemeApplier.BuiltInTheme(
                ct.Name, string.IsNullOrEmpty(ct.Franchise) ? "Custom" : ct.Franchise,
                ct.BackgroundColor, ct.TileColor, ct.AccentColor,
                ct.TextColor, ct.MutedTextColor, ct.SkinName));
        }

        // Sync index to ActiveTheme name in settings, or 0 if no match.
        var activeName = App.Current.Settings.ActiveTheme;
        if (!string.IsNullOrEmpty(activeName))
        {
            var i = _themePresets.FindIndex(t => t.Name == activeName);
            if (i >= 0) _themePresetIndex = i;
            else        _themePresetIndex = 0;
        }
        else { _themePresetIndex = 0; }
        UpdateThemePresetCycler();
    }

    private void UpdateThemePresetCycler()
    {
        if (_themePresets.Count == 0) return;
        var t = _themePresets[_themePresetIndex];
        // v1.20.1: strip the franchise prefix from the cycler arrow label so it
        // reads cleanly ("Velvet Room" not "Persona 5 Velvet Room"). The data
        // still carries the full name so the browse popup's franchise grouping
        // continues to work correctly. We only strip the prefix that exactly
        // matches the entry's own Franchise field, so user-typed names without
        // a franchise prefix are unaffected.
        var displayName = t.Name;
        if (!string.IsNullOrEmpty(t.Franchise) && displayName.StartsWith(t.Franchise + " "))
            displayName = displayName.Substring(t.Franchise.Length + 1);
        ThemePresetNameLabel.Text = displayName;
        try
        {
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(t.Accent);
            ThemePresetAccentDot.Fill = new System.Windows.Media.SolidColorBrush(color);
        }
        catch { ThemePresetAccentDot.Fill = System.Windows.Media.Brushes.Transparent; }
    }

    /// <summary>v1.20: Apply a theme = atomic colors + skin update.</summary>
    private static bool TryComputeIsDark(string hex)
    {
        try
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return (c.R + c.G + c.B) < 384;
        }
        catch { return true; }
    }

    private void ApplyThemePreset(ThemeApplier.BuiltInTheme t)
    {
        var s = App.Current.Settings;
        // Set colors
        s.BackgroundColor = t.Bg;
        s.TileColor       = t.Tile;
        s.AccentColor     = t.Accent;
        s.TextColor       = t.Text;
        s.MutedTextColor  = t.Muted;
        s.IsDarkMode      = TryComputeIsDark(t.Bg);
        s.ActiveTheme     = t.Name;
        ThemeApplier.Apply(s, Application.Current.Resources);

        // Apply paired skin via the proven cycler path (same as Skins row).
        var skinIdx = _skins.FindIndex(sk => sk.Name == t.SkinName);
        if (skinIdx >= 0)
        {
            _skinIndex = skinIdx;
            UpdateSkinCycler();
            ApplyCurrentSkin();
        }
        // else: skin not installed, leave the current one; colors still apply

        // Refresh UI bindings
        _loading = true;
        BackgroundColorBox.Text = s.BackgroundColor; TileColorBox.Text = s.TileColor;
        AccentColorBox.Text     = s.AccentColor;     TextColorBox.Text = s.TextColor;
        MutedTextColorBox.Text  = s.MutedTextColor;
        DarkModeBtn.IsChecked   = s.IsDarkMode;
        LightModeBtn.IsChecked  = !s.IsDarkMode;
        _loading = false;
        UpdateSwatches();
        _themeIndex = FindCurrentThemeIndex();
        UpdateThemeCycler();
        SettingsService.Save(s);
    }

    private void OnThemePresetPrev(object sender, RoutedEventArgs e)
    {
        if (_themePresets.Count == 0) return;
        PushUndoSnapshot();
        _suppressSnapshot = true;
        try
        {
            _themePresetIndex = (_themePresetIndex - 1 + _themePresets.Count) % _themePresets.Count;
            UpdateThemePresetCycler();
            ApplyThemePreset(_themePresets[_themePresetIndex]);
        }
        finally { _suppressSnapshot = false; }
    }

    private void OnThemePresetNext(object sender, RoutedEventArgs e)
    {
        if (_themePresets.Count == 0) return;
        PushUndoSnapshot();
        _suppressSnapshot = true;
        try
        {
            _themePresetIndex = (_themePresetIndex + 1) % _themePresets.Count;
            UpdateThemePresetCycler();
            ApplyThemePreset(_themePresets[_themePresetIndex]);
        }
        finally { _suppressSnapshot = false; }
    }

    private void OnRandomizeThemePreset(object sender, RoutedEventArgs e)
    {
        if (_themePresets.Count == 0) return;
        PushUndoSnapshot();
        _suppressSnapshot = true;
        try
        {
            _themePresetIndex = _diceRng.Next(0, _themePresets.Count);
            UpdateThemePresetCycler();
            ApplyThemePreset(_themePresets[_themePresetIndex]);
            // v1.25.22: Theme dice with shuffle ON randomizes fonts; with
            // shuffle OFF the theme's default applies (themes don't store
            // a font, so SkinManager.ApplyFontOverrides honors whatever
            // the user already had). Skin dice handler intentionally
            // doesn't touch fonts at all unless shuffle is on -- only
            // theme changes might want custom font selection.
            var s = App.Current.Settings;
            if (s.RandomizeFontsOnDice)
            {
                var primary   = FontCatalog.Random(_diceRng);
                var secondary = s.SyncFonts ? primary : FontCatalog.Random(_diceRng);
                var indicator = s.SyncFonts ? primary : FontCatalog.Random(_diceRng);
                _loading = true;
                try
                {
                    PrimaryFontCombo.SelectedItem   = primary;
                    SecondaryFontCombo.SelectedItem = secondary;
                    IndicatorFontCombo.SelectedItem = indicator;
                }
                finally { _loading = false; }
                s.PrimaryFont   = primary   == FontCatalog.DefaultEntry ? "" : primary;
                s.SecondaryFont = secondary == FontCatalog.DefaultEntry ? "" : secondary;
                s.IndicatorFont = indicator == FontCatalog.DefaultEntry ? "" : indicator;
                SettingsService.Save(s);
                SkinManager.ApplyFontOverrides(s, Application.Current.Resources);
            }
        }
        finally { _suppressSnapshot = false; }
    }

    private void OnUndoThemePreset(object sender, RoutedEventArgs e)
    {
        // Reuse the global appearance undo - it captures the full state
        // so undoing the last theme change goes back to the previous state.
        OnUndoAppearance(sender, e);
    }

    private void OnThemePresetBrowse(object sender, RoutedEventArgs e)
    {
        // v1.20: Group themes by Franchise with collapsible group headers.
        // The popup uses a CollectionView with grouping.
        var items = _themePresets.Select(t => new ThemePresetBrowseItem
        {
            Name        = t.Name,
            DisplayName = (!string.IsNullOrEmpty(t.Franchise) && t.Name.StartsWith(t.Franchise + " "))
                          ? t.Name.Substring(t.Franchise.Length + 1)
                          : t.Name,
            Franchise = t.Franchise,
            AccentHex = t.Accent,
        }).ToList();

        var cv = System.Windows.Data.CollectionViewSource.GetDefaultView(items);
        if (cv.GroupDescriptions != null)
        {
            cv.GroupDescriptions.Clear();
            cv.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Franchise"));
        }
        ThemePresetBrowseList.ItemsSource = cv;
        ThemePresetBrowsePopup.IsOpen = true;
    }

    public class ThemePresetBrowseItem
    {
        public string Name        { get; set; } = "";  // full name (for click lookup)
        public string DisplayName { get; set; } = "";  // franchise-stripped name (for UI)
        public string Franchise   { get; set; } = "";
        public string AccentHex   { get; set; } = "";
        public System.Windows.Media.Brush AccentBrush
        {
            get
            {
                try
                {
                    var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(AccentHex);
                    return new System.Windows.Media.SolidColorBrush(c);
                }
                catch { return System.Windows.Media.Brushes.Transparent; }
            }
        }
    }

    private void OnThemePresetBrowseItemClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.DataContext is ThemePresetBrowseItem it)
        {
            var idx = _themePresets.FindIndex(t => t.Name == it.Name);
            if (idx >= 0)
            {
                PushUndoSnapshot();
                _suppressSnapshot = true;
                try
                {
                    _themePresetIndex = idx;
                    UpdateThemePresetCycler();
                    ApplyThemePreset(_themePresets[_themePresetIndex]);
                }
                finally { _suppressSnapshot = false; }
                ThemePresetBrowsePopup.IsOpen = false;
            }
        }
    }

    /// <summary>v1.20: Load a .fluidtheme JSON file from disk.</summary>
    private void OnThemePresetFromFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load a fluidtheme file",
            Filter = "fluidMonitor Theme (*.fluidtheme)|*.fluidtheme|JSON (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var json = System.IO.File.ReadAllText(dlg.FileName);
            var loaded = System.Text.Json.JsonSerializer.Deserialize<CustomTheme>(json);
            if (loaded == null || string.IsNullOrEmpty(loaded.Name) || string.IsNullOrEmpty(loaded.BackgroundColor))
            {
                FluidMessageBox.Show("That file doesn't appear to be a valid .fluidtheme.", "Load failed", owner: this);
                return;
            }
            if (string.IsNullOrEmpty(loaded.SkinName)) loaded.SkinName = "Default";
            App.Current.Settings.CustomThemes.Add(loaded);
            SettingsService.Save(App.Current.Settings);
            LoadThemePresetCycler();
            // Point cycler at the just-added theme
            var idx = _themePresets.FindIndex(t => t.Name == loaded.Name);
            if (idx >= 0)
            {
                _themePresetIndex = idx;
                UpdateThemePresetCycler();
                ApplyThemePreset(_themePresets[idx]);
            }
        }
        catch (System.Exception ex)
        {
            FluidMessageBox.Show("Could not load theme: " + ex.Message, "Load failed", owner: this);
        }
    }
    // --------------------------------------------------------------------

    // --- v1.17: Skin browse popup -- real mini-tile previews -------------
    // Each row in the popup is now an actual mini tile rendered from the
    // skin's real resources (border thickness, corner radius, border brush,
    // optional accent stripe) on top of the current theme colors. The XAML
    // template inserts these UIElements via a ContentControl.
    private void OnSkinBrowse(object sender, RoutedEventArgs e)
    {
        var items = _skins.Select(sk => new
        {
            Name           = sk.Name,
            PreviewElement = BuildSkinPreviewElement(sk),
        }).ToList();
        SkinBrowseList.ItemsSource = items;
        SkinBrowsePopup.IsOpen = true;
    }

    /// <summary>
    /// v1.17: Build a UIElement representing a skin in the picker. Reads the
    /// skin's actual ResourceDictionary so the preview reflects its real
    /// structural choices (border thickness, corner radius, accent stripe).
    /// Colors come from the current theme so the user sees what they'd get.
    /// </summary>
    private System.Windows.UIElement BuildSkinPreviewElement(SkinInfo skin)
    {
        System.Windows.ResourceDictionary? dict = null;
        try
        {
            if (skin.IsBuiltIn)
            {
                var uri = new System.Uri($"pack://application:,,,/Styles/Skins/{skin.Name}.xaml", System.UriKind.Absolute);
                dict = new System.Windows.ResourceDictionary { Source = uri };
            }
            else if (!string.IsNullOrEmpty(skin.FolderPath))
            {
                var path = System.IO.Path.Combine(skin.FolderPath, "skin.xaml");
                if (System.IO.File.Exists(path))
                {
                    using var fs = System.IO.File.OpenRead(path);
                    dict = (System.Windows.ResourceDictionary)System.Windows.Markup.XamlReader.Load(fs);
                }
            }
        }
        catch { /* generic preview will be used */ }

        TStruct GetStruct<TStruct>(string key, TStruct fallback) where TStruct : struct
        {
            try { return dict?[key] is TStruct ts ? ts : fallback; }
            catch { return fallback; }
        }
        string GetString(string key, string fallback)
        {
            try { return dict?[key] as string ?? fallback; }
            catch { return fallback; }
        }
        double GetDouble(string key, double fallback)
        {
            try
            {
                var v = dict?[key];
                if (v is double d)  return d;
                if (v is int i)     return i;
                return fallback;
            }
            catch { return fallback; }
        }

        // Resolve theme brushes from the live app resources
        var bgBrush     = Application.Current.Resources["BgBrush"]        as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Black;
        var tileBrush   = Application.Current.Resources["TileBrush"]      as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DimGray;
        var accentBrush = Application.Current.Resources["AccentBrush"]    as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.DodgerBlue;
        var textBrush   = Application.Current.Resources["TextBrush"]      as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.White;
        var mutedBrush  = Application.Current.Resources["MutedTextBrush"] as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;

        var corner        = GetStruct("SkinTileCornerRadius", new System.Windows.CornerRadius(4));
        var borderThk     = GetStruct("SkinTileBorderThickness", new System.Windows.Thickness(0));
        var contentMargin = GetStruct("SkinContentMargin", new System.Windows.Thickness(0));
        var borderSource  = GetString("SkinTileBorderSource", "Accent");
        var accentBarW    = GetDouble("SkinAccentBarWidth", 0);

        var borderBrush = borderSource switch
        {
            "Accent"      => accentBrush,
            "Text"        => textBrush,
            "Muted"       => mutedBrush,
            "Background"  => bgBrush,
            "Tile"        => tileBrush,
            "Transparent" => System.Windows.Media.Brushes.Transparent,
            _             => accentBrush,
        };

        // Outer 2-column grid: [mini tile | name label]
        var outer = new System.Windows.Controls.Grid
        {
            Height = 36,
            Margin = new System.Windows.Thickness(0),
        };
        outer.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(64) });
        outer.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });

        // Mini tile -- uses the skin's real structural values
        var miniTile = new System.Windows.Controls.Border
        {
            CornerRadius    = corner,
            BorderThickness = borderThk,
            BorderBrush     = borderBrush,
            Background      = tileBrush,
            Width           = 54,
            Height          = 28,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment   = System.Windows.VerticalAlignment.Center,
        };

        // If the skin draws a left accent stripe, render it inside the mini tile
        if (accentBarW > 0 || contentMargin.Left > 0)
        {
            var stripeGrid = new System.Windows.Controls.Grid();
            stripeGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(3) });
            stripeGrid.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new System.Windows.GridLength(1, System.Windows.GridUnitType.Star) });
            var stripe = new System.Windows.Controls.Border { Background = accentBrush };
            System.Windows.Controls.Grid.SetColumn(stripe, 0);
            stripeGrid.Children.Add(stripe);
            miniTile.Child = stripeGrid;
        }

        System.Windows.Controls.Grid.SetColumn(miniTile, 0);
        outer.Children.Add(miniTile);

        // Skin name label
        var label = new System.Windows.Controls.TextBlock
        {
            Text       = skin.Name,
            FontSize   = 12,
            FontWeight = System.Windows.FontWeights.SemiBold,
            Foreground = textBrush,
            VerticalAlignment   = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Margin     = new System.Windows.Thickness(8, 0, 0, 0),
        };
        System.Windows.Controls.Grid.SetColumn(label, 1);
        outer.Children.Add(label);

        return outer;
    }

    /// <summary>
    /// v1.15: returns a Brush that conveys a skin's visual character in the
    /// picker dropdown. Built-in skins have curated gradients; unknown skins
    /// <summary>
    /// v1.16: returns a Brush that fills an entire skin-picker row, conveying
    /// that skin's visual character at full-row scale. Horizontal gradients
    /// with multiple stops where appropriate. Unknown skins fall back to the
    /// active accent (faded) so user skins still show something.
    /// </summary>
    private System.Windows.Media.Brush MakeSkinPreviewBrush(string skinName)
    {
        // Helper to build a horizontal multi-stop gradient (left-to-right)
        static System.Windows.Media.Brush HGrad(params (double Offset, string Hex)[] stops)
        {
            var g = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint   = new System.Windows.Point(1, 0),
            };
            foreach (var s in stops)
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(s.Hex);
                g.GradientStops.Add(new System.Windows.Media.GradientStop(c, s.Offset));
            }
            return g;
        }
        // Diagonal gradient for skins with a directional sheen
        static System.Windows.Media.Brush DGrad(params (double Offset, string Hex)[] stops)
        {
            var g = new System.Windows.Media.LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint   = new System.Windows.Point(1, 1),
            };
            foreach (var s in stops)
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(s.Hex);
                g.GradientStops.Add(new System.Windows.Media.GradientStop(c, s.Offset));
            }
            return g;
        }
        static System.Windows.Media.Brush Solid(string hex)
        {
            var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            return new System.Windows.Media.SolidColorBrush(c);
        }
        return skinName switch
        {
            "Default"       => HGrad((0,"#FF505068"),(1,"#FF353550")),
            "Minimal"       => Solid("#FFC8C8CE"),
            "Sharp"         => HGrad((0,"#FF0066AA"),(0.5,"#FF00A8FF"),(1,"#FF0066AA")),
            "Glassmorphism" => DGrad((0,"#FFE0EEFF"),(0.4,"#FFB0CCEE"),(1,"#FF7090CC")),  // sheen
            "Retro"         => HGrad((0,"#FFFFAA22"),(0.5,"#FFFF7700"),(1,"#FFCC4400")),
            "Terminal"      => HGrad((0,"#FF002200"),(0.5,"#FF00FF44"),(1,"#FF002200")),  // CRT phosphor glow
            "Holographic"   => HGrad((0,"#FF00E5FF"),(0.33,"#FFFF00DD"),(0.66,"#FFFFEE00"),(1,"#FF00FF99")),
            "Brutalist"     => HGrad((0,"#FFFFFFFF"),(0.5,"#FF888888"),(0.5,"#FF222222"),(1,"#FF000000")),  // sharp split
            "Carbon"        => HGrad((0,"#FF1A1A1F"),(0.5,"#FF505056"),(1,"#FF1A1A1F")),  // fiber weave
            "Neon"          => HGrad((0,"#FFFF00DD"),(0.5,"#FF8800FF"),(1,"#FF00FFFF")),
            "Frosted"       => DGrad((0,"#FFFFFFFF"),(0.5,"#FFC8D8E8"),(1,"#FF8AA0BC")),
            "Cyberpunk"     => HGrad((0,"#FF00FFFF"),(0.5,"#FFFF0080"),(1,"#FFFFEE00")),
            "Paper"         => HGrad((0,"#FFFFF8E8"),(0.5,"#FFEDE0C8"),(1,"#FFD5C8B0")),
            "Ink"           => HGrad((0,"#FF1A1A1A"),(1,"#FF000000")),
            "Aurora"        => HGrad((0,"#FF00FF88"),(0.4,"#FF00CCFF"),(0.7,"#FFAA66FF"),(1,"#FFFF00AA")),
            "Compact"       => Solid("#FF656575"),
            _               => (System.Windows.Media.Brush)Application.Current.Resources["AccentBrush"],
        };
    }

    /// <summary>
    /// v1.16: returns a text-color brush appropriate for reading on top of
    /// the corresponding MakeSkinPreviewBrush. Light-toned skins get near-black
    /// text; dark-toned skins get near-white. Hand-picked rather than computed
    /// because the gradient brushes have multiple stops and luminance varies
    /// across the row.
    /// </summary>
    private System.Windows.Media.Brush MakeSkinTextBrush(string skinName)
    {
        var black = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FF111111"));
        var white = new System.Windows.Media.SolidColorBrush(
            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFF5F5F5"));
        return skinName switch
        {
            "Minimal"       => black,
            "Glassmorphism" => black,  // light gradient
            "Frosted"       => black,
            "Paper"         => black,
            "Brutalist"     => white,  // dark half dominates visually with text shadow
            _               => white,
        };
    }

    private void OnSkinBrowseItemClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.Tag is string name)
        {
            var idx = _skins.FindIndex(sk => sk.Name == name);
            if (idx >= 0)
            {
                _skinIndex = idx;
                UpdateSkinCycler();
                ApplyCurrentSkin();
                ClearActiveTheme();  // v1.21
            }
            SkinBrowsePopup.IsOpen = false;
        }
    }

    // --- v1.13: Font family handlers (v1.14: +undo snapshot) ------------
    private void OnPrimaryFontFamilyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (PrimaryFontCombo.SelectedItem is not string font) return;
        PushUndoSnapshot();
        _suppressSnapshot = true; // sync-mirror below sets two more combos which would re-snapshot
        try
        {
            var s = App.Current.Settings;
            s.PrimaryFont = IsDefaultFontEntry(font) ? "" : font;

            if (s.SyncFonts)
            {
                _loading = true;
                try
                {
                    SecondaryFontCombo.SelectedItem = font;
                    IndicatorFontCombo.SelectedItem = font;
                }
                finally { _loading = false; }
                s.SecondaryFont = s.PrimaryFont;
                s.IndicatorFont = s.PrimaryFont;
            }
            SettingsService.Save(s);
            SkinManager.ApplyFontOverrides(s, Application.Current.Resources);
        }
        finally { _suppressSnapshot = false; }
    }

    private void OnSecondaryFontFamilyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (SecondaryFontCombo.SelectedItem is not string font) return;
        PushUndoSnapshot();
        var s = App.Current.Settings;
        s.SecondaryFont = IsDefaultFontEntry(font) ? "" : font;
        SettingsService.Save(s);
        SkinManager.ApplyFontOverrides(s, Application.Current.Resources);
    }

    private void OnIndicatorFontFamilyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (IndicatorFontCombo.SelectedItem is not string font) return;
        PushUndoSnapshot();
        var s = App.Current.Settings;
        s.IndicatorFont = IsDefaultFontEntry(font) ? "" : font;
        SettingsService.Save(s);
        SkinManager.ApplyFontOverrides(s, Application.Current.Resources);
    }

    // v1.25.15: ─── skin-aware "(skin default)" label ────────────────────
    // The combos store FontCatalog.DefaultEntry ("(skin default)") at index 0
    // as the "no override" choice. To make it useful we replace that string
    // at render time with "(skin default — <skin font name>)" so the user
    // can see what they'll actually get. The stored AppSettings value stays
    // "" (empty) for the no-override case -- only the display label changes.

    private static bool IsDefaultFontEntry(string? s)
        => s == null || s == FontCatalog.DefaultEntry || s.StartsWith("(skin default", System.StringComparison.Ordinal);

    private static string ResolveFontComboSelection(string? settingValue)
    {
        // Empty / missing settings value -> use the live combo's index-0 label
        // (whatever flavour of "(skin default...)" it currently shows).
        // The caller still selects-by-string, so we return the literal label.
        // Since callers select on PrimaryFontCombo specifically, we look up
        // the actual placeholder at runtime via the static helper below.
        if (string.IsNullOrEmpty(settingValue)) return CurrentDefaultLabel;
        return settingValue;
    }

    // Cached display label for the no-override entry; refreshed when the
    // active skin (and therefore SkinFontFamily resource) changes.
    private static string CurrentDefaultLabel { get; set; } = FontCatalog.DefaultEntry;

    private void RefreshFontComboPlaceholders()
    {
        var skinFont = Application.Current.Resources["SkinFontFamily"] as System.Windows.Media.FontFamily;
        // v1.25.16: previous label was "(skin default — Cascadia Mono)" which
        // took too much horizontal space in the narrow font combo. Now we
        // just show the actual skin font name (e.g. "Cascadia Mono") at
        // index 0. The combo's index-0 entry is still the "no override"
        // token regardless of display label; the change handlers detect it
        // via combo.SelectedIndex == 0 in IsDefaultFontEntry.
        // v1.25.17: a FontFamily's Source can be a comma-separated fallback
        // chain (e.g. "Inter, Segoe UI, Arial"). Show only the first entry
        // -- that's the font WPF will actually try to use first -- so the
        // combo doesn't render as an unreadable wrapped chain.
        var source = skinFont?.Source ?? FontCatalog.DefaultEntry;
        var label = source.Split(',')[0].Trim();
        if (string.IsNullOrEmpty(label)) label = FontCatalog.DefaultEntry;
        CurrentDefaultLabel = label;

        // Item 0 in each combo IS the default entry placeholder; rewrite it.
        // Setting Items[0] = newString without changing selection works because
        // ComboBox keys items by reference equality only when the item is bound;
        // for plain strings we update both Items[0] and the SelectedItem if it
        // was the default.
        foreach (var combo in new[] { PrimaryFontCombo, SecondaryFontCombo, IndicatorFontCombo })
        {
            if (combo.Items.Count == 0) continue;
            var wasDefault = combo.SelectedIndex == 0;
            _loading = true;
            try
            {
                combo.Items[0] = label;
                if (wasDefault) combo.SelectedIndex = 0;
            }
            finally { _loading = false; }
        }
    }

    private void OnSyncFontsToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = App.Current.Settings;
        s.SyncFonts = SyncFontsCheck.IsChecked == true;
        SettingsService.Save(s);
        // If turning sync ON, immediately mirror Primary to the others
        if (s.SyncFonts && PrimaryFontCombo.SelectedItem is string pf)
        {
            _loading = true;
            try { SecondaryFontCombo.SelectedItem = pf; IndicatorFontCombo.SelectedItem = pf; }
            finally { _loading = false; }
            s.SecondaryFont = pf == FontCatalog.DefaultEntry ? "" : pf;
            s.IndicatorFont = pf == FontCatalog.DefaultEntry ? "" : pf;
            SettingsService.Save(s);
            SkinManager.ApplyFontOverrides(s, Application.Current.Resources);
        }
    }

    private void OnRandomizeFontsToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = App.Current.Settings;
        s.RandomizeFontsOnDice = RandomizeFontsCheck.IsChecked == true;
        SettingsService.Save(s);
    }

    // --- v1.13: Save-current-as-preset button ---------------------------
    // The dedicated Save button next to the preset slots opens the same
    // SavePresetPanel that the [SAVE] icon in an empty slot would have opened.
    // Default target slot is the first empty slot, or slot 1 if all are full.
    private void OnSaveCurrentClicked(object sender, RoutedEventArgs e)
    {
        var presets = App.Current.Settings.UserPresets;
        int target = presets.FindIndex(p => p.IsEmpty);
        if (target < 0) target = 0;
        ShowSavePresetDialog(target);
    }

    // v1.19: + button next to the Colors cycler. Opens a small naming panel
    // that saves the CURRENT colors (background/tile/accent/text/muted) into
    // AppSettings.CustomColors with a user-chosen name. Skin and fonts are
    // NOT captured -- per user clarification, the 5-slot Saved Presets are
    // for full combos; this button is colors-only. The custom color shows
    // up in the Colors cycler dropdown alongside built-ins, and is
    // user-deletable via the X icon next to it in the browse popup.
    private void OnSaveColorPresetClicked(object sender, RoutedEventArgs e)
    {
        _pendingCustomColorSave = true;
        // Suggest a default name based on the current accent
        var s = App.Current.Settings;
        var suggested = !string.IsNullOrEmpty(s.AccentColor)
            ? $"Custom {s.AccentColor.Substring(System.Math.Max(0, s.AccentColor.Length - 6))}"
            : "Custom Color";
        SavePresetNameBox.Text = suggested;
        SavePresetLabel.Text   = "Name this color set";
        SavePresetPanel.Visibility = Visibility.Visible;
        SavePresetNameBox.Focus();
        SavePresetNameBox.SelectAll();
    }
    // Flag set when SavePresetPanel was opened by the + button (not by an
    // empty preset slot). The shared OnConfirmSavePreset checks this and
    // routes to CustomColors instead of UserPresets[slot].
    private bool _pendingCustomColorSave = false;

    // v1.19: X button next to a CustomColor entry in the theme browse popup.
    // Pops a confirm popup (reuses ClearConfirmPopup styling via a small flag),
    // and on confirm removes the named CustomColor from settings.
    private string? _pendingDeleteCustomColor = null;
    private void OnDeleteCustomColor(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var baseName = btn.Tag as string;
        if (string.IsNullOrEmpty(baseName)) return;
        _pendingDeleteCustomColor = baseName;
        // Reuse the existing ClearConfirmPopup since visually it does the same job.
        // The Yes-click handler checks _pendingDeleteCustomColor BEFORE _pendingClearSlot.
        ClearConfirmLabel.Text = $"Delete \"{baseName}\"?";
        ClearConfirmPopup.PlacementTarget = btn;
        ClearConfirmPopup.IsOpen = true;
        // Stop the click from bubbling up to the parent Border's MouseLeftButtonUp
        // (which would otherwise pick the row -- not what the user wants here).
        e.Handled = true;
    }

    // --- v1.15: Export / Import appearance share codes --------------------
    // Tracks whether the ShareCodePanel is currently in Export or Import mode
    // so the Apply button knows what to do. Export: Apply is hidden, only Copy
    // + Close are useful. Import: Copy is hidden, Apply + Close are.
    private bool _shareCodeIsImportMode = false;

    private void OnExportShareCode(object sender, RoutedEventArgs e)
    {
        var code = AppearanceShareCodec.Export(App.Current.Settings);
        _shareCodeIsImportMode = false;
        ShareCodeLabel.Text = "Share code (copy this to share your appearance)";
        ShareCodeBox.Text   = code;
        ShareCodeBox.IsReadOnly = true;
        ShareCodeCopyBtn.Visibility  = Visibility.Visible;
        ShareCodeApplyBtn.Visibility = Visibility.Collapsed;
        ShareCodeStatus.Text = "";
        ShareCodePanel.Visibility = Visibility.Visible;
        ShareCodeBox.Focus();
        ShareCodeBox.SelectAll();
        // Also copy to clipboard immediately as a convenience
        try { System.Windows.Clipboard.SetText(code); ShareCodeStatus.Text = "Copied to clipboard"; }
        catch { /* clipboard can fail in odd contexts */ }
    }

    private void OnImportShareCode(object sender, RoutedEventArgs e)
    {
        _shareCodeIsImportMode = true;
        ShareCodeLabel.Text = "Paste a share code to apply";
        // v1.18 fix: do NOT auto-prefill from clipboard. The previous behavior
        // pulled the just-exported code into the import box, which looked like
        // a bug to the user (they expected a blank import area). The user can
        // still Ctrl+V manually -- the box is focused below.
        ShareCodeBox.Text   = "";
        ShareCodeBox.IsReadOnly = false;
        ShareCodeCopyBtn.Visibility  = Visibility.Collapsed;
        ShareCodeApplyBtn.Visibility = Visibility.Visible;
        ShareCodeStatus.Text = "";
        ShareCodePanel.Visibility = Visibility.Visible;
        ShareCodeBox.Focus();
    }

    private void OnShareCodeCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(ShareCodeBox.Text);
            ShareCodeStatus.Text = "Copied";
        }
        catch (Exception ex) { ShareCodeStatus.Text = "Copy failed: " + ex.Message; }
    }

    private void OnShareCodeApply(object sender, RoutedEventArgs e)
    {
        if (!_shareCodeIsImportMode) return;
        var code = ShareCodeBox.Text.Trim();
        var s = App.Current.Settings;
        PushUndoSnapshot();
        _suppressSnapshot = true;
        try
        {
            if (!AppearanceShareCodec.TryImport(code, s, out var err))
            {
                ShareCodeStatus.Text = err;
                return;
            }
            // Apply to the running app: theme, then skin, then fonts.
            ThemeApplier.Apply(s, Application.Current.Resources);
            // Skin may not exist on this machine -- soft fall back to current
            var skinIdx = _skins.FindIndex(sk => sk.Name == s.ActiveSkin);
            if (skinIdx >= 0)
            {
                _skinIndex = skinIdx;
                UpdateSkinCycler();
                ApplyCurrentSkin();
            }
            else
            {
                ShareCodeStatus.Text = $"Skin '{s.ActiveSkin}' not installed -- other settings applied";
            }
            SkinManager.ApplyFontOverrides(s, Application.Current.Resources);
            SettingsService.Save(s);

            // Reflect into the Settings UI controls
            _loading = true;
            BackgroundColorBox.Text = s.BackgroundColor; TileColorBox.Text = s.TileColor;
            AccentColorBox.Text = s.AccentColor; TextColorBox.Text = s.TextColor;
            MutedTextColorBox.Text = s.MutedTextColor;
            DarkModeBtn.IsChecked = s.IsDarkMode; LightModeBtn.IsChecked = !s.IsDarkMode;
            PrimaryFontCombo.SelectedItem   = string.IsNullOrEmpty(s.PrimaryFont)   ? FontCatalog.DefaultEntry : s.PrimaryFont;
            SecondaryFontCombo.SelectedItem = string.IsNullOrEmpty(s.SecondaryFont) ? FontCatalog.DefaultEntry : s.SecondaryFont;
            IndicatorFontCombo.SelectedItem = string.IsNullOrEmpty(s.IndicatorFont) ? FontCatalog.DefaultEntry : s.IndicatorFont;
            SyncFontsCheck.IsChecked        = s.SyncFonts;
            RandomizeFontsCheck.IsChecked   = s.RandomizeFontsOnDice;
            PrimaryFontSlider.Value         = s.PrimaryFontSizeOffset;
            SecondaryFontSlider.Value       = s.SecondaryFontSizeOffset;
            IndicatorFontSlider.Value       = s.IndicatorFontSizeOffset;
            WidthSlider.Value               = s.TileWidth;
            HeightSlider.Value              = s.TileHeight;
            ScaleSlider.Value               = s.UiScale;
            OpacitySlider.Value             = s.Opacity;
            _loading = false;
            UpdateSwatches();
            _themeIndex = FindCurrentThemeIndex();
            UpdateThemeCycler();
            RebuildUserPresetSlots();
            // v1.20: on successful import (no prior error in ShareCodeStatus),
            // close the share code panel and show a clean matte popup notice.
            if (string.IsNullOrEmpty(ShareCodeStatus.Text))
            {
                ShareCodePanel.Visibility = Visibility.Collapsed;
                ShareCodeBox.Text = "";    // clear so a fresh open is blank
                ShareCodeStatus.Text = "";
                ImportNoticePopup.IsOpen = true;
            }
        }
        finally { _suppressSnapshot = false; }
    }

    private void OnImportNoticeClose(object sender, RoutedEventArgs e)
    {
        ImportNoticePopup.IsOpen = false;
    }

    private void OnShareCodeClose(object sender, RoutedEventArgs e)
    {
        ShareCodePanel.Visibility = Visibility.Collapsed;
        ShareCodeStatus.Text = "";
    }
    // --------------------------------------------------------------------

    private void OnSkinFromFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title  = "Select skin.xaml or skin folder",
            Filter = "XAML Skin|skin.xaml|All Files|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;

        var installed = SkinManager.InstallSkinFromPath(dlg.FileName);
        if (installed == null)
        {
            FluidMessageBox.Show("Couldn't install skin — make sure it contains a valid skin.xaml.", "Skin error", owner: this);
            return;
        }

        // Reload and select new skin
        _skins = SkinManager.GetAllSkins();
        var newName = Path.GetFileName(installed);
        var idx = _skins.FindIndex(s => s.FolderPath == installed || s.Name == newName);
        if (idx >= 0) _skinIndex = idx;
        UpdateSkinCycler();
        ApplyCurrentSkin();
    }

    private void ApplyCurrentSkin()
    {
        if (_skins.Count == 0) return;
        PushUndoSnapshot();
        var skin = _skins[_skinIndex];
        var key  = skin.IsBuiltIn ? skin.Name : skin.FolderPath;
        SkinManager.ApplySkin(key, Application.Current.Resources);
        App.Current.Settings.ActiveSkin = skin.Name;
        SettingsService.Save(App.Current.Settings);
        // v1.25.15: new skin means new SkinFontFamily; refresh the
        // "(skin default — XYZ)" labels in the font combos so they match.
        RefreshFontComboPlaceholders();
        Fmt.InvalidateAllDeferred();
    }


    private int _themeIndex = 0;
    private int _pendingSaveSlot = -1;
    private int _pendingClearSlot = -1;
    private const int MaxUserPresets = 5;

    private List<ThemeApplier.ThemePreset> AllPresets()
    {
        var list = new List<ThemeApplier.ThemePreset>(ThemeApplier.Presets);
        // v1.20: Colors cycler shows ONLY built-ins + CustomColors. UserPresets
        // (the 5 saved slots) capture full combos -- colors + skin + fonts --
        // and live exclusively in their dedicated Saved presets row.
        // v1.20: the (i) badge for imported colors is now ONLY shown by the
        // browse popup's ⓘ icon. Earlier versions also appended ", i" to the
        // cycler label, which caused a duplicate badge visible in the popup.
        foreach (var c in App.Current.Settings.CustomColors)
        {
            if (string.IsNullOrEmpty(c.BackgroundColor)) continue;
            var label = $"{c.Name} (custom)";
            list.Add(new ThemeApplier.ThemePreset(
                label,
                c.BackgroundColor, c.TileColor, c.AccentColor,
                c.TextColor, c.MutedTextColor));
        }
        // v1.20.5: also include all 89 built-in themes as color entries
        // suffixed "(theme)". Without this, picking a theme via the Preset
        // Themes cycler causes the Colors cycler to flip to "Custom" because
        // the theme's color palette isn't otherwise findable in this list.
        // The browse popup picks these up and groups them by Franchise.
        foreach (var t in ThemeApplier.BuiltInThemes)
        {
            // v1.25.37: skip the Default theme (empty franchise) -- its
            // palette is already available as "Dark (default)" / "Light
            // (default)" in the base Presets. Showing it again as "Default
            // (theme)" creates an unnamed franchise group in the browse.
            if (string.IsNullOrEmpty(t.Franchise)) continue;
            list.Add(new ThemeApplier.ThemePreset(
                $"{t.Name} (theme)",
                t.Bg, t.Tile, t.Accent, t.Text, t.Muted));
        }
        return list;
    }

    private void LoadThemePresets()
    {
        _themeIndex = FindCurrentThemeIndex();
        UpdateThemeCycler();
        RebuildUserPresetSlots();
        RefreshBrowseList();
    }

    private int FindCurrentThemeIndex()
    {
        var all = AllPresets();
        var matched = ThemeApplier.MatchPreset(App.Current.Settings, all);
        for (int i = 0; i < all.Count; i++)
            if (all[i].Name == matched.Name) return i;
        return 0;
    }

    private void UpdateThemeCycler()
    {
        var all = AllPresets();
        if (_themeIndex < 0 || _themeIndex >= all.Count) _themeIndex = 0;
        var preset = all[_themeIndex];
        // v1.20.5: for theme-tagged entries strip the "(theme)" suffix AND
        // the franchise prefix so the Colors cycler reads cleanly. The data
        // still carries the full name; this is display-only.
        var displayName = preset.Name;
        if (displayName.EndsWith(" (theme)"))
        {
            displayName = displayName.Substring(0, displayName.Length - " (theme)".Length);
            // Strip franchise prefix by matching against any of the known prefixes
            foreach (var t in ThemeApplier.BuiltInThemes)
            {
                if (preset.Name == $"{t.Name} (theme)")
                {
                    if (!string.IsNullOrEmpty(t.Franchise) && displayName.StartsWith(t.Franchise + " "))
                        displayName = displayName.Substring(t.Franchise.Length + 1);
                    break;
                }
            }
        }
        ThemeNameLabel.Text = displayName;
        try
        {
            var hex = string.IsNullOrEmpty(preset.Accent) ? App.Current.Settings.AccentColor : preset.Accent;
            var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
            ThemeAccentDot.Fill = new System.Windows.Media.SolidColorBrush(color);
        }
        catch { ThemeAccentDot.Fill = (System.Windows.Media.Brush)Application.Current.Resources["AccentBrush"]; }
    }

    private void OnThemePrev(object sender, RoutedEventArgs e)
    {
        var all = AllPresets();
        _themeIndex = (_themeIndex - 1 + all.Count) % all.Count;
        if (_themeIndex == 0) _themeIndex = all.Count - 1; // skip Custom
        UpdateThemeCycler();
        ApplyCurrentCyclerTheme();
    }

    private void OnThemeNext(object sender, RoutedEventArgs e)
    {
        var all = AllPresets();
        _themeIndex = (_themeIndex + 1) % all.Count;
        if (_themeIndex == 0) _themeIndex = 1; // skip Custom
        UpdateThemeCycler();
        ApplyCurrentCyclerTheme();
    }

    private void ApplyCurrentCyclerTheme()
    {
        var all = AllPresets();
        if (_themeIndex < 0 || _themeIndex >= all.Count) return;
        var preset = all[_themeIndex];
        if (string.IsNullOrEmpty(preset.Bg)) return;
        PushUndoSnapshot();
        ThemeApplier.ApplyPreset(App.Current.Settings, preset);
        ClearActiveTheme();  // v1.21: Colors cycler pick = manual color change
        SettingsService.Save(App.Current.Settings);
        _loading = true;
        var s = App.Current.Settings;
        BackgroundColorBox.Text = s.BackgroundColor; TileColorBox.Text = s.TileColor;
        AccentColorBox.Text = s.AccentColor; TextColorBox.Text = s.TextColor;
        MutedTextColorBox.Text = s.MutedTextColor;
        DarkModeBtn.IsChecked = s.IsDarkMode; LightModeBtn.IsChecked = !s.IsDarkMode;
        _loading = false;
        UpdateSwatches();
        ApplyColorsLive();
        RebuildUserPresetSlots();
    }

    private void SyncPresetCombo()
    {
        _themeIndex = FindCurrentThemeIndex();
        UpdateThemeCycler();
        RebuildUserPresetSlots();
        RefreshBrowseList();
    }

    // ── Browse panel ──────────────────────────────────────────────────────
    // Wrapper so each browse row gets its own resolved accent brush.
    // v1.19: also carries IsCustom (true for CustomColors entries; controls
    // whether the X delete button is visible) and IsImported (true for
    // colors brought in from a share code; controls the (i) badge + tooltip).
    private class BrowseItem
    {
        public string Name { get; set; } = "";  // full name with suffix (for click lookup)
        public string DisplayName { get; set; } = "";  // v1.20.5: stripped name for the row label
        public string Group { get; set; } = "";  // v1.20.5: grouping bucket (franchise or "Built-in")
        // v1.25.37: group ordering -- 0 = Built-in (flat, first),
        // 1 = Custom Colors (flat, second), 2 = franchise folders (A-Z).
        public int GroupSortKey { get; set; } = 0;
        public SolidColorBrush AccentBrush { get; set; } = new SolidColorBrush(Colors.Gray);
        public bool   IsCustom   { get; set; } = false;
        public bool   IsImported { get; set; } = false;
        public string InfoText   { get; set; } = "";
        // Underlying CustomColor name (without the "(custom)" or "(custom, i)" suffix)
        // so the delete handler can find and remove it from settings.CustomColors.
        public string CustomColorBaseName { get; set; } = "";
        // For UI binding: Visibility for the X button.
        public Visibility DeleteVisibility => IsCustom ? Visibility.Visible : Visibility.Collapsed;
        public Visibility InfoVisibility   => IsImported ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshBrowseList()
    {
        var customByName = App.Current.Settings.CustomColors
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First());

        // v1.20.5: build a lookup of theme names -> Franchise so themed colors
        // can be grouped by franchise in the popup.
        var themeFranchiseByName = ThemeApplier.BuiltInThemes
            .ToDictionary(t => $"{t.Name} (theme)", t => (Franchise: t.Franchise, BareName: t.Name));

        var items = AllPresets()
            .Where(p => !string.IsNullOrEmpty(p.Bg))
            // v1.25.6 filtered the 89 "(theme)" entries out of this popup
            // entirely. v1.25.37 brings them BACK, but tucked into per-
            // franchise folder expanders below the flat defaults (per user:
            // "a list of the regular defaults, then folder drop downs with
            // the game names"). They were already in AllPresets() for the
            // cycler-naming fix (v1.20.5), so no data change -- just no
            // longer hidden from the browse.
            .Select(p =>
            {
                SolidColorBrush brush;
                try
                {
                    var col = (Color)ColorConverter.ConvertFromString(p.Accent);
                    brush = new SolidColorBrush(col);
                    brush.Freeze();
                }
                catch { brush = new SolidColorBrush(Colors.Gray); brush.Freeze(); }

                // v1.20: detect CustomColors entries by the "(custom)" suffix.
                bool isCustom = p.Name.EndsWith(" (custom)");
                string baseName = "";
                bool isImported = false;
                string info = "";
                if (isCustom)
                {
                    baseName = p.Name.Replace(" (custom)", "");
                    if (customByName.TryGetValue(baseName, out var cc))
                    {
                        isImported = cc.IsImported;
                        info = isImported
                            ? $"Imported from a share code{(string.IsNullOrEmpty(cc.ImportedFrom) ? "" : $" ({cc.ImportedFrom})")}"
                            : "User-created color";
                    }
                }

                // v1.20.5: group + display name resolution
                // v1.25.37: GroupSortKey orders the sections.
                string group = "Built-in";
                int groupSortKey = 0;
                string displayName = p.Name;
                if (isCustom)
                {
                    group = "Custom Colors";
                    groupSortKey = 1;
                    displayName = baseName;
                }
                else if (themeFranchiseByName.TryGetValue(p.Name, out var meta))
                {
                    group = meta.Franchise;
                    groupSortKey = 2;
                    // Strip the franchise prefix from displayName for cleanness inside the group
                    displayName = meta.BareName;
                    if (!string.IsNullOrEmpty(meta.Franchise) && displayName.StartsWith(meta.Franchise + " "))
                        displayName = displayName.Substring(meta.Franchise.Length + 1);
                }

                return new BrowseItem
                {
                    Name        = p.Name,
                    DisplayName = displayName,
                    Group       = group,
                    GroupSortKey = groupSortKey,
                    AccentBrush = brush,
                    IsCustom    = isCustom,
                    IsImported  = isImported,
                    InfoText    = info,
                    CustomColorBaseName = baseName,
                };
            })
            .ToList();

        // v1.25.37: grouping re-enabled (removed in v1.25.6 when this list
        // was flat). Sort: Built-in first, Custom Colors second, franchise
        // folders A-Z; within-group order is preserved (OrderBy is stable).
        // ListCollectionView groups in encounter order over the pre-sorted
        // list, and the XAML GroupStyle renders Built-in / Custom Colors
        // flat (no expander) and franchises as collapsed folders.
        items = items
            .OrderBy(i => i.GroupSortKey)
            .ThenBy(i => i.Group, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var view = new System.Windows.Data.ListCollectionView(items);
        view.GroupDescriptions.Add(new System.Windows.Data.PropertyGroupDescription("Group"));
        ThemeBrowseList.ItemsSource = view;
    }

    private void OnThemeBrowse(object sender, RoutedEventArgs e)
    {
        RefreshBrowseList();
        ThemeBrowsePopup.IsOpen = !ThemeBrowsePopup.IsOpen;
    }

    private void OnBrowseItemClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is string name)
        {
            var all = AllPresets();
            var idx = all.FindIndex(p => p.Name == name);
            if (idx >= 0) { _themeIndex = idx; UpdateThemeCycler(); ApplyCurrentCyclerTheme(); }
        }
        ThemeBrowsePopup.IsOpen = false;
    }

    // ── User preset slots ─────────────────────────────────────────────────
    private void RebuildUserPresetSlots()
    {
        UserPresetSlots.Children.Clear();
        var s = App.Current.Settings;
        while (s.UserPresets.Count < MaxUserPresets) s.UserPresets.Add(new UserColorPreset());

        for (int i = 0; i < MaxUserPresets; i++)
        {
            int idx = i;
            var preset = s.UserPresets[i];

            // v1.14 styling:
            //   Empty slot  -> number "1"-"5", thin muted outline, dim text
            //   Saved slot  -> number "1"-"5", accent-colored border, slight accent tint background, bright text
            //   On left-click of empty slot, the button swaps to a 💾 icon and the save dialog opens.
            //   On left-click of saved slot, the preset is loaded.
            //   On right-click of saved slot, the existing clear-confirm popup appears (unchanged).
            var btn = new Button
            {
                Width = 24, Height = 24, Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 3, 0),
                Cursor = System.Windows.Input.Cursors.Hand,
                FontSize = 11,
                Content = (i + 1).ToString(),
                ToolTip = preset.IsEmpty
                    ? $"Slot {i + 1} -- click to save current theme + skin + fonts here"
                    : $"{preset.Name} -- click to apply, right-click to clear"
            };

            // Visual styling via inline overrides (no XAML style needed)
            var accent = (System.Windows.Media.Brush)Application.Current.Resources["AccentBrush"];
            var muted  = (System.Windows.Media.Brush)Application.Current.Resources["MutedTextBrush"];
            var text   = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"];

            if (preset.IsEmpty)
            {
                btn.Background  = System.Windows.Media.Brushes.Transparent;
                btn.BorderBrush = muted;
                btn.BorderThickness = new Thickness(0.5);
                btn.Foreground  = muted;
                btn.Opacity     = 0.6;
                btn.FontWeight  = FontWeights.Normal;
            }
            else
            {
                // Try to derive a per-preset accent for the border so each saved
                // slot tints toward its stored accent color. Fall back to AccentBrush.
                System.Windows.Media.Brush borderBrush = accent;
                System.Windows.Media.Color tintColor;
                try
                {
                    var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(preset.AccentColor);
                    borderBrush = new System.Windows.Media.SolidColorBrush(col);
                    tintColor   = System.Windows.Media.Color.FromArgb(0x2E, col.R, col.G, col.B); // ~18% opacity tint
                }
                catch
                {
                    var col = ((System.Windows.Media.SolidColorBrush)accent).Color;
                    tintColor = System.Windows.Media.Color.FromArgb(0x2E, col.R, col.G, col.B);
                }
                btn.Background     = new System.Windows.Media.SolidColorBrush(tintColor);
                btn.BorderBrush    = borderBrush;
                btn.BorderThickness = new Thickness(1);
                btn.Foreground     = text;
                btn.FontWeight     = FontWeights.SemiBold;
            }

            btn.Click += (_, _) =>
            {
                if (preset.IsEmpty)
                {
                    // Visual swap: turn the button into a 💾 icon, THEN open the dialog.
                    // If the user cancels the save dialog, RebuildUserPresetSlots will
                    // re-render and restore the number.
                    btn.Content = "💾";
                    btn.FontSize = 13;
                    ShowSavePresetDialog(idx);
                }
                else
                {
                    LoadUserPreset(idx);
                }
            };

            btn.MouseRightButtonUp += (_, _) =>
            {
                if (preset.IsEmpty) return;
                _pendingClearSlot = idx;
                ClearConfirmLabel.Text = $"Clear \"{preset.Name}\"?";
                ClearConfirmPopup.PlacementTarget = btn;
                ClearConfirmPopup.IsOpen = true;
            };

            UserPresetSlots.Children.Add(btn);
        }
    }

    private void LoadUserPreset(int slot)
    {
        var u = App.Current.Settings.UserPresets[slot];
        if (u.IsEmpty) return;
        PushUndoSnapshot();
        _suppressSnapshot = true;
        try
        {
            var tp = ThemeApplier.UserPresetToThemePreset(u);
            ThemeApplier.ApplyPreset(App.Current.Settings, tp);

            // v1.13: also apply saved skin + fonts when the preset has them.
            // Empty strings mean "don't override" (backward-compat with pre-v1.13 presets).
            var s = App.Current.Settings;
            if (!string.IsNullOrEmpty(u.ActiveSkin))
        {
            s.ActiveSkin = u.ActiveSkin;
            // Update the cycler index and apply via SkinManager
            var skinIdx = _skins.FindIndex(sk => sk.Name == u.ActiveSkin);
            if (skinIdx >= 0)
            {
                _skinIndex = skinIdx;
                UpdateSkinCycler();
                ApplyCurrentSkin();
            }
        }
        if (!string.IsNullOrEmpty(u.PrimaryFont))   s.PrimaryFont   = u.PrimaryFont;
        if (!string.IsNullOrEmpty(u.SecondaryFont)) s.SecondaryFont = u.SecondaryFont;
        if (!string.IsNullOrEmpty(u.IndicatorFont)) s.IndicatorFont = u.IndicatorFont;
        ClearActiveTheme();  // v1.21: a saved combo is not a Preset Theme
        SettingsService.Save(s);

        // Sync UI controls to new state
        _loading = true;
        BackgroundColorBox.Text = s.BackgroundColor; TileColorBox.Text = s.TileColor;
        AccentColorBox.Text = s.AccentColor; TextColorBox.Text = s.TextColor;
        MutedTextColorBox.Text = s.MutedTextColor;
        DarkModeBtn.IsChecked = s.IsDarkMode; LightModeBtn.IsChecked = !s.IsDarkMode;
        if (!string.IsNullOrEmpty(u.PrimaryFont))   PrimaryFontCombo.SelectedItem   = u.PrimaryFont;
        if (!string.IsNullOrEmpty(u.SecondaryFont)) SecondaryFontCombo.SelectedItem = u.SecondaryFont;
        if (!string.IsNullOrEmpty(u.IndicatorFont)) IndicatorFontCombo.SelectedItem = u.IndicatorFont;
        _loading = false;
        UpdateSwatches(); ApplyColorsLive(); SyncPresetCombo();
        SkinManager.ApplyFontOverrides(s, Application.Current.Resources);
        }
        finally { _suppressSnapshot = false; }
    }

    private void ClearUserPreset(int slot)
    {
        App.Current.Settings.UserPresets[slot] = new UserColorPreset();
        SettingsService.Save(App.Current.Settings);
        SyncPresetCombo();
        RefreshBrowseList();
    }

    private void ShowSavePresetDialog(int slot)
    {
        _pendingSaveSlot = slot;
        SavePresetNameBox.Text = $"My preset {slot + 1}";
        SavePresetPanel.Visibility = Visibility.Visible;
        SavePresetNameBox.Focus();
        SavePresetNameBox.SelectAll();
    }

    private void OnConfirmSavePreset(object sender, RoutedEventArgs e)
    {
        var s = App.Current.Settings;
        var name = SavePresetNameBox.Text.Trim();

        // v1.19: branch on whether this was opened by the + button (CustomColors)
        // or by an empty preset slot (UserPresets / full combo).
        if (_pendingCustomColorSave)
        {
            if (string.IsNullOrEmpty(name)) name = "Custom Color";
            // De-dupe by name -- replace any existing CustomColor with the
            // same name so users can update an entry without ending up with
            // duplicates in the cycler.
            s.CustomColors.RemoveAll(c => c.Name == name);
            s.CustomColors.Add(new CustomColor
            {
                Name            = name,
                BackgroundColor = s.BackgroundColor,
                TileColor       = s.TileColor,
                AccentColor     = s.AccentColor,
                TextColor       = s.TextColor,
                MutedTextColor  = s.MutedTextColor,
                IsImported      = false,
                ImportedFrom    = "",
            });
            SettingsService.Save(s);
            SavePresetPanel.Visibility = Visibility.Collapsed;
            _pendingCustomColorSave = false;
            RefreshBrowseList();
            // Move cycler to the newly added custom color
            var all = AllPresets();
            var idx = all.FindIndex(p => p.Name == $"{name} (custom)");
            if (idx >= 0) { _themeIndex = idx; UpdateThemeCycler(); }
            else SyncPresetCombo();
            return;
        }

        if (_pendingSaveSlot < 0) return;
        if (string.IsNullOrEmpty(name)) name = $"My preset {_pendingSaveSlot + 1}";
        // v1.13: presets now capture full appearance state -- colors + skin + fonts.
        // Empty font fields mean "no override" and are preserved as such.
        s.UserPresets[_pendingSaveSlot] = new UserColorPreset
        {
            Name            = name,
            BackgroundColor = s.BackgroundColor,
            TileColor       = s.TileColor,
            AccentColor     = s.AccentColor,
            TextColor       = s.TextColor,
            MutedTextColor  = s.MutedTextColor,
            ActiveSkin      = s.ActiveSkin   ?? "",
            PrimaryFont     = s.PrimaryFont  ?? "",
            SecondaryFont   = s.SecondaryFont ?? "",
            IndicatorFont   = s.IndicatorFont ?? ""
        };
        SettingsService.Save(s);
        SavePresetPanel.Visibility = Visibility.Collapsed;
        _pendingSaveSlot = -1;
        // v1.20: UserPresets no longer appear in the Colors cycler (only built-ins
        // and CustomColors do). So saving to a slot doesn't move the cycler -- it
        // just populates the slot. RebuildUserPresetSlots refreshes the 1-5 row
        // visuals so the new entry shows up there.
        RefreshBrowseList();
        RebuildUserPresetSlots();
    }

    private void OnCancelSavePreset(object sender, RoutedEventArgs e)
    {
        SavePresetPanel.Visibility = Visibility.Collapsed;
        _pendingSaveSlot = -1;
        _pendingCustomColorSave = false;  // v1.19
    }

    private void OnClearConfirmYes(object sender, RoutedEventArgs e)
    {
        ClearConfirmPopup.IsOpen = false;
        // v1.19: prioritize custom-color delete -- the popup was opened by the
        // X button on a CustomColors entry, not by the right-click-clear flow
        // on a preset slot.
        if (!string.IsNullOrEmpty(_pendingDeleteCustomColor))
        {
            var s = App.Current.Settings;
            var name = _pendingDeleteCustomColor;
            s.CustomColors.RemoveAll(c => c.Name == name);
            SettingsService.Save(s);
            _pendingDeleteCustomColor = null;
            RefreshBrowseList();
            // If the cycler was pointing at the deleted color, fall back to the first built-in
            var all = AllPresets();
            if (_themeIndex >= all.Count) _themeIndex = System.Math.Max(0, all.Count - 1);
            UpdateThemeCycler();
            return;
        }
        if (_pendingClearSlot >= 0) ClearUserPreset(_pendingClearSlot);
        _pendingClearSlot = -1;
    }

    private void OnClearConfirmCancel(object sender, RoutedEventArgs e)
    {
        ClearConfirmPopup.IsOpen = false;
        _pendingClearSlot = -1;
        _pendingDeleteCustomColor = null;  // v1.19
    }

    private void OnSavePresetKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter) OnConfirmSavePreset(sender, e);
        if (e.Key == System.Windows.Input.Key.Escape) OnCancelSavePreset(sender, e);
    }



    // ------------------------------------------------------------------
    // Load / save settings
    // ------------------------------------------------------------------


    private void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string key) return;
        _activeSwatchKey = key;
        var s = App.Current.Settings;
        var hex = key switch
        {
            "BackgroundColor" => s.BackgroundColor,
            "TileColor"       => s.TileColor,
            "AccentColor"     => s.AccentColor,
            "TextColor"       => s.TextColor,
            "MutedTextColor"  => s.MutedTextColor,
            _                 => "#FFFFFFFF"
        };
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            PickerControl.SetColor(c);
        }
        catch { PickerControl.SetColor(Colors.White); }

        ColorPopup.IsOpen = true;
        UpdateSwatchHighlight(); // v1.25.37: highlight the active swatch
    }

    private void OnPickerApplied(Color c)
    {
        ColorPopup.IsOpen = false;
        if (_activeSwatchKey == null) return;
        var hex = $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        ApplyColorValue(_activeSwatchKey, hex);
        _activeSwatchKey = null;
    }

    private void OnColorBoxChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = App.Current.Settings;
        if (IsRemote) {
            var p = _activeDevice!.Popout;
            p.BackgroundColor = BackgroundColorBox.Text.Trim(); p.TileColor = TileColorBox.Text.Trim();
            p.AccentColor = AccentColorBox.Text.Trim(); p.TextColor = TextColorBox.Text.Trim();
            p.MutedTextColor = MutedTextColorBox.Text.Trim();
        } else {
            s.BackgroundColor = BackgroundColorBox.Text.Trim(); s.TileColor = TileColorBox.Text.Trim();
            s.AccentColor = AccentColorBox.Text.Trim(); s.TextColor = TextColorBox.Text.Trim();
            s.MutedTextColor = MutedTextColorBox.Text.Trim();
            ClearActiveTheme();  // v1.21
        }
        SettingsService.Save(s); UpdateSwatches(); ApplyColorsLive(); SyncPresetCombo();
    }

    private void ApplyColorValue(string key, string hex)
    {
        var s = App.Current.Settings;
        if (IsRemote) {
            var p = _activeDevice!.Popout;
            switch (key) {
                case "BackgroundColor": p.BackgroundColor = hex; BackgroundColorBox.Text = hex; break;
                case "TileColor":       p.TileColor       = hex; TileColorBox.Text       = hex; break;
                case "AccentColor":     p.AccentColor     = hex; AccentColorBox.Text     = hex; break;
                case "TextColor":       p.TextColor       = hex; TextColorBox.Text       = hex; break;
                case "MutedTextColor":  p.MutedTextColor  = hex; MutedTextColorBox.Text  = hex; break;
            }
        } else {
            switch (key) {
                case "BackgroundColor": s.BackgroundColor = hex; BackgroundColorBox.Text = hex; break;
                case "TileColor":       s.TileColor       = hex; TileColorBox.Text       = hex; break;
                case "AccentColor":     s.AccentColor     = hex; AccentColorBox.Text     = hex; break;
                case "TextColor":       s.TextColor       = hex; TextColorBox.Text       = hex; break;
                case "MutedTextColor":  s.MutedTextColor  = hex; MutedTextColorBox.Text  = hex; break;
            }
            ClearActiveTheme();  // v1.21
        }
        SettingsService.Save(s); UpdateSwatches(); ApplyColorsLive(); SyncPresetCombo();
    }

    private void ApplyColorsLive()
    {
        if (IsRemote)
        {
            RefreshOpenPopout();
        }
        else
        {
            Dispatcher.Invoke(() =>
            {
                ThemeApplier.Apply(App.Current.Settings, Application.Current.Resources);
                // Force all bound elements to re-evaluate DynamicResource bindings now
                foreach (Window w in Application.Current.Windows)
                    w.InvalidateVisual();
            }, System.Windows.Threading.DispatcherPriority.Render);
        }
    }

    // ------------------------------------------------------------------
    // Reset All to Defaults
    // ------------------------------------------------------------------
    //
    // v1.16 overhaul: instead of enumerating every property by hand (which has
    // missed v1.13 fonts, v1.16 ShowDateTime, ActiveSkin, etc. across past
    // releases), we now copy from a fresh AppSettings() over EVERY public
    // settable property via reflection. A small explicit skip-list preserves
    // the few things the user wants to keep across reset:
    //   - WindowLeft / WindowTop : per user request, the widget stays put
    //
    // UserPresets ARE cleared (per user: "true factory wipe").
    // RemoteDevices ARE cleared.
    // All Game Mode, font, skin, tile, color, layout settings: reset.
    //
    // Future-proofing: any property added to AppSettings is automatically
    // reset. If you add a property that should NOT be reset, add it to
    // PreservedPropertyNames below.

    private static readonly HashSet<string> PreservedPropertyNames = new(System.StringComparer.Ordinal)
    {
        "WindowLeft", "WindowTop",  // user request: keep widget where it is
        "SchemaVersion",            // v1.21: default is now the oldest version (1); copying it would re-trigger migrations
    };

    private async void OnResetAll(object sender, RoutedEventArgs e)
    {
        var result = FluidMessageBox.Show(
            "This will reset everything to factory defaults.\n\n" +
            "Only your widget position is preserved — all other settings, " +
            "saved presets, remote devices, and the handshake key will be cleared.\n\n" +
            "The app will restart to apply changes.\n\n" +
            "Continue?",
            "Reset All to Defaults",
            MessageBoxButton.YesNo, this);
        if (result != MessageBoxResult.Yes) return;

        // Remove all remote device connections (cleanup any active sockets/timers)
        foreach (var dev in App.Current.Settings.RemoteDevices.ToList())
            App.Current.DeviceManager.Remove(dev.Id);

        // Reflection-based reset: walk every public settable property on
        // AppSettings, copy the value from a fresh instance unless it's in
        // the preserve list. This way new properties added in future versions
        // get reset by default without anyone having to remember.
        var s     = App.Current.Settings;
        var fresh = new AppSettings();
        var type  = typeof(AppSettings);
        foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            if (PreservedPropertyNames.Contains(prop.Name)) continue;

            // Skip indexers (have parameters)
            if (prop.GetIndexParameters().Length > 0) continue;

            try
            {
                var freshValue = prop.GetValue(fresh);
                prop.SetValue(s, freshValue);
            }
            catch
            {
                // Property is read-only at runtime (init-only or complex setter).
                // The list-type properties below handle these explicitly.
            }
        }

        // List-type properties: replace contents in-place so any bindings
        // observing them update. Reflection-copying the reference would orphan
        // anything bound to the existing list instance.
        s.RemoteDevices.Clear();
        s.UserPresets.Clear();  // v1.16: clear saved presets per user request
        s.Warnings.Clear();
        foreach (var w in new AppSettings().Warnings) s.Warnings.Add(w);

        SettingsService.Save(s);

        // v1.23.1: RunAtStartup just reset to false in JSON, but the HKCU Run
        // key / startup shortcut are machine state -- clear them too so reset
        // means what it says.
        try { Services.StartupManager.SetEnabled(false); } catch { }

        // Regenerate the Handshake Key
        RegenerateStatus.Text = "Regenerating key...";
        try
        {
            var newKey = await CmdClient.RegenerateKeyAsync();
            HandshakeKeyBox.Text  = newKey;
            RegenerateStatus.Text = "Key regenerated";
        }
        catch { RegenerateStatus.Text = "Key regeneration failed"; }

        // v1.20: Reset to Defaults now relaunches the app to apply changes.
        // Rationale: WPF DynamicResource refreshes can lag in-process when
        // multiple skin swaps race during dice spamming, causing tile visuals
        // to keep stale skin properties (corner radius, glow, etc) even though
        // settings.json + the cycler labels all show "Default". A fresh
        // process launch is the simplest, most reliable way to guarantee the
        // widget renders cleanly against the reset state -- same code path as
        // a normal app start which always works correctly.
        //
        // v1.21: corrected comment -- no --open-settings flag exists; the new
        // process starts with the widget only and the user reopens Settings.
        // Window position is preserved via WindowLeft/WindowTop in
        // PreservedPropertyNames, so the widget reappears in the same spot.
        try
        {
            var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(exePath))
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = exePath,
                    Arguments       = "",
                    UseShellExecute = false,
                };
                System.Diagnostics.Process.Start(psi);
            }
        }
        catch
        {
            // If we can't relaunch, fall back to the previous in-process apply
            // path -- not perfect, but better than leaving the user with no app.
            var __allThemes = AllPresets();
            var __dt = __allThemes.FindIndex(p => p.Name == "Dark (default)");
            if (__dt < 0) __dt = 0;
            _themeIndex = __dt;
            UpdateThemeCycler();
            ApplyCurrentCyclerTheme();
            var __ds = _skins.FindIndex(sk => sk.Name == "Default");
            if (__ds < 0) __ds = 0;
            _skinIndex = __ds;
            UpdateSkinCycler();
            ApplyCurrentSkin();
            SkinManager.ApplyFontOverrides(s, Application.Current.Resources);
            _undoStack.Clear();
            UpdateUndoButtonVisibility();
            _loading = true;
            LoadFromSettings();
            _loading = false;
            RefreshDeviceList();
            UpdateSwatches();
            (Owner as MainWindow)?.SwitchToLocal();
            (Owner as MainWindow)?.RefreshDeviceSwitcher();
            (Owner as MainWindow)?.RebuildVisibleTiles();
            return;
        }

        // Shutdown current instance. The single-instance mutex is released on
        // process exit. App.xaml.cs OnStartup retries the mutex acquisition
        // for up to 2 seconds, which covers the small window where both
        // processes briefly coexist.
        System.Windows.Application.Current.Shutdown();
    }
    private void OnTileToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = App.Current.Settings;
        if (IsRemote)
        {
            var p = _activeDevice!.Popout;
            p.ShowCpu = CpuCheck.IsChecked == true; p.ShowGpu = GpuCheck.IsChecked == true;
            p.ShowRam = RamCheck.IsChecked == true; p.ShowNetwork = NetworkCheck.IsChecked == true;
            p.ShowStorage = StorageCheck.IsChecked == true;
            SettingsService.Save(s);
            // Refresh popout if open
            RefreshOpenPopout();
        }
        else
        {
            s.ShowCpu = CpuCheck.IsChecked == true; s.ShowGpu = GpuCheck.IsChecked == true;
            s.ShowRam = RamCheck.IsChecked == true; s.ShowNetwork = NetworkCheck.IsChecked == true;
            s.ShowStorage = StorageCheck.IsChecked == true;
            // v1.16: DateTime tile (local only)
            s.ShowDateTime = DateTimeCheck.IsChecked == true;
            SettingsService.Save(s);
            (Owner as MainWindow)?.RebuildVisibleTiles();
        }
    }

    // ------------------------------------------------------------------
    // v1.18: Tile drag-reorder
    // ------------------------------------------------------------------
    //
    // Each tile CheckBox in TileTogglesGrid is draggable. Drop on another
    // CheckBox swaps their positions in AppSettings.TileOrder. The grid stays
    // 3x2; we just shuffle Grid.Row / Grid.Column attached values. Local
    // widget AND remote popouts both pick up the new order on the next
    // RebuildVisibleTiles call.

    // CheckBox-by-TileKind map (set up once in InitTileDragDrop)
    private readonly System.Collections.Generic.Dictionary<string, System.Windows.Controls.CheckBox> _tileCheckByKind = new();
    private System.Windows.Point _dragStartPt;
    private System.Windows.Controls.CheckBox? _dragSource;

    /// <summary>Initialize the drag-drop wiring. Called from LoadFromSettings.</summary>
    private void InitTileDragDrop()
    {
        _tileCheckByKind.Clear();
        _tileCheckByKind[nameof(Fluid.Shared.Protocol.TileKind.Cpu)]      = CpuCheck;
        _tileCheckByKind[nameof(Fluid.Shared.Protocol.TileKind.Gpu)]      = GpuCheck;
        _tileCheckByKind[nameof(Fluid.Shared.Protocol.TileKind.Ram)]      = RamCheck;
        _tileCheckByKind[nameof(Fluid.Shared.Protocol.TileKind.Network)]  = NetworkCheck;
        _tileCheckByKind[nameof(Fluid.Shared.Protocol.TileKind.Storage)]  = StorageCheck;
        _tileCheckByKind[nameof(Fluid.Shared.Protocol.TileKind.DateTime)] = DateTimeCheck;

        // Wire drag handlers exactly once per CheckBox. Using PreviewMouseLeftButtonDown
        // so we still own the drag gesture even if the CheckBox's own click handling
        // would otherwise consume it. We start the drag from MouseMove (not Down) so
        // a simple click still toggles the checkbox normally.
        foreach (var cb in _tileCheckByKind.Values)
        {
            cb.AllowDrop          = true;
            // v1.23: the drag-reorder has existed since v1.18 but had zero
            // visual affordance, so nobody knew it was there. Cursor + tooltip
            // make it discoverable.
            cb.Cursor             = System.Windows.Input.Cursors.SizeAll;
            cb.ToolTip            = "Drag onto another tile to reorder the widget";
            cb.PreviewMouseLeftButtonDown -= OnTileCheckMouseDown;
            cb.PreviewMouseLeftButtonDown += OnTileCheckMouseDown;
            cb.PreviewMouseMove   -= OnTileCheckMouseMove;
            cb.PreviewMouseMove   += OnTileCheckMouseMove;
            cb.Drop               -= OnTileCheckDrop;
            cb.Drop               += OnTileCheckDrop;
            cb.DragOver           -= OnTileCheckDragOver;
            cb.DragOver           += OnTileCheckDragOver;
        }
    }

    /// <summary>Assign Grid.Row / Grid.Column to each CheckBox per current TileOrder.</summary>
    private void PositionTileChecks()
    {
        var s = App.Current.Settings;
        int i = 0;
        foreach (var kindName in s.TileOrder)
        {
            if (!_tileCheckByKind.TryGetValue(kindName, out var cb)) continue;
            // 3 columns, 2 rows -- left-to-right, top-to-bottom
            int row = i / 3;
            int col = i % 3;
            System.Windows.Controls.Grid.SetRow(cb, row);
            System.Windows.Controls.Grid.SetColumn(cb, col);
            i++;
        }
    }

    private void OnTileCheckMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dragStartPt = e.GetPosition(this);
        _dragSource  = sender as System.Windows.Controls.CheckBox;
    }

    private void OnTileCheckMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragSource == null) return;
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) { _dragSource = null; return; }
        var p = e.GetPosition(this);
        // Must move at least a system threshold before promoting click to drag.
        // Otherwise a normal click+release would start a phantom DnD operation.
        if (System.Math.Abs(p.X - _dragStartPt.X) < System.Windows.SystemParameters.MinimumHorizontalDragDistance &&
            System.Math.Abs(p.Y - _dragStartPt.Y) < System.Windows.SystemParameters.MinimumVerticalDragDistance) return;
        // Identify the source by its name
        var sourceKind = LookupKindForCheck(_dragSource);
        if (sourceKind == null) return;
        var data = new System.Windows.DataObject("FluidTileKind", sourceKind);
        try
        {
            System.Windows.DragDrop.DoDragDrop(_dragSource, data, System.Windows.DragDropEffects.Move);
        }
        catch { /* drag aborted */ }
        finally { _dragSource = null; }
    }

    private void OnTileCheckDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent("FluidTileKind")
            ? System.Windows.DragDropEffects.Move
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTileCheckDrop(object sender, System.Windows.DragEventArgs e)
    {
        if (!e.Data.GetDataPresent("FluidTileKind")) return;
        var sourceKind = e.Data.GetData("FluidTileKind") as string;
        var targetKind = LookupKindForCheck(sender as System.Windows.Controls.CheckBox);
        if (sourceKind == null || targetKind == null || sourceKind == targetKind) return;

        var s = App.Current.Settings;
        var order = s.TileOrder;
        int srcIdx = order.IndexOf(sourceKind);
        int dstIdx = order.IndexOf(targetKind);
        if (srcIdx < 0 || dstIdx < 0) return;

        // Remove from old position, insert at target position. This produces
        // a "shift" behavior rather than a "swap" -- feels more natural when
        // dragging across multiple cells.
        order.RemoveAt(srcIdx);
        order.Insert(dstIdx, sourceKind);

        SettingsService.Save(s);
        PositionTileChecks();
        (Owner as MainWindow)?.RebuildVisibleTiles();
        RefreshOpenPopout();
        e.Handled = true;
    }

    private string? LookupKindForCheck(System.Windows.Controls.CheckBox? cb)
    {
        if (cb == null) return null;
        foreach (var kv in _tileCheckByKind)
            if (ReferenceEquals(kv.Value, cb)) return kv.Key;
        return null;
    }

    private void RefreshOpenPopout()
    {
        if (_activeDevice == null) return;
        foreach (Window w in Application.Current.Windows)
        {
            if (w is RemotePopoutWindow rpw && rpw.Title.Contains(_activeDevice.Name))
            {
                rpw.ApplySettings();
                rpw.BuildTiles();
                break;
            }
        }
    }

    // v1.20.3: layout toggle handler. The two ToggleButtons (HorizToggle/VertToggle)
    // are mutually exclusive; clicking one un-checks the other. Falls back gracefully
    // if both somehow ended up checked (default to whichever the user just clicked).
    private void OnLayoutToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender is System.Windows.Controls.Primitives.ToggleButton tb)
        {
            // Force IsChecked = true on clicked, false on other (mutual exclusion)
            if (tb == HorizToggle) { HorizToggle.IsChecked = true; VertToggle.IsChecked = false; }
            else                   { VertToggle.IsChecked  = true; HorizToggle.IsChecked = false; }
        }
        App.Current.Settings.Orientation = HorizToggle.IsChecked == true
            ? LayoutOrientation.Horizontal : LayoutOrientation.Vertical;
        SettingsService.Save(App.Current.Settings);
    }

    // ------------------------------------------------------------------
    // Behavior
    // ------------------------------------------------------------------

    private void OnTopmostToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Current.Settings.AlwaysOnTop = TopmostCheck.IsChecked == true;
        SettingsService.Save(App.Current.Settings);
    }

    private void OnSnapToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Current.Settings.SnapToEdges = SnapCheck.IsChecked == true;
        SettingsService.Save(App.Current.Settings);
        // v1.25.37: show/hide the "Snap to windows" sub-option
        SnapWindowsCheck.Visibility = (SnapCheck.IsChecked == true)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSnapWindowsToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        App.Current.Settings.SnapToWindows = SnapWindowsCheck.IsChecked == true;
        SettingsService.Save(App.Current.Settings);
    }

    // v1.23: run-at-startup toggle. Writes a per-user HKCU Run value -- no
    // elevation needed, which is why the checkbox carries no UAC shield.
    // Unchecking also removes the installer's optional startup-folder
    // shortcut so startup can never stay half-enabled via the other path.
    private void OnStartupToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var on = StartupCheck.IsChecked == true;
        try { StartupManager.SetEnabled(on); }
        catch (System.Exception ex)
        {
            FluidMessageBox.Show($"Couldn't update the startup setting:\n{ex.Message}", "fluidMonitor", owner: this);
            _loading = true;
            try { StartupCheck.IsChecked = StartupManager.IsEnabled(); }
            finally { _loading = false; }
            return;
        }
        App.Current.Settings.RunAtStartup = on;
        SettingsService.Save(App.Current.Settings);
    }

    // v1.25.37: temperature unit rocker (°C / °F)
    private void OnTempUnitToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        bool isFahrenheit = sender == FahrenheitToggle;
        CelsiusToggle.IsChecked    = !isFahrenheit;
        FahrenheitToggle.IsChecked =  isFahrenheit;
        App.Current.Settings.UseFahrenheit = isFahrenheit;
        SettingsService.Save(App.Current.Settings);
    }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        OpacityLabel.Text = $"{(int)(e.NewValue * 100)}%";
        if (IsRemote) { _activeDevice!.Popout.Opacity = e.NewValue; RefreshOpenPopout(); }
        else App.Current.Settings.Opacity = e.NewValue;
        SettingsService.Save(App.Current.Settings);
    }

    private async void OnIntervalChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var ms = (int)e.NewValue;
        App.Current.Settings.UpdateIntervalMs = ms;
        IntervalLabel.Text = $"{ms} ms";
        SettingsService.Save(App.Current.Settings);
        // v1.25.50: push to service if the method exists (added by patch-interval.ps1)
        try
        {
            var method = typeof(CmdClient).GetMethod("SetUpdateIntervalAsync");
            if (method != null) await (System.Threading.Tasks.Task)method.Invoke(null, new object[] { ms })!;
        }
        catch { }
    }

    private void OnScaleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        App.Current.Settings.UiScale = e.NewValue;
        ScaleLabel.Text = $"{e.NewValue:0.00}x";
        SettingsService.Save(App.Current.Settings);
    }

    private void OnWidthChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        WidthLabel.Text = $"{e.NewValue:0}px";
        if (IsRemote) { _activeDevice!.Popout.TileWidth = e.NewValue; RefreshOpenPopout(); }
        else { App.Current.Settings.TileWidth = e.NewValue; ThemeApplier.Apply(App.Current.Settings, Application.Current.Resources); }
        SettingsService.Save(App.Current.Settings);
    }

    private void OnHeightChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        HeightLabel.Text = $"{e.NewValue:0}px";
        if (IsRemote) { _activeDevice!.Popout.TileHeight = e.NewValue; RefreshOpenPopout(); }
        else { App.Current.Settings.TileHeight = e.NewValue; ThemeApplier.Apply(App.Current.Settings, Application.Current.Resources); }
        SettingsService.Save(App.Current.Settings);
    }

    // ------------------------------------------------------------------
    // Color swatches + picker
    // ------------------------------------------------------------------

    private void OnModeToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        PushUndoSnapshot();
        var isDark = sender == DarkModeBtn;
        DarkModeBtn.IsChecked  =  isDark;
        LightModeBtn.IsChecked = !isDark;
        var s = App.Current.Settings;
        s.IsDarkMode = isDark;
        ThemeApplier.ApplyModeDefaults(s);

        // v1.25.37: toggling dark/light resets to a clean default state
        // (default skin + Default theme) so the mode switch always looks
        // right. Without this, a dark-optimized skin stays applied in
        // light mode and the widget is unreadable.
        s.ActiveTheme = "Default";
        s.ActiveSkin  = "Default";
        SettingsService.Save(s);

        // Apply the default skin visually
        var skinIdx = _skins.FindIndex(sk => sk.Name == "Default");
        if (skinIdx >= 0) { _skinIndex = skinIdx; UpdateSkinCycler(); }
        SkinManager.ApplySkin("Default", Application.Current.Resources);
        SkinManager.ApplyFontOverrides(s, Application.Current.Resources);

        // v1.25.60: force full opacity for light mode AFTER the skin merges
        // its resource dictionary (which may set SkinWidgetOpacity < 1.0).
        if (!isDark)
        {
            // Nuclear fix: the skin's merged ResourceDictionary can override
            // BackgroundBrush/TileBrush/SkinTileBackground with dark values.
            // Even though top-level entries should win, some WPF resource
            // lookup paths hit the merged dict first. Remove competing keys
            // from the skin dict directly.
            foreach (var md in Application.Current.Resources.MergedDictionaries)
            {
                foreach (var key in new[] { "BackgroundBrush", "TileBrush", "SkinTileBackground", "SkinWidgetOpacity" })
                {
                    if (md.Contains(key))
                        md.Remove(key);
                }
            }
            Application.Current.Resources["SkinWidgetOpacity"] = 1.0;
            var main = Application.Current.MainWindow;
            if (main != null) main.Opacity = 1.0;
            // Re-apply light colors to ensure they're the only definitions
            ThemeApplier.Apply(s, Application.Current.Resources);
        }

        _loading = true;
        BackgroundColorBox.Text = s.BackgroundColor;
        TileColorBox.Text       = s.TileColor;
        AccentColorBox.Text     = s.AccentColor;
        TextColorBox.Text       = s.TextColor;
        MutedTextColorBox.Text  = s.MutedTextColor;
        _loading = false;
        UpdateSwatches();
        ApplyColorsLive();

        // Sync both cyclers to "Default"
        _themeIndex = FindCurrentThemeIndex();
        UpdateThemeCycler();
        LoadThemePresetCycler();
        RebuildUserPresetSlots();
        Fmt.InvalidateAllDeferred();
    }

    private void OnCpuPillToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var isCustom = sender == CpuCustomBtn;
        CpuAutoBtn.IsChecked   = !isCustom;
        CpuCustomBtn.IsChecked =  isCustom;
        CpuCustomNameBox.IsEnabled = isCustom;
        if (isCustom && string.IsNullOrEmpty(CpuCustomNameBox.Text))
            CpuCustomNameBox.Text = App.Current.SensorState.CpuTile.SubHeader;
        if (!isCustom) { App.Current.Settings.CpuCustomName = ""; SettingsService.Save(App.Current.Settings); }
        if (isCustom) CpuCustomNameBox.Focus();
    }

    private void OnGpuPillToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var isCustom = sender == GpuCustomBtn;
        GpuAutoBtn.IsChecked   = !isCustom;
        GpuCustomBtn.IsChecked =  isCustom;
        GpuCustomNameBox.IsEnabled = isCustom;
        if (isCustom && string.IsNullOrEmpty(GpuCustomNameBox.Text))
            GpuCustomNameBox.Text = GetGpuAutoName(_gpuLabelIndex);
        if (!isCustom) SaveGpuCustomName(_gpuLabelIndex, "");
        if (isCustom) GpuCustomNameBox.Focus();
    }

    // Keep old radio handlers as no-ops for compat

    private int _gpuLabelIndex = 0;

    private void OnGpuLabelSelectorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        LoadGpuLabelForIndex(_gpuLabelIndex);
    }

    private void LoadGpuLabelForIndex(int idx)
    {
        var s = App.Current.Settings;
        var customName = s.GpuCustomName;
        var isCustom = !string.IsNullOrEmpty(customName);
        _loading = true;
        GpuAutoBtn.IsChecked     = !isCustom;
        GpuCustomBtn.IsChecked   = isCustom;
        GpuCustomNameBox.IsEnabled = isCustom;
        GpuCustomNameBox.Text      = isCustom ? customName : GetGpuAutoName(idx);
        _loading = false;
    }

    private string GetGpuAutoName(int idx)
    {
        var state = App.Current.SensorState;
        return App.Current.SensorState.GpuTile.SubHeader;
    }

    private void SaveGpuCustomName(int idx, string name)
    {
        var s = App.Current.Settings;
        s.GpuCustomName = name;
        SettingsService.Save(s);
    }

    private void BuildGpuLabelSelector()
    {
        var state = App.Current.SensorState;
    }

    private void OnCpuNameChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loading || CpuCustomBtn.IsChecked != true) return;
        App.Current.Settings.CpuCustomName = CpuCustomNameBox.Text;
        SettingsService.Save(App.Current.Settings);
    }

    private void OnGpuNameChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_loading || GpuCustomBtn.IsChecked != true) return;
        SaveGpuCustomName(_gpuLabelIndex, GpuCustomNameBox.Text);
    }

    private void OnCpuNameReset(object sender, RoutedEventArgs e)
    {
        App.Current.Settings.CpuCustomName = "";
        CpuAutoBtn.IsChecked = true;
        CpuCustomNameBox.IsEnabled = false;
        CpuCustomNameBox.Text = App.Current.SensorState.CpuTile.SubHeader;
        SettingsService.Save(App.Current.Settings);
    }

    private void OnGpuNameReset(object sender, RoutedEventArgs e)
    {
        SaveGpuCustomName(_gpuLabelIndex, "");
        GpuAutoBtn.IsChecked = true;
        GpuCustomNameBox.IsEnabled = false;
        GpuCustomNameBox.Text = GetGpuAutoName(_gpuLabelIndex);
    }

    private void OnClickThroughToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var enabled = ClickThroughCheck.IsChecked == true;
        App.Current.Settings.ClickThrough = enabled;
        SettingsService.Save(App.Current.Settings);
        (Owner as MainWindow)?.SetClickThrough(enabled);
    }

    private void OnIndicatorFontChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var v = (int)e.NewValue;
        IndicatorFontLabel.Text = $"{v:+0;-0;0}pt";
        App.Current.Settings.IndicatorFontSizeOffset = v;
        SettingsService.Save(App.Current.Settings);
        ThemeApplier.Apply(App.Current.Settings, Application.Current.Resources);
    }

    // v1.20.2: muted text contrast slider. Updates AppSettings.MutedContrast
    // and re-applies the theme so MutedTextBrush recomputes immediately.
    private void OnMutedContrastChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var v = e.NewValue;
        App.Current.Settings.MutedContrast = v;
        SettingsService.Save(App.Current.Settings);
        // v1.20.3: MutedContrast does NOT mark the palette as Custom -- it's a
        // render-time multiplier, not a color value change. So we ONLY apply
        // the theme (which recomputes MutedTextBrush) but don't update the
        // cycler index. The cycler keeps whatever theme/color was selected.
        ThemeApplier.Apply(App.Current.Settings, Application.Current.Resources);
    }

    // v1.20.3: Network traffic indicator style cycler. Cycles Off→Blink→Fade→Glow.
    private static readonly string[] _trafficStyles = { "Off", "Blink", "Fade", "Glow" };
    private void OnTrafficIndicatorCycle(object sender, RoutedEventArgs e)
    {
        var s = App.Current.Settings;
        var cur = System.Array.IndexOf(_trafficStyles, s.NetworkTrafficIndicator);
        if (cur < 0) cur = 0;
        cur = (cur + 1) % _trafficStyles.Length;
        s.NetworkTrafficIndicator = _trafficStyles[cur];
        SettingsService.Save(s);
        UpdateTrafficIndicatorButtonLabel();
        // v1.20.3 / v1.21: live-update the singleton driving the animation triggers
        TrafficIndicatorState.Instance.Style = s.NetworkTrafficIndicator;
    }

    private void UpdateTrafficIndicatorButtonLabel()
    {
        if (TrafficBtnLabel != null)
            TrafficBtnLabel.Text = App.Current.Settings.NetworkTrafficIndicator;
        UpdateTrafficPreviewAnimation();
    }

    // v1.25.37: animate the preview arrows next to the traffic indicator
    // button to show what the selected style looks like in real time.
    private System.Windows.Media.Animation.Storyboard? _trafficPreviewStoryboard;
    private void UpdateTrafficPreviewAnimation()
    {
        if (TrafficPreviewDown == null || TrafficPreviewUp == null) return;

        // Stop any running animation
        _trafficPreviewStoryboard?.Stop();
        _trafficPreviewStoryboard = null;
        TrafficPreviewDown.Opacity = 1.0;
        TrafficPreviewUp.Opacity   = 1.0;
        TrafficPreviewDown.Effect  = null;
        TrafficPreviewUp.Effect    = null;

        var style = App.Current.Settings.NetworkTrafficIndicator;
        if (style == "Off")
        {
            TrafficPreviewDown.Opacity = 0.3;
            TrafficPreviewUp.Opacity   = 0.3;
            return;
        }

        var sb = new System.Windows.Media.Animation.Storyboard { RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever };
        System.Windows.Media.Animation.DoubleAnimation downAnim, upAnim;

        switch (style)
        {
            case "Blink":
                downAnim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.15, TimeSpan.FromMilliseconds(300))
                    { AutoReverse = true };
                upAnim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.15, TimeSpan.FromMilliseconds(300))
                    { AutoReverse = true, BeginTime = TimeSpan.FromMilliseconds(300) };
                break;
            case "Fade":
                downAnim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.15, TimeSpan.FromMilliseconds(600))
                    { AutoReverse = true };
                upAnim = new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.15, TimeSpan.FromMilliseconds(600))
                    { AutoReverse = true, BeginTime = TimeSpan.FromMilliseconds(600) };
                break;
            case "Glow":
                // Static bright + halo (no animation). Matches the widget
                // where Glow is a steady accent-colored bloom, not a pulse.
                var accent = Application.Current.Resources["AccentBrush"] is SolidColorBrush ab
                    ? ab.Color : Colors.DodgerBlue;
                var glow = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = accent, ShadowDepth = 0, BlurRadius = 10, Opacity = 0.9
                };
                TrafficPreviewDown.Effect = glow;
                TrafficPreviewUp.Effect   = glow;
                return; // no storyboard needed
            default: return;
        }

        System.Windows.Media.Animation.Storyboard.SetTarget(downAnim, TrafficPreviewDown);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(downAnim, new PropertyPath(UIElement.OpacityProperty));
        System.Windows.Media.Animation.Storyboard.SetTarget(upAnim, TrafficPreviewUp);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(upAnim, new PropertyPath(UIElement.OpacityProperty));
        sb.Children.Add(downAnim);
        sb.Children.Add(upAnim);
        sb.Begin();
        _trafficPreviewStoryboard = sb;
    }

    // v1.20.3: Network arrow spacing slider. Live updates the TileData
    // binding for the column width so the widget reflects immediately.
    private void OnNetworkArrowSpacingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var v = e.NewValue;
        NetArrowSpacingLabel.Text = $"{(int)v}px";
        App.Current.Settings.NetworkArrowSpacing = v;
        SettingsService.Save(App.Current.Settings);
        // Live-update the TileData on the widget
        var st = App.Current.SensorState;
        if (st != null) st.NetworkTile.LabelColumnWidth = v;
    }

    // v1.20.3: Disk R:/W: spacing slider.
    private void OnDiskLabelSpacingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var v = e.NewValue;
        DiskLabelSpacingLabel.Text = $"{(int)v}px";
        App.Current.Settings.DiskLabelSpacing = v;
        SettingsService.Save(App.Current.Settings);
        var st = App.Current.SensorState;
        if (st != null) st.StorageTile.LabelColumnWidth = v;
    }

    // v1.25.16: per-tile font-size sliders. Each is additive on top of the
    // global IndicatorFontSizeOffset slider, applied via ThemeApplier.Apply.
    private void OnArrowFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var v = (int)e.NewValue;
        ArrowFontSizeLabel.Text = $"{v:+0;-0;0}pt";
        App.Current.Settings.ArrowFontSizeOffset = v;
        SettingsService.Save(App.Current.Settings);
        ThemeApplier.Apply(App.Current.Settings, Application.Current.Resources);
    }

    private void OnDiskLabelFontSizeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var v = (int)e.NewValue;
        DiskLabelFontSizeLabel.Text = $"{v:+0;-0;0}pt";
        App.Current.Settings.DiskLabelFontSizeOffset = v;
        SettingsService.Save(App.Current.Settings);
        ThemeApplier.Apply(App.Current.Settings, Application.Current.Resources);
    }

    // v1.20.3: Disk picker dropdown. Lists physical disks from WMI. Changing
    // the selection saves to AppSettings; the service reads SelectedDiskId at
    // its own startup so the user needs to restart the app (which on Windows
    // restarts the service config-read) for the change to take effect.
    private class DiskComboItem
    {
        public string Id      { get; set; } = "";   // "0", "1", ...
        public string Model   { get; set; } = "";
        public string Caption { get; set; } = "";   // e.g. "C: D:"
        public string Display => string.IsNullOrEmpty(Id) || Id == "*"
            ? "(All disks)"
            : string.IsNullOrEmpty(Model)
                ? $"Disk {Id}"
                : $"Disk {Id}  -  {Model}";
    }
    private void LoadDiskCombo()
    {
        // v1.21: guard with _loading -- this runs in the constructor AFTER the
        // _loading=false line, and programmatic SelectedItem assignment fires
        // OnDiskChanged. Without the guard, merely OPENING Settings used to
        // save SelectedDiskId="0", silently converting the all-disks default.
        _loading = true;
        try
        {
            DiskCombo.Items.Clear();
            // v1.21: explicit aggregate entry so the user can get back to
            // all-disks mode after picking a specific disk.
            DiskCombo.Items.Add(new DiskComboItem { Id = "*", Model = "" }); // v1.23: explicit aggregate sentinel
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT Index, Model, Caption FROM Win32_DiskDrive");
                foreach (System.Management.ManagementObject m in searcher.Get())
                {
                    var idx = m["Index"]?.ToString() ?? "";
                    var mdl = m["Model"]?.ToString() ?? "";
                    var cap = m["Caption"]?.ToString() ?? "";
                    DiskCombo.Items.Add(new DiskComboItem { Id = idx, Model = mdl, Caption = cap });
                }
            }
            catch { /* WMI may be slow or fail; combo keeps just the aggregate entry */ }

            // v1.23: "" now means "never chosen" and resolves to the physical
            // disk that hosts the system drive (so the tile shows a model out
            // of the box, matching the tooltip's promise). The explicit
            // all-disks choice is stored as "*".
            var saved = App.Current.Settings.SelectedDiskId;
            if (string.IsNullOrEmpty(saved))
            {
                var sysDisk = ResolveSystemDiskIndex();
                if (!string.IsNullOrEmpty(sysDisk)) saved = sysDisk;
            }
            foreach (DiskComboItem it in DiskCombo.Items)
            {
                if (it.Id == saved) { DiskCombo.SelectedItem = it; break; }
            }
            if (DiskCombo.SelectedItem == null) DiskCombo.SelectedIndex = 0;

            // v1.23: persist + push the resolved default so the SERVICE also
            // tracks the system disk (and resolves its model for the tile)
            // without requiring the user to touch the dropdown first.
            if (string.IsNullOrEmpty(App.Current.Settings.SelectedDiskId) &&
                DiskCombo.SelectedItem is DiskComboItem sel && sel.Id != "*")
            {
                App.Current.Settings.SelectedDiskId = sel.Id;
                SettingsService.Save(App.Current.Settings);
                _ = PushDiskSelectionAsync(sel.Id);
            }
        }
        finally { _loading = false; }
    }

    private static async System.Threading.Tasks.Task PushDiskSelectionAsync(string id)
    {
        try { await CmdClient.SetSelectedDiskAsync(id); } catch { }
    }

    /// <summary>v1.23: WMI association walk LogicalDisk(system drive) -> Partition -> DiskDrive.Index.</summary>
    private static string ResolveSystemDiskIndex()
    {
        try
        {
            var sysLetter = System.IO.Path.GetPathRoot(System.Environment.SystemDirectory)?.TrimEnd('\\') ?? "C:";
            using var parts = new System.Management.ManagementObjectSearcher(
                $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{sysLetter}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");
            foreach (System.Management.ManagementObject part in parts.Get())
            {
                using var drives = new System.Management.ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{part["DeviceID"]}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                foreach (System.Management.ManagementObject drive in drives.Get())
                    return drive["Index"]?.ToString() ?? "";
            }
        }
        catch { }
        return "";
    }
    private async void OnDiskChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (DiskCombo.SelectedItem is not DiskComboItem it) return;
        App.Current.Settings.SelectedDiskId = it.Id;
        SettingsService.Save(App.Current.Settings);
        // v1.21: push the selection to the service over the cmd pipe. The
        // service persists it to service.json and re-routes its perf counters
        // live -- no restart needed. (Previously the value only landed in the
        // user's settings.json, which the LocalSystem service never reads, so
        // the picker had no effect at all.)
        try { await CmdClient.SetSelectedDiskAsync(it.Id); } catch { }
    }

    // v1.24: Disk tile label style cycler (Letter -> Model -> Both).
    // Display-only preference; SensorState reads it on every snapshot, so
    // the tile updates on the next tick with no service involvement.
    private void RefreshDiskLabelStyleBtn()
    {
        DiskLabelStyleBtn.Content = App.Current.Settings.DiskLabelStyle switch
        {
            "Model" => "Show: Model",
            "Both"  => "Show: Both",
            _       => "Show: Drive letter",
        };
    }

    private void OnDiskLabelStyleCycle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var s = App.Current.Settings;
        s.DiskLabelStyle = s.DiskLabelStyle switch
        {
            "Letter" => "Model",
            "Model"  => "Both",
            _        => "Letter",
        };
        SettingsService.Save(s);
        RefreshDiskLabelStyleBtn();
    }

    // v1.25: CPU temperature opt-in / opt-out row. Reflects whether the sensor
    // driver is installed and flips the button between Set up and Remove.
    // v1.25.1: also drives the attention border/arrow + one-shot pulse when
    // attention is needed (no driver AND user hasn't dismissed).
    private void RefreshCpuTempRow()
    {
        bool installed;
        try { installed = Services.CpuSensorDriver.IsInstalled(); } catch { installed = false; }
        bool dismissed = App.Current.Settings.CpuTempHintDismissed;
        bool needsAttention = !installed && !dismissed;

        if (installed)
        {
            // v1.25.49: driver installed = hide entire Sensors section.
            SensorsSection.Visibility  = Visibility.Collapsed;
            CpuTempSetupRow.Visibility = Visibility.Visible;
            CpuTempStatus.Text = "Active";
            CpuTempStatus.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
        }
        else if (dismissed)
        {
            var choice = App.Current.Settings.CpuTempDismissChoice ?? "";
            CpuTempStatusLabel.Text = choice == "HideTile"
                ? "Off — CPU tile is hidden. Set up to bring it back."
                : "Off — showing load only. Set up to add temperature.";
            CpuTempActionBtn.Content = "Set up";
            CpuTempActionBtn.Background = (System.Windows.Media.Brush)FindResource("AccentBrush");
            CpuTempActionBtn.Foreground = System.Windows.Media.Brushes.White;
            SensorsSection.Visibility  = Visibility.Collapsed;
            CpuTempSetupRow.Visibility = Visibility.Visible;
            CpuTempStatus.Text = "Inactive";
            CpuTempStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC0, 0x60, 0x60));
        }
        else
        {
            CpuTempStatusLabel.Text = "Off — showing load only. Set up to add temperature.";
            CpuTempActionBtn.Content = "Set up";
            CpuTempActionBtn.Background = (System.Windows.Media.Brush)FindResource("AccentBrush");
            CpuTempActionBtn.Foreground = System.Windows.Media.Brushes.White;
            SensorsSection.Visibility  = Visibility.Visible;
            CpuTempSetupRow.Visibility = Visibility.Visible;
            CpuTempStatus.Text = "Inactive";
            CpuTempStatus.Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC0, 0x60, 0x60));
        }

        CpuTempAttentionBorder.BorderThickness = new Thickness(needsAttention ? 1.5 : 0);
        CpuTempAttentionArrow.Visibility = needsAttention ? Visibility.Visible : Visibility.Collapsed;

        if (needsAttention && !_cpuTempPulsed)
        {
            _cpuTempPulsed = true;
            PulseCpuTempAttention();
        }
    }

    // One-shot pulse: fades the border/arrow opacity 1->0.4->1 twice over ~1.4s,
    // calling attention without becoming a constant nag. Runs once per settings
    // window lifetime via the _cpuTempPulsed guard.
    private bool _cpuTempPulsed;
    private void PulseCpuTempAttention()
    {
        var anim = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames
        {
            Duration = new System.Windows.Duration(System.TimeSpan.FromMilliseconds(1400)),
        };
        anim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(1.0, System.Windows.Media.Animation.KeyTime.FromPercent(0.0)));
        anim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.4, System.Windows.Media.Animation.KeyTime.FromPercent(0.25)));
        anim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(1.0, System.Windows.Media.Animation.KeyTime.FromPercent(0.5)));
        anim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(0.4, System.Windows.Media.Animation.KeyTime.FromPercent(0.75)));
        anim.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(1.0, System.Windows.Media.Animation.KeyTime.FromPercent(1.0)));
        CpuTempAttentionBorder.BeginAnimation(Border.OpacityProperty, anim);
        CpuTempAttentionArrow.BeginAnimation(Border.OpacityProperty, anim);
    }

    private async void OnCpuTempAction(object sender, RoutedEventArgs e)
    {
        if (Services.CpuSensorDriver.IsInstalled())
        {
            // Opt-out: remove the driver (one UAC prompt), then re-check.
            CpuTempActionBtn.IsEnabled = false;
            CpuTempStatusLabel.Text = "Removing…";
            var outcome = await Services.CpuSensorDriver.UninstallAsync();
            try { await Services.CmdClient.RecheckSensorsAsync(); } catch { }
            CpuTempActionBtn.IsEnabled = true;
            RefreshCpuTempRow();
            if (outcome.Result == Services.CpuSensorDriver.Result.Failed &&
                !string.IsNullOrEmpty(outcome.Detail))
                CpuTempStatusLabel.Text = outcome.Detail;
        }
        else
        {
            // Opt-in: reuse the full explainer dialog (same as the tile hint).
            var dlg = new CpuTempDialog { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
            // v1.25.1: enabling clears any prior dismissal so future absences
            // of the driver surface the hint again.
            if (dlg.DriverNowInstalled)
            {
                var s = App.Current.Settings;
                if (s.CpuTempHintDismissed)
                {
                    s.CpuTempHintDismissed = false;
                    s.CpuTempDismissChoice = "";
                    Services.SettingsService.Save(s);
                }
            }
            RefreshCpuTempRow();
        }
    }

    // v1.25.2: collapse/expand the Remote Monitoring body (and Remote Devices
    // via its own visibility binding). Default-collapsed; auto-expanded on load
    // when the TCP feed is already enabled so an active config stays visible.
    private void OnRemoteMonitoringToggle(object sender, RoutedEventArgs e)
    {
        if (RemoteMonitoringBody != null)
            RemoteMonitoringBody.Visibility =
                RemoteMonitoringToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------
    // Slider default-value tick marks (v1.25.45)
    // ------------------------------------------------------------------
    // A thin 1px vertical line at each slider's factory default position.
    // Uses a Border element added to the slider's parent Grid at the same
    // row/column, repositioned on SizeChanged.

    private void InitSliderDefaultMarkers()
    {
        AddDefaultMarker(OpacitySlider,          0.9,  0.3, 1.0);
        AddDefaultMarker(IntervalSlider,         1500, 250, 5000);
        AddDefaultMarker(ScaleSlider,            1.0,  0.75, 1.5);
        AddDefaultMarker(WidthSlider,            130,  110, 200);
        AddDefaultMarker(HeightSlider,           110,  80,  150);
        AddDefaultMarker(NetArrowSpacingSlider,  16,   8,   40);
        AddDefaultMarker(ArrowFontSizeSlider,    0,    -5,  10);
        AddDefaultMarker(DiskLabelSpacingSlider, 16,   8,   40);
        AddDefaultMarker(DiskLabelFontSizeSlider,0,    -5,  10);
        AddDefaultMarker(MutedContrastSlider,    1.0,  0.5, 1.6);
        AddDefaultMarker(PrimaryFontSlider,      0,    -5,  5);
        AddDefaultMarker(SecondaryFontSlider,    0,    -5,  5);
        AddDefaultMarker(IndicatorFontSlider,    0,    -5,  5);
    }

    private void AddDefaultMarker(Slider slider, double defaultVal, double min, double max)
    {
        if (slider?.Parent is not Grid parentGrid) return;

        var marker = new System.Windows.Controls.Border
        {
            Width = 1.5,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            IsHitTestVisible = false,
            Opacity = 0.5,
        };
        marker.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "MutedTextBrush");

        Grid.SetColumn(marker, Grid.GetColumn(slider));
        Grid.SetRow(marker, Grid.GetRow(slider));
        parentGrid.Children.Add(marker);

        // The Slim template has a 12px thumb (Ellipse Width=12) with no
        // extra padding. The track spans the full slider width; the thumb
        // center sits at 6px for Minimum and (width-6) for Maximum.
        const double thumbHalf = 6.0;

        void UpdatePosition()
        {
            var w = slider.ActualWidth;
            if (w <= 12) return;
            var frac = (defaultVal - min) / (max - min);
            var x = thumbHalf + frac * (w - thumbHalf * 2);
            marker.Margin = new Thickness(x - 0.75, 0, 0, 0); // -0.75 = half marker width
        }

        void UpdateGlow()
        {
            var range = max - min;
            if (range <= 0) return;
            var dist = Math.Abs(slider.Value - defaultVal) / range;
            if (dist < 0.05)
            {
                marker.Opacity = 1.0;
                marker.Width = 2;
                marker.SetResourceReference(
                    System.Windows.Controls.Border.BackgroundProperty, "AccentBrush");
            }
            else if (dist < 0.15)
            {
                var t = (dist - 0.05) / 0.10;
                marker.Opacity = 1.0 - t * 0.4;
                marker.Width = 2.0 - t * 0.5;
                marker.SetResourceReference(
                    System.Windows.Controls.Border.BackgroundProperty, "AccentBrush");
            }
            else
            {
                marker.Opacity = 0.5;
                marker.Width = 1.5;
                marker.SetResourceReference(
                    System.Windows.Controls.Border.BackgroundProperty, "MutedTextBrush");
            }
        }

        slider.ValueChanged += (s, e) => UpdateGlow();
        slider.Loaded += (s, e) =>
        {
            Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Loaded,
                new Action(() => { UpdatePosition(); UpdateGlow(); }));
        };
        slider.SizeChanged += (s, e) => UpdatePosition();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnPrimaryFontChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var v = (int)e.NewValue;
        PrimaryFontLabel.Text = $"{v:+0;-0;0}pt";
        App.Current.Settings.PrimaryFontSizeOffset = v;
        SettingsService.Save(App.Current.Settings);
        ThemeApplier.Apply(App.Current.Settings, Application.Current.Resources);
    }

    private void OnSecondaryFontChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        var v = (int)e.NewValue;
        SecondaryFontLabel.Text = $"{v:+0;-0;0}pt";
        App.Current.Settings.SecondaryFontSizeOffset = v;
        SettingsService.Save(App.Current.Settings);
        ThemeApplier.Apply(App.Current.Settings, Application.Current.Resources);
    }


    // ------------------------------------------------------------------
    // Network adapter
    // ------------------------------------------------------------------

    private void LoadNetworkAdapterCombo()
    {
        // v1.21: guard with _loading -- runs after the constructor's
        // _loading=false line, and programmatic selection fires
        // OnNetworkAdapterChanged (which saves + sends to the service).
        _loading = true;
        try
        {
            var adapters = new List<string> { "(All adapters)" };
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    adapters.Add(ni.Name);
                }
            }
            catch { }
            // v1.21: keep a temporarily-down saved adapter in the list instead of
            // showing a blank selection (and risking the choice being wiped).
            var current = App.Current.Settings.NetworkAdapterName;
            if (!string.IsNullOrEmpty(current) && !adapters.Contains(current))
                adapters.Add(current);
            NetworkAdapterCombo.ItemsSource = adapters;
            NetworkAdapterCombo.SelectedItem = string.IsNullOrEmpty(current) ? "(All adapters)" : current;
        }
        finally { _loading = false; }
    }

    private async void OnNetworkAdapterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        var selected = NetworkAdapterCombo.SelectedItem as string ?? "";
        var adapterName = selected == "(All adapters)" ? "" : selected;
        App.Current.Settings.NetworkAdapterName = adapterName;
        SettingsService.Save(App.Current.Settings);
        try { await CmdClient.SetNetworkAdapterAsync(adapterName); } catch { }
    }

    // ------------------------------------------------------------------
    // Connection status live refresh
    // ------------------------------------------------------------------

    private bool _capturingHotkey;

    private void OnHotkeyBoxFocused(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        HotkeyPrompt.Text = " (press key combo…)";
    }

    private void OnHotkeyBoxUnfocused(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = false;
        HotkeyPrompt.Text = " (click to set)";
    }

    private void OnHotkeyCapture(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_capturingHotkey) return;
        e.Handled = true;

        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        // v1.21: Escape cancels capture -- it must never become the hotkey.
        // This handler saves and registers immediately, so a bare Esc here
        // used to register a GLOBAL no-modifier Escape hotkey that hijacked
        // the key from every app on the system.
        if (key == System.Windows.Input.Key.Escape)
        {
            _capturingHotkey = false;
            HotkeyPrompt.Text = " (click to set)";
            Keyboard.ClearFocus();
            return;
        }
        // Ignore modifier-only keys
        if (key == System.Windows.Input.Key.LeftCtrl  || key == System.Windows.Input.Key.RightCtrl ||
            key == System.Windows.Input.Key.LeftShift || key == System.Windows.Input.Key.RightShift ||
            key == System.Windows.Input.Key.LeftAlt   || key == System.Windows.Input.Key.RightAlt ||
            key == System.Windows.Input.Key.LWin      || key == System.Windows.Input.Key.RWin)
            return;
        // v1.21: require at least one modifier so a bare key can't be
        // registered as a system-wide hotkey.
        if (System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None)
        {
            HotkeyPrompt.Text = " (add Ctrl/Alt/Shift…)";
            return;
        }

        var mods    = System.Windows.Input.Keyboard.Modifiers;
        var combo   = HotkeyHelper.FormatCombo(mods, key);
        HotkeyBox.Text = combo;
        App.Current.Settings.ClickThroughHotkey = combo;
        SettingsService.Save(App.Current.Settings);
        (Owner as MainWindow)?.RegisterClickThroughHotkey();
        _capturingHotkey = false;
        HotkeyPrompt.Text = " (click to set)";
        Keyboard.ClearFocus();
    }

    private void OnClearHotkey(object sender, RoutedEventArgs e)
    {
        HotkeyBox.Text = "";
        App.Current.Settings.ClickThroughHotkey = "";
        SettingsService.Save(App.Current.Settings);
        (Owner as MainWindow)?.RegisterClickThroughHotkey();
    }

    // ------------------------------------------------------------------
    // Remote Monitoring
    // ------------------------------------------------------------------

    private async void LoadRemoteMonitoringSection()
    {
        HandshakeKeyBox.Text = "Loading…";
        try
        {
            var (tcpEnabled, _, key, _) = await CmdClient.GetConfigAsync();
            _loading = true;
            TcpEnabledCheck.IsChecked = tcpEnabled;
            _loading = false;
            HandshakeKeyBox.Text = key;
            // v1.25.2: if the feed is already on, expand the (default-collapsed)
            // section so the active config is visible without a click.
            if (tcpEnabled)
            {
                RemoteMonitoringToggle.IsChecked = true;
                if (RemoteMonitoringBody != null)
                    RemoteMonitoringBody.Visibility = Visibility.Visible;
            }
        }
        catch { HandshakeKeyBox.Text = "(service not running)"; }
    }

    private async void OnTcpEnabledToggle(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        try { await CmdClient.SetTcpEnabledAsync(TcpEnabledCheck.IsChecked == true); }
        catch { }
    }

    private void OnCopyHandshakeKey(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(HandshakeKeyBox.Text) && !HandshakeKeyBox.Text.StartsWith("("))
            Clipboard.SetText(HandshakeKeyBox.Text);
    }

    private async void OnRegenerateKey(object sender, RoutedEventArgs e)
    {
        if (FluidMessageBox.Show(
            "Regenerating the Handshake Key will disconnect ALL remote devices " +
            "that have this machine configured.\n\nContinue?",
            "Regenerate Handshake Key",
            MessageBoxButton.YesNo, this) != MessageBoxResult.Yes) return;

        RegenerateStatus.Text = "Regenerating…";
        try
        {
            var newKey = await CmdClient.RegenerateKeyAsync();
            HandshakeKeyBox.Text  = newKey;
            RegenerateStatus.Text = "✓ Key regenerated";
        }
        catch { RegenerateStatus.Text = "Failed — is service running?"; }
    }

    // ------------------------------------------------------------------
    // Remote Devices list
    // ------------------------------------------------------------------

    private void RefreshDeviceList()
    {
        var devices = App.Current.Settings.RemoteDevices;
        DeviceCountLabel.Text  = $"{devices.Count} / 5 devices configured";
        AddDeviceBtn.IsEnabled = devices.Count < 5;

        // Ensure a client exists for each device (so IsConnected can be accurate)
        foreach (var d in devices)
            App.Current.DeviceManager.GetOrCreate(d);

        _deviceItems = devices.ConvertAll(d =>
            new DeviceStatusItem(d, App.Current.DeviceManager.IsConnected(d.Id)));

        DeviceList.ItemsSource = _deviceItems;
    }

    private void OnShowAddDevice(object sender, RoutedEventArgs e)
    {
        AddDevicePanel.Visibility = Visibility.Visible;
        AddDeviceBtn.Visibility   = Visibility.Collapsed;
        NewDeviceName.Text = ""; NewDeviceIp.Text = ""; NewDeviceKey.Text = "";
        TestStatus.Text = "";
    }

    private void OnCancelAddDevice(object sender, RoutedEventArgs e)
    {
        AddDevicePanel.Visibility = Visibility.Collapsed;
        AddDeviceBtn.Visibility   = Visibility.Visible;
    }

    private async void OnTestConnection(object sender, RoutedEventArgs e)
    {
        TestStatus.Text = "Testing…";
        var dev = BuildDeviceFromForm();
        if (dev == null) { TestStatus.Text = "Fill in all fields first"; return; }
        var error = await RemoteTcpClient.TestAsync(dev);
        TestStatus.Text = error == null ? "✓ Connected" : $"✗ {error}";
        TestStatus.Foreground = error == null
            ? (FindResource("AccentBrush") as Brush) : Brushes.IndianRed;
    }

    private void OnSaveDevice(object sender, RoutedEventArgs e)
    {
        var dev = BuildDeviceFromForm();
        if (dev == null) { TestStatus.Text = "Fill in all fields first"; return; }
        App.Current.Settings.RemoteDevices.Add(dev);
        SettingsService.Save(App.Current.Settings);
        OnCancelAddDevice(sender, e);
        RefreshDeviceList();
        (Owner as MainWindow)?.RefreshDeviceSwitcher();
    }

    private RemoteDevice? BuildDeviceFromForm()
    {
        var name = NewDeviceName.Text.Trim();
        var ip   = NewDeviceIp.Text.Trim();
        var key  = NewDeviceKey.Text.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(ip) ||
            !TcpProtocol.TryDecodeHandshakeKey(key, out _, out _))
            return null;
        return new RemoteDevice { Name = name, IpAddress = ip, HandshakeKey = key };
    }

    private void OnDeviceRemove(object sender, RoutedEventArgs e)
    {
        if (!TryGetGuidTag(sender, out var id)) return;
        App.Current.Settings.RemoteDevices.RemoveAll(d => d.Id == id);
        App.Current.DeviceManager.Remove(id);
        SettingsService.Save(App.Current.Settings);
        RefreshDeviceList();
        (Owner as MainWindow)?.RefreshDeviceSwitcher();
    }

    private void OnDevicePopout(object sender, RoutedEventArgs e)
    {
        if (!TryGetGuidTag(sender, out var id)) return;
        var dev = App.Current.Settings.RemoteDevices.Find(d => d.Id == id);
        if (dev == null) return;
        new RemotePopoutWindow(dev).Show();
    }

    private void OnDeviceSwitch(object sender, RoutedEventArgs e)
    {
        if (!TryGetGuidTag(sender, out var id)) return;
        (Owner as MainWindow)?.SwitchToDevice(id);
        Close();
    }

    private static bool TryGetGuidTag(object sender, out Guid id)
    {
        id = Guid.Empty;
        if (sender is not Button btn) return false;
        if (btn.Tag is Guid g)   { id = g; return true; }
        if (btn.Tag is string s) return Guid.TryParse(s, out id);
        return false;
    }

    private void OnOpenGameMode(object sender, RoutedEventArgs e)
        => new GameModeWindow { Owner = this }.ShowDialog();

    private void OnOpenWarnings(object sender, RoutedEventArgs e)
        => new WarningsWindow(_activeDevice) { Owner = this }.ShowDialog();

    // v1.25.18: Utilities accessible from Settings bottom bar too (was only
    // reachable from the widget tray previously).
    private void OnOpenTweaks(object sender, RoutedEventArgs e)
        => new TweaksWindow { Owner = this }.ShowDialog();

    private void OnOpenHelp(object sender, RoutedEventArgs e)
        => new HelpWindow { Owner = this }.ShowDialog();

    private void OnOpenTools(object sender, RoutedEventArgs e)
        => new ToolsWindow(this) { Owner = this }.ShowDialog();

    // v1.25.37: CPU temperature info dot handler (opens the same setup
    // dialog that the widget banner used to trigger).
    private async void OnCpuTempInfoClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        bool installed;
        try { installed = Services.CpuSensorDriver.IsInstalled(); } catch { installed = false; }

        if (installed)
        {
            // Driver is active — offer info + uninstall
            var result = FluidMessageBox.Show(
                "CPU temperature sensor is active.\n\n" +
                "The PawnIO driver is installed and providing hardware temperature readings. " +
                "If you uninstall it, CPU temperature monitoring will stop after the next restart.\n\n" +
                "Would you like to uninstall the sensor driver?",
                "CPU temperature",
                MessageBoxButton.YesNo, this);

            if (result == MessageBoxResult.Yes)
            {
                var confirm = FluidMessageBox.Show(
                    "After restarting your computer, CPU temperature will no longer be available. " +
                    "You can reinstall the driver at any time from this same menu.\n\n" +
                    "Uninstall now?",
                    "Confirm uninstall",
                    MessageBoxButton.YesNo, this);

                if (confirm == MessageBoxResult.Yes)
                {
                    try
                    {
                        await Services.CpuSensorDriver.UninstallAsync();
                        // Mark as dismissed so the info row shows instead of
                        // the empty Sensors section
                        var s = App.Current.Settings;
                        s.CpuTempHintDismissed = true;
                        s.CpuTempDismissChoice = "LoadOnly";
                        SettingsService.Save(s);
                        FluidMessageBox.Show(
                            "Driver uninstalled. CPU temperature will stop showing after your next restart.",
                            "Uninstalled", owner: this);
                    }
                    catch (System.Exception ex)
                    {
                        FluidMessageBox.Show(
                            $"Could not uninstall: {ex.Message}",
                            "Uninstall failed", owner: this);
                    }
                }
            }
        }
        else
        {
            // Not installed — open the setup wizard
            try
            {
                var dlg = new CpuTempDialog { Owner = this };
                dlg.ShowDialog();
            }
            catch { /* dialog crash guard */ }
        }
        RefreshCpuTempRow();
    }

    private void OnDismissSensors(object sender, RoutedEventArgs e)
    {
        App.Current.Settings.CpuTempHintDismissed = true;
        SettingsService.Save(App.Current.Settings);
        SensorsSection.Visibility  = Visibility.Collapsed;
        CpuTempSetupRow.Visibility = Visibility.Visible;
    }

    // ------------------------------------------------------------------
    // Helper stubs (device selector moved to top bar, status refresh)
    // ------------------------------------------------------------------

    private void LoadTileDeviceCombo() { /* device switcher moved to top bar */ }

    private void StartStatusRefreshTimer()
    {
        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusTimer.Tick += (_, _) => RefreshDeviceList();
        _statusTimer.Start();
        Closed += (_, _) => _statusTimer?.Stop();
    }

    // ------------------------------------------------------------------
    // Updates section (v1.0.6: moved from UpdatesWindow into Settings)
    // ------------------------------------------------------------------

    private void LoadUpdateSection()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersionLabel.Text = $"v{version?.Major}.{version?.Minor}.{version?.Build}";

        var mode = App.Current.Settings.UpdateCheckMode ?? "Manual";
        UpdateAutoBtn.IsChecked   = mode == "Auto";
        UpdateManualBtn.IsChecked = mode == "Manual";
        UpdateOffBtn.IsChecked    = mode == "Off";
        CheckNowBtn.IsEnabled     = mode != "Off";

        RefreshLastChecked();
    }

    private void OnUpdateModeChanged(object sender, RoutedEventArgs e)
    {
        var mode = UpdateAutoBtn.IsChecked == true ? "Auto"
                 : UpdateOffBtn.IsChecked == true  ? "Off"
                 : "Manual";
        App.Current.Settings.UpdateCheckMode = mode;
        SettingsService.Save(App.Current.Settings);
        CheckNowBtn.IsEnabled = mode != "Off";
    }

    private async void OnCheckNow(object sender, RoutedEventArgs e)
    {
        CheckNowBtn.IsEnabled = false;
        UpdateStatusText.Text = "Checking...";
        NewVersionRow.Visibility = Visibility.Collapsed;
        ChangelogBox.Visibility = Visibility.Collapsed;
        DownloadBtn.Visibility = Visibility.Collapsed;
        LaterBtn.Visibility = Visibility.Collapsed;

        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var current = $"{version?.Major}.{version?.Minor}.{version?.Build}";
            var update = await UpdateService.CheckAsync(current);

            App.Current.Settings.LastUpdateCheck = DateTime.Now.ToString("o");
            SettingsService.Save(App.Current.Settings);
            RefreshLastChecked();

            if (update == null)
            {
                UpdateStatusText.Text = "Up to date";
                UpdateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x58, 0xC8, 0x58));
            }
            else
            {
                _pendingUpdate = update;
                ShowUpdateAvailable(update);
            }
        }
        catch
        {
            UpdateStatusText.Text = "Check failed — try again later";
            UpdateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x60, 0x60));
        }
        finally
        {
            var mode = UpdateOffBtn.IsChecked == true ? "Off" : "other";
            CheckNowBtn.IsEnabled = mode != "Off";
        }
    }

    private void ShowUpdateAvailable(UpdateService.UpdateInfo update)
    {
        UpdateStatusText.Text = "";
        NewVersionRow.Visibility = Visibility.Visible;
        NewVersionLabel.Text = $"v{update.Version}";

        ChangelogBox.Visibility = Visibility.Visible;
        var lines = update.Changelog.Split('\n');
        var bullets = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith("- ") || t.StartsWith("* "))
                bullets.AppendLine(t);
        }
        ChangelogText.Text = bullets.Length > 0 ? bullets.ToString().TrimEnd() : update.Changelog.Trim();

        CheckNowBtn.Visibility = Visibility.Collapsed;
        DownloadBtn.Visibility = Visibility.Visible;
        LaterBtn.Visibility = Visibility.Visible;
    }

    private async void OnUpdateDownload(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;
        DownloadBtn.IsEnabled = false;
        DownloadBtn.Content = "Downloading...";

        try
        {
            var path = await UpdateService.DownloadAsync(
                _pendingUpdate.DownloadUrl,
                new Progress<double>(p =>
                    Dispatcher.Invoke(() =>
                        DownloadBtn.Content = $"Downloading {p:P0}...")));

            DownloadBtn.Content = "Installing...";
            UpdateService.LaunchInstallerAndExit(path);
        }
        catch (Exception ex)
        {
            DownloadBtn.Content = "Download failed";
            DownloadBtn.IsEnabled = true;
            UpdateStatusText.Text = ex.Message;
        }
    }

    private void OnUpdateLater(object sender, RoutedEventArgs e)
    {
        NewVersionRow.Visibility = Visibility.Collapsed;
        ChangelogBox.Visibility = Visibility.Collapsed;
        DownloadBtn.Visibility = Visibility.Collapsed;
        LaterBtn.Visibility = Visibility.Collapsed;
        CheckNowBtn.Visibility = Visibility.Visible;
        UpdateStatusText.Text = "";
    }

    private void RefreshLastChecked()
    {
        var raw = App.Current.Settings.LastUpdateCheck;
        if (string.IsNullOrEmpty(raw))
        {
            LastCheckedLabel.Text = "Last checked: never";
        }
        else if (DateTime.TryParse(raw, out var dt))
        {
            var ago = DateTime.Now - dt;
            LastCheckedLabel.Text = ago.TotalMinutes < 1   ? "Last checked: just now"
                                 : ago.TotalMinutes < 60   ? $"Last checked: {(int)ago.TotalMinutes} min ago"
                                 : ago.TotalHours < 24     ? $"Last checked: {(int)ago.TotalHours}h ago"
                                 : $"Last checked: {dt:MMM d}";
        }
    }

    // ------------------------------------------------------------------
    // Theme Store (v1.0.7: downloadable theme packs)
    // ------------------------------------------------------------------

    private void OnOpenThemeStore(object sender, RoutedEventArgs e)
    {
        var store = new ThemeStoreWindow { Owner = this };
        store.ShowDialog();
        if (store.Changed) LoadThemePresetCycler();
    }

    private void OnBrowseBannerClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        ThemePresetBrowsePopup.IsOpen = false;
        OnOpenThemeStore(sender, e);
    }
}

public class DeviceStatusItem
{
    public RemoteDevice Device { get; }
    public bool IsConnected { get; }
    public DeviceStatusItem(RemoteDevice device, bool isConnected)
    {
        Device = device;
        IsConnected = isConnected;
    }
}
