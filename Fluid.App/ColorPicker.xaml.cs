using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Fluid.App;

public partial class ColorPicker : UserControl
{
    public event Action<Color>? ColorApplied;
    public event Action? Cancelled;

    private double _hue = 200;
    private double _sat = 0.8;
    private double _val = 0.9;
    private byte   _alpha = 255;
    private bool   _updating;

    public ColorPicker()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateAll();
    }

    public void SetColor(Color c)
    {
        _alpha = c.A;
        RgbToHsv(c.R, c.G, c.B, out _hue, out _sat, out _val);
        AlphaSlider.Value = _alpha;
        HueSlider.Value   = _hue;
        UpdateAll();
    }

    // ------------------------------------------------------------------
    // SV square interaction
    // ------------------------------------------------------------------

    private bool _svDragging;

    private void SvCanvas_MouseDown(object s, MouseButtonEventArgs e)
    {
        _svDragging = true;
        SvCanvas.CaptureMouse();
        UpdateSvFromMouse(e.GetPosition(SvCanvas));
    }

    private void SvCanvas_MouseMove(object s, MouseEventArgs e)
    {
        if (!_svDragging) return;
        UpdateSvFromMouse(e.GetPosition(SvCanvas));
    }

    private void SvCanvas_MouseUp(object s, MouseButtonEventArgs e)
    {
        _svDragging = false;
        SvCanvas.ReleaseMouseCapture();
    }

    private void UpdateSvFromMouse(Point p)
    {
        var w = SvCanvas.ActualWidth;
        var h = SvCanvas.ActualHeight;
        _sat = Math.Clamp(p.X / w, 0, 1);
        _val = Math.Clamp(1.0 - p.Y / h, 0, 1);
        UpdateAll();
    }

    // ------------------------------------------------------------------
    // Sliders
    // ------------------------------------------------------------------

    private void HueSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        _hue = e.NewValue;
        UpdateAll();
    }

    private void AlphaSlider_ValueChanged(object s, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating) return;
        _alpha = (byte)e.NewValue;
        UpdateAll();
    }

    // ------------------------------------------------------------------
    // Hex box
    // ------------------------------------------------------------------

    private void HexBox_TextChanged(object s, TextChangedEventArgs e) { }

    private void HexBox_LostFocus(object s, RoutedEventArgs e)
    {
        if (_updating) return;
        var text = HexBox.Text.Trim().TrimStart('#');
        try
        {
            var color = (Color)ColorConverter.ConvertFromString("#" + text);
            _alpha = color.A;
            RgbToHsv(color.R, color.G, color.B, out _hue, out _sat, out _val);
            _updating = true;
            HueSlider.Value   = _hue;
            AlphaSlider.Value = _alpha;
            _updating = false;
            UpdateAll();
        }
        catch { /* invalid hex — ignore */ }
    }

    // ------------------------------------------------------------------
    // Update everything from current HSV + alpha
    // ------------------------------------------------------------------

    private void UpdateAll()
    {
        _updating = true;

        var hueColor = HsvToRgb(_hue, 1, 1);
        SvBase.Fill = new SolidColorBrush(hueColor);

        var finalColor = HsvToRgb(_hue, _sat, _val);
        finalColor.A = _alpha;

        // Cursor position
        if (SvCanvas.ActualWidth > 0)
        {
            Canvas.SetLeft(SvCursor, _sat * SvCanvas.ActualWidth - 6);
            Canvas.SetTop (SvCursor, (1 - _val) * SvCanvas.ActualHeight - 6);
        }

        // Preview
        PreviewSwatch.Background = new SolidColorBrush(finalColor);

        // Hex box
        HexBox.Text = $"#{finalColor.A:X2}{finalColor.R:X2}{finalColor.G:X2}{finalColor.B:X2}";

        // Sliders (only if not triggered by them)
        if (Math.Abs(HueSlider.Value - _hue) > 0.1)   HueSlider.Value   = _hue;
        if (Math.Abs(AlphaSlider.Value - _alpha) > 0.5) AlphaSlider.Value = _alpha;

        _updating = false;
    }

    // ------------------------------------------------------------------
    // Buttons
    // ------------------------------------------------------------------

    private void OnPreset(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string hex) return;
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            _alpha = c.A;
            RgbToHsv(c.R, c.G, c.B, out _hue, out _sat, out _val);
            _updating = true;
            HueSlider.Value   = _hue;
            AlphaSlider.Value = _alpha;
            _updating = false;
            UpdateAll();
            // Apply immediately without requiring the user to click Apply
            var result = HsvToRgb(_hue, _sat, _val);
            result.A = _alpha;
            ColorApplied?.Invoke(result);
        }
        catch { }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Set preset button backgrounds from their Tag values
        SetPresetBackgrounds();
    }

    private void SetPresetBackgrounds()
    {
        foreach (var child in PresetPanel.Children)
        {
            if (child is Button btn && btn.Tag is string hex)
            {
                try { btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
                catch { }
            }
        }
    }

    private void OnApply(object s, RoutedEventArgs e)
    {
        var c = HsvToRgb(_hue, _sat, _val);
        c.A = _alpha;
        ColorApplied?.Invoke(c);
    }

    private void OnCancel(object s, RoutedEventArgs e) => Cancelled?.Invoke();

    // ------------------------------------------------------------------
    // HSV ↔ RGB
    // ------------------------------------------------------------------

    private static Color HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        var c = v * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = v - c;
        double r = 0, g = 0, b = 0;
        if      (h < 60)  { r = c; g = x; }
        else if (h < 120) { r = x; g = c; }
        else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; }
        else if (h < 300) { r = x; b = c; }
        else              { r = c; b = x; }
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    private static void RgbToHsv(byte r, byte g, byte b,
        out double h, out double s, out double v)
    {
        var rf = r / 255.0; var gf = g / 255.0; var bf = b / 255.0;
        var max = Math.Max(rf, Math.Max(gf, bf));
        var min = Math.Min(rf, Math.Min(gf, bf));
        var d   = max - min;
        v = max;
        s = max == 0 ? 0 : d / max;
        if (d == 0) { h = 0; return; }
        if      (max == rf) h = 60 * (((gf - bf) / d) % 6);
        else if (max == gf) h = 60 * ((bf - rf) / d + 2);
        else                h = 60 * ((rf - gf) / d + 4);
        if (h < 0) h += 360;
    }
}
