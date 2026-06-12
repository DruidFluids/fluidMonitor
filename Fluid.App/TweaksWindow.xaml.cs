using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Fluid.App.Models;
using Fluid.App.Services;

namespace Fluid.App;

public partial class TweaksWindow : Window
{
    public TweaksWindow()
    {
        InitializeComponent();
        LoadSnapSettings();
    }

    // -- Existing handlers (reconstructed; originals not in zip) -----------

    private void OnRunChrisTitus(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("powershell", "-Command \"irm christitus.com/win | iex\"") { UseShellExecute = true, Verb = "runas" }); }
        catch { /* user cancelled UAC or powershell not found */ }
    }

    private void OnRunMassgrave(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("powershell", "-Command \"irm https://get.activated.win | iex\"") { UseShellExecute = true, Verb = "runas" }); }
        catch { /* user cancelled UAC or powershell not found */ }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    // -- v1.25.37: Snap-to-windows sub-options ----------------------------

    private void LoadSnapSettings()
    {
        var s = App.Current.Settings;
        BlocklistBox.Text = string.Join(Environment.NewLine, s.SnapBlocklist ?? new());
    }

    private void OnSaveBlocklist(object sender, RoutedEventArgs e)
    {
        var lines = BlocklistBox.Text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => !string.IsNullOrEmpty(l))
            .ToList();
        App.Current.Settings.SnapBlocklist = lines;
        SettingsService.Save(App.Current.Settings);
        BlocklistStatus.Text = $"Saved ({lines.Count} rule{(lines.Count == 1 ? "" : "s")})";
    }

    private void OnPickWindow(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new WindowPickerDialog { Owner = this };
            if (picker.ShowDialog() == true && !string.IsNullOrEmpty(picker.SelectedTitle))
            {
                if (!string.IsNullOrWhiteSpace(BlocklistBox.Text))
                    BlocklistBox.Text += Environment.NewLine;
                BlocklistBox.Text += picker.SelectedTitle;
                BlocklistStatus.Text = "Window added (click Save)";
            }
        }
        catch (Exception ex)
        {
            BlocklistStatus.Text = $"Error: {ex.Message}";
        }
    }
}
