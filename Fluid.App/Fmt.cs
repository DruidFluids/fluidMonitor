using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Fluid.App.Models;

namespace Fluid.App;

/// <summary>
/// Attached property for TextBlock that parses [a]...[/a] tags and renders
/// the tagged segments in AccentBrush (blue). Everything else inherits
/// the TextBlock's normal Foreground.
///
/// Does NOT manage Visibility — let the layout or Style handle that.
/// </summary>
public static class Fmt
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(Fmt),
            new PropertyMetadata(null, OnTextChanged));

    public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);
    public static void SetText(DependencyObject o, string v) => o.SetValue(TextProperty, v);

    // v1.21: AccentOverride as a bindable attached property. Previously the
    // override (warning gradient hex) was only read from the DataContext when
    // the TEXT changed -- so a gradient color change with an unchanged value
    // string (steady temp) never rendered, and was otherwise one cycle behind.
    // Binding this property makes override changes re-render immediately.
    public static readonly DependencyProperty AccentOverrideProperty =
        DependencyProperty.RegisterAttached(
            "AccentOverride", typeof(string), typeof(Fmt),
            new PropertyMetadata(null, OnAccentOverrideChanged));

    public static string? GetAccentOverride(DependencyObject o) => (string?)o.GetValue(AccentOverrideProperty);
    public static void SetAccentOverride(DependencyObject o, string? v) => o.SetValue(AccentOverrideProperty, v);

    // v1.25.16: per-TextBlock scale for [a]...[/a] accent segments. Used by
    // Network/Disk tiles to shrink the "KB/s" unit so it doesn't visually
    // dominate the number; RAM/CPU/clock keep the default 1.0 because their
    // units (GB, MHz, %, °C) are short and look right at full size.
    public static readonly DependencyProperty AccentScaleProperty =
        DependencyProperty.RegisterAttached(
            "AccentScale", typeof(double), typeof(Fmt),
            new PropertyMetadata(1.0));

    public static double GetAccentScale(DependencyObject o) => (double)o.GetValue(AccentScaleProperty);
    public static void SetAccentScale(DependencyObject o, double v) => o.SetValue(AccentScaleProperty, v);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb)
        {
            TrackTextBlock(tb);
            Render(tb, e.NewValue as string);
        }
    }

    private static void OnAccentOverrideChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextBlock tb) Render(tb, GetText(tb));
    }

    // v1.25.37: track all Fmt-bound TextBlocks so we can force a re-render
    // after skin/theme changes (closes the stale-Run-FontSize gap between
    // a skin change and the next sensor tick).
    private static readonly List<WeakReference<TextBlock>> _tracked = new();
    private static void TrackTextBlock(TextBlock tb)
    {
        // Deduplicate (cheap linear scan; there are only ~12 tile TextBlocks)
        foreach (var wr in _tracked)
            if (wr.TryGetTarget(out var existing) && existing == tb) return;
        _tracked.Add(new WeakReference<TextBlock>(tb));
    }

    /// <summary>
    /// Force every Fmt-bound TextBlock to re-render with current resource
    /// values. Call after skin/theme changes to eliminate the stale-font
    /// flash that would otherwise persist until the next sensor tick.
    /// </summary>
    public static void InvalidateAll()
    {
        for (int i = _tracked.Count - 1; i >= 0; i--)
        {
            if (_tracked[i].TryGetTarget(out var tb))
                Render(tb, GetText(tb));
            else
                _tracked.RemoveAt(i);
        }
    }

    /// <summary>
    /// Invalidate now AND schedule a second pass after the current layout
    /// cycle completes. Catches cases where resources haven't fully settled
    /// when the first InvalidateAll fires (skin XAML merge is async).
    /// </summary>
    public static void InvalidateAllDeferred()
    {
        InvalidateAll();
        Application.Current?.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(InvalidateAll));
    }

    private static void Render(TextBlock tb, string? raw)
    {
        tb.Inlines.Clear();
        if (string.IsNullOrEmpty(raw)) return;

        // Prefer the bound attached property; fall back to DataContext for
        // any usage that doesn't bind it.
        var overrideHex = GetAccentOverride(tb)
                          ?? (tb.DataContext as TileData)?.AccentOverride;

        int pos = 0;
        while (pos < raw.Length)
        {
            int tagStart = raw.IndexOf("[a]", pos, StringComparison.Ordinal);
            if (tagStart < 0)
            {
                AddSegment(tb, raw[pos..], false, null);
                break;
            }
            if (tagStart > pos)
                AddSegment(tb, raw[pos..tagStart], false, null);

            int tagEnd = raw.IndexOf("[/a]", tagStart + 3, StringComparison.Ordinal);
            if (tagEnd < 0)
            {
                AddSegment(tb, raw[(tagStart + 3)..], true, overrideHex);
                break;
            }
            AddSegment(tb, raw[(tagStart + 3)..tagEnd], true, overrideHex);
            pos = tagEnd + 4;
        }
    }

    private static void AddSegment(TextBlock tb, string text, bool isAccent, string? overrideHex)
    {
        if (text.Length == 0) return;
        // v1.25.16: read the scale once per call; only accent runs apply it.
        double scale = isAccent ? GetAccentScale(tb) : 1.0;
        var parts = text.Split('\n');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) tb.Inlines.Add(new LineBreak());
            if (parts[i].Length > 0)
            {
                var run = new Run(parts[i]);
                if (overrideHex != null)
                {
                    try { run.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(overrideHex)); }
                    catch { run.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush"); }
                }
                else if (isAccent)
                {
                    run.SetResourceReference(TextElement.ForegroundProperty, "AccentBrush");
                }

                // v1.25.37: add to tree first, then set properties
                tb.Inlines.Add(run);

                if (isAccent && scale != 1.0)
                {
                    run.FontFamily = new FontFamily("Segoe UI");
                    run.FontWeight = FontWeights.SemiBold;
                    // Read UnitFontSize directly from resources at render
                    // time. Explicit value, not a resource reference — avoids
                    // the detached-Run resolution problem entirely. Stale
                    // values between skin changes and the next sensor tick
                    // are handled by InvalidateAllFmtText() calls in the
                    // dice handlers.
                    if (Application.Current.Resources["UnitFontSize"] is double unitSize && unitSize > 0)
                        run.FontSize = unitSize;
                }
            }
        }
    }
}
