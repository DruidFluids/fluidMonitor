using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using Fluid.App.Models;
using Fluid.App.Services;
using Fluid.Shared.Protocol;

namespace Fluid.App;

public partial class WarningsWindow : Window
{
    private readonly RemoteDevice? _device;

    public WarningsWindow(RemoteDevice? device = null)
    {
        InitializeComponent();
        _device = device;

        if (device != null)
            Title = $"fluidMonitor — Warnings ({device.Name})";

        ThemeApplier.Apply(App.Current.Settings, Application.Current.Resources);
        var warnings = device?.Popout.Warnings ?? App.Current.Settings.Warnings;
        WarnList.ItemsSource = warnings;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        SettingsService.Save(App.Current.Settings);
        // If editing a remote device, refresh its popout
        if (_device != null)
        {
            foreach (Window w in Application.Current.Windows)
            {
                if (w is RemotePopoutWindow rpw && rpw.Title.Contains(_device.Name))
                {
                    // Update ExternalWarnings reference
                    var state = App.Current.DeviceManager.GetOrCreate(_device);
                    state.ExternalWarnings = _device.Popout.Warnings;
                    break;
                }
            }
        }
        Close();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            // v1.21: Keyboard.ClearFocus() only clears KEYBOARD focus -- the
            // TextBox keeps logical focus, so its LostFocus-triggered binding
            // (Threshold, FlashColor) never pushed an in-progress edit to the
            // source and the edit was silently discarded. Push it explicitly.
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox tb)
                tb.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            OnSave(sender, e);
        }
    }
}
