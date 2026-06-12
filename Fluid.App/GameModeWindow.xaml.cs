using System.Windows;
using System.Windows.Input;
using Fluid.App.Models;
using Fluid.App.Services;

namespace Fluid.App;

public partial class GameModeWindow : Window
{
    private bool _loading = true;
    private bool _capturingHotkey;

    public GameModeWindow()
    {
        InitializeComponent();
        ThemeApplier.Apply(App.Current.Settings, Application.Current.Resources);
        Load();
        _loading = false;
    }

    private void Load()
    {
        var s = App.Current.Settings;
        EnabledCheck.IsChecked      = s.GameModeEnabled;
        HotkeyBox.Text             = s.GameModeHotkey;
        OpacitySlider.Value        = s.GameModeOpacity;
        OpacityLabel.Text          = $"{(int)(s.GameModeOpacity * 100)}%";
        ClickThroughCheck.IsChecked = s.GameModeClickThrough;
        CpuCheck.IsChecked         = s.GameModeShowCpu;
        GpuCheck.IsChecked         = s.GameModeShowGpu;
        RamCheck.IsChecked         = s.GameModeShowRam;
        NetworkCheck.IsChecked     = s.GameModeShowNetwork;
        StorageCheck.IsChecked     = s.GameModeShowStorage;
        ClockCheck.IsChecked       = s.GameModeShowDateTime;  // v1.21

        // Position
        var posRadios = new[] { PosTopLeft, PosTopCenter, PosTopRight,
                                PosLeftCenter, PosRightCenter,
                                PosBotLeft, PosBotCenter, PosBotRight };
        foreach (var rb in posRadios)
            if (rb.Tag as string == s.GameModePosition) { rb.IsChecked = true; break; }
        // v1.21: the fallback must check ALL radios. The old check only looked
        // at the top row, so any saved bottom/center position got stomped back
        // to TopRight every time the dialog opened (all 8 share a radio group).
        if (!System.Array.Exists(posRadios, rb => rb.IsChecked == true))
            PosTopRight.IsChecked = true;

        // Orientation
        switch (s.GameModeOrientation)
        {
            case "Horizontal": OrientHorizontal.IsChecked = true; break;
            case "Vertical":   OrientVertical.IsChecked   = true; break;
            default:           OrientCurrent.IsChecked    = true; break;
        }
    }

    private void OnPositionChanged(object sender, RoutedEventArgs e) { }
    private void OnOrientChanged(object sender, RoutedEventArgs e) { }

    private void OnOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_loading) return;
        OpacityLabel.Text = $"{(int)(e.NewValue * 100)}%";
    }

    // ------------------------------------------------------------------
    // Hotkey capture
    // ------------------------------------------------------------------

    private void OnHotkeyBoxFocused(object s, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        HotkeyPrompt.Text = " (press key combo…)";
    }

    private void OnHotkeyBoxUnfocused(object s, RoutedEventArgs e)
    {
        _capturingHotkey = false;
        HotkeyPrompt.Text = " (click to set)";
    }

    private void OnHotkeyCapture(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        // v1.21: Escape cancels capture -- it must never become the hotkey
        if (key == Key.Escape)
        {
            _capturingHotkey = false;
            HotkeyPrompt.Text = " (click to set)";
            Keyboard.ClearFocus();
            return;
        }
        if (key == Key.LeftCtrl || key == Key.RightCtrl ||
            key == Key.LeftShift || key == Key.RightShift ||
            key == Key.LeftAlt || key == Key.RightAlt ||
            key == Key.LWin || key == Key.RWin) return;
        // v1.21: require at least one modifier. RegisterHotKey with a bare key
        // would hijack that key system-wide (e.g. a bare Esc or letter key).
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            HotkeyPrompt.Text = " (add Ctrl/Alt/Shift…)";
            return;
        }
        var combo = HotkeyHelper.FormatCombo(Keyboard.Modifiers, key);
        HotkeyBox.Text = combo;
        _capturingHotkey = false;
        HotkeyPrompt.Text = " (click to set)";
        Keyboard.ClearFocus();
    }

    private void OnClearHotkey(object s, RoutedEventArgs e) => HotkeyBox.Text = "";

    // ------------------------------------------------------------------
    // Reset / Save
    // ------------------------------------------------------------------

    private void OnReset(object sender, RoutedEventArgs e)
    {
        var fresh = new AppSettings();
        _loading = true;
        EnabledCheck.IsChecked      = false;
        HotkeyBox.Text             = "";
        PosTopRight.IsChecked      = true;
        OpacitySlider.Value        = fresh.GameModeOpacity;
        OpacityLabel.Text          = $"{(int)(fresh.GameModeOpacity * 100)}%";
        OrientCurrent.IsChecked    = true;
        ClickThroughCheck.IsChecked = true;
        CpuCheck.IsChecked = GpuCheck.IsChecked = RamCheck.IsChecked =
            NetworkCheck.IsChecked = StorageCheck.IsChecked = true;
        ClockCheck.IsChecked = false;  // v1.21: matches AppSettings default
        _loading = false;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        var s = App.Current.Settings;
        s.GameModeEnabled      = EnabledCheck.IsChecked == true;
        s.GameModeHotkey       = HotkeyBox.Text;
        s.GameModeOpacity      = OpacitySlider.Value;
        s.GameModeClickThrough = ClickThroughCheck.IsChecked == true;
        s.GameModeShowCpu      = CpuCheck.IsChecked     == true;
        s.GameModeShowGpu      = GpuCheck.IsChecked     == true;
        s.GameModeShowRam      = RamCheck.IsChecked     == true;
        s.GameModeShowNetwork  = NetworkCheck.IsChecked == true;
        s.GameModeShowStorage  = StorageCheck.IsChecked == true;
        s.GameModeShowDateTime = ClockCheck.IsChecked   == true;  // v1.21

        s.GameModeOrientation = OrientHorizontal.IsChecked == true ? "Horizontal"
                              : OrientVertical.IsChecked   == true ? "Vertical"
                              : "Current";

        foreach (var rb in new[] { PosTopLeft, PosTopCenter, PosTopRight,
                                   PosLeftCenter, PosRightCenter,
                                   PosBotLeft, PosBotCenter, PosBotRight })
            if (rb.IsChecked == true) { s.GameModePosition = rb.Tag as string ?? "TopRight"; break; }

        SettingsService.Save(s);
        (Application.Current.MainWindow as MainWindow)?.RegisterGameModeHotkey();
        Close();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) OnSave(sender, e);
    }
}
