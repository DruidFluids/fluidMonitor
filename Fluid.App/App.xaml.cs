using System;
using System.IO;
using System.Threading;
using System.Windows;
using Fluid.App.Models;
using Fluid.App.Services;
using WinForms = System.Windows.Forms;

namespace Fluid.App;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private WinForms.NotifyIcon? _trayIcon;

    public AppSettings   Settings      { get; private set; } = new();
    public SensorState   SensorState   { get; private set; } = new();
    public PipeClient    PipeClient    { get; private set; } = new();
    public DeviceManager DeviceManager { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        // v1.20: when launched via Reset-restart, the previous process might
        // still hold the single-instance mutex for a brief moment. Wait a short
        // time before bailing -- if the lock is still held after 2 seconds,
        // something else is really wrong.
        const int singleInstanceWaitMs = 2000;
        var started = System.DateTime.UtcNow;
        bool created;
        do
        {
            _singleInstanceMutex = new Mutex(true, "Global\\fluidMonitor.app.singleinstance", out created);
            if (created) break;
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            System.Threading.Thread.Sleep(50);
        } while ((System.DateTime.UtcNow - started).TotalMilliseconds < singleInstanceWaitMs);
        if (!created) { Shutdown(); return; }

        base.OnStartup(e);

        Settings = SettingsService.Load();
        ThemeApplier.Apply(Settings, Resources);
        // Apply saved skin (if not Default which is already loaded via App.xaml)
        if (Settings.ActiveSkin != "Default")
            SkinManager.ApplySkin(Settings.ActiveSkin, Resources);

        // v1.13: seed font overrides for the widget. ApplySkin already did this
        // if a non-Default skin was loaded; otherwise we need to do it here so
        // WidgetPrimaryFont / WidgetSecondaryFont / WidgetIndicatorFont resolve.
        SkinManager.ApplyFontOverrides(Settings, Resources);

        // v1.20.3 / v1.21: seed the traffic-indicator style from the saved setting
        TrafficIndicatorState.Instance.Style = Settings.NetworkTrafficIndicator;

        SensorState.Attach(PipeClient);
        PipeClient.Start();

        CreateTrayIcon();

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new WinForms.NotifyIcon
        {
            Text    = "fluidMonitor",
            Visible = true,
        };

        // Load the .ico from the embedded WPF resource
        try
        {
            var sri = GetResourceStream(new Uri("pack://application:,,,/Assets/fluid.ico"));
            if (sri != null)
                _trayIcon.Icon = new System.Drawing.Icon(sri.Stream);
        }
        catch { /* icon just won't show — non-fatal */ }

        // Left-click tray icon → toggle show/hide
        _trayIcon.MouseClick += (_, me) =>
        {
            if (me.Button != WinForms.MouseButtons.Left) return;
            if (MainWindow?.IsVisible == true)
                MainWindow.Hide();
            else
            {
                MainWindow?.Show();
                MainWindow?.Activate();
            }
        };

        // v1.20.3: tray right-click menu stripped to essentials. Left-click
        // already toggles show/hide; the in-app Settings handles Click-through
        // and Game Mode. Right-click only exposes Settings + Exit now.
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => (MainWindow as MainWindow)?.OpenSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Shutdown());
        _trayIcon.ContextMenuStrip = menu;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { SettingsService.Save(Settings); } catch { }
        try { PipeClient.Dispose(); } catch { }
        try { DeviceManager.Dispose(); } catch { }
        if (_trayIcon != null) { _trayIcon.Visible = false; _trayIcon.Dispose(); }
        try { _singleInstanceMutex?.ReleaseMutex(); } catch { }
        base.OnExit(e);
    }

    public new static App Current => (App)Application.Current;
}
