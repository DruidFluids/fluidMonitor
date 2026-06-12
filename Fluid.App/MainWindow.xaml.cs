using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Fluid.App.Models;
using Fluid.App.Services;
using Fluid.Shared.Protocol;
namespace Fluid.App;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<TileData> _visibleTiles = new();
    private SensorState _activeState = null!;
    private Guid? _activeDeviceId; // null = local

    // ---- Manual drag state ----
    private bool _isDragging;
    private Point _dragMouseStartDip;   // cursor in DIP screen coords at drag start
    private Point _dragWindowStart;     // window Left/Top in DIPs

    private const double SnapThreshold  = 18.0;
    private const double ClickThreshold = 5.0;

    // v1.25.37: cached rects of other visible windows, collected once per
    // drag in OnDragMouseDown. Avoids per-mousemove EnumWindows cost.
    private System.Collections.Generic.List<Rect> _snapTargetRects = new();

    public MainWindow()
    {
        // v1.25.37: global crash logging to crash.log next to the executable.
        // Catches both UI-thread and background-thread unhandled exceptions.
        var logPath = System.IO.Path.Combine(AppContext.BaseDirectory, "crash.log");
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try { System.IO.File.AppendAllText(logPath,
                $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] UNHANDLED ({e.ExceptionObject.GetType().Name})\n{e.ExceptionObject}\n"); }
            catch { }
        };
        Application.Current.DispatcherUnhandledException += (s, e) =>
        {
            try { System.IO.File.AppendAllText(logPath,
                $"\n[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] DISPATCHER ({e.Exception.GetType().Name})\n{e.Exception}\n"); }
            catch { }
            e.Handled = true;
            // v1.25.59: force visual recovery — the rendering pipeline can
            // freeze if a resource-change exception is swallowed silently.
            try
            {
                Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Render,
                    new Action(() => {
                        foreach (Window w in Application.Current.Windows)
                            w.InvalidateVisual();
                    }));
            }
            catch { }
        };

        InitializeComponent();

        _activeState = App.Current.SensorState;

        var settings = App.Current.Settings;

        Left = settings.WindowLeft;
        Top  = settings.WindowTop;

        // v1.21.1: position fixup after first layout (SizeToContent means the
        // real size isn't known until then). Fresh install -> center on the
        // primary monitor. Restored position entirely off-screen (stale
        // settings.json from a different monitor layout, bad coords from a
        // crash, etc.) -> rescue to center instead of opening invisibly.
        Loaded += OnLoadedPositionFixup;

        Topmost = settings.AlwaysOnTop;
        Opacity = settings.Opacity;

        DataContext = _visibleTiles;
        RebuildDeviceSwitcher();
        RebuildVisibleTiles();
        ApplyOrientation();

        settings.PropertyChanged += OnSettingsChanged;

        // Apply initial UI scale
        ApplyUiScale(settings.UiScale);

        // v1.20: Anchor-aware size growth. When the widget's content changes size
        // (skin swap with different border thickness, tile width slider, etc.),
        // WPF's SizeToContent=WidthAndHeight grows the window from top-left by
        // default -- which pushes the widget into the screen if it was snapped
        // to the right or bottom edge, OR pulls it away from that edge. Snap
        // users hate this. Fix: on each size change, adjust Left/Top to keep
        // the snapped edge stable. If not snapped, anchor on the center so
        // growth is symmetrical.
        SizeChanged += OnSizeChangedAnchorAware;
    }

    // v1.20: previous window rect (just-before-size-change) so we can compute
    // a delta and decide how to shift Left/Top so the anchor stays put.
    private double _prevWidth  = 0;
    private double _prevHeight = 0;
    private void OnSizeChangedAnchorAware(object sender, SizeChangedEventArgs e)
    {
        // First call after init: just record sizes, no shift yet.
        if (_prevWidth == 0 && _prevHeight == 0)
        {
            _prevWidth  = e.NewSize.Width;
            _prevHeight = e.NewSize.Height;
            return;
        }
        var dw = e.NewSize.Width  - _prevWidth;
        var dh = e.NewSize.Height - _prevHeight;
        _prevWidth  = e.NewSize.Width;
        _prevHeight = e.NewSize.Height;
        if (Math.Abs(dw) < 0.5 && Math.Abs(dh) < 0.5) return;

        // Determine snap edges, if any. We look at where the OLD bounds sat
        // relative to the work area to decide which edge to keep stable.
        var work = GetCurrentMonitorWorkArea();
        if (work.IsEmpty)
        {
            // Fallback: shift toward center
            Left -= dw / 2;
            Top  -= dh / 2;
            return;
        }

        var oldRight  = Left + (_prevWidth  - dw); // pre-resize right edge
        var oldBottom = Top  + (_prevHeight - dh); // pre-resize bottom edge
        const double snapTol = 4.0;

        bool atLeft   = Math.Abs(Left - work.Left)    < snapTol;
        bool atRight  = Math.Abs(work.Right  - oldRight)  < snapTol;
        bool atTop    = Math.Abs(Top - work.Top)      < snapTol;
        bool atBottom = Math.Abs(work.Bottom - oldBottom) < snapTol;

        // Horizontal compensation
        if (atRight && !atLeft)                Left -= dw;          // right-anchored: grow leftward
        else if (!atLeft && !atRight)          Left -= dw / 2;      // floating: grow around center
        // else atLeft (or both): Left stays anchored, growth happens rightward

        // Vertical compensation
        if (atBottom && !atTop)                Top -= dh;           // bottom-anchored: grow upward
        else if (!atTop && !atBottom)          Top -= dh / 2;       // floating: grow around center
        // else atTop (or both): Top stays anchored, growth happens downward
    }

    private void ApplyUiScale(double scale)
    {
        RootBorder.LayoutTransform = new ScaleTransform(scale, scale);
    }

    // ------------------------------------------------------------------
    // v1.21.1: startup position fixup
    // ------------------------------------------------------------------

    private void OnLoadedPositionFixup(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoadedPositionFixup;
        if (SettingsService.LastLoadWasFresh)
        {
            CenterOnPrimary();
            return;
        }
        EnsureOnScreen();
    }

    /// <summary>Center the widget in the primary monitor's work area.</summary>
    private void CenterOnPrimary()
    {
        UpdateLayout();
        var work = SystemParameters.WorkArea; // primary monitor, DIPs
        var w = ActualWidth  > 0 ? ActualWidth  : 140;
        var h = ActualHeight > 0 ? ActualHeight : 300;
        Left = work.Left + (work.Width  - w) / 2;
        Top  = work.Top  + (work.Height - h) / 2;
        // OnLocationChanged records the new position into Settings, so the
        // centered coords persist on exit.
    }

    /// <summary>
    /// If the restored window rect has no meaningful overlap with the virtual
    /// screen (all monitors combined), center on primary. Requires at least a
    /// 40x40 visible sliver so the widget is actually grabbable.
    /// </summary>
    private void EnsureOnScreen()
    {
        UpdateLayout();
        var w = ActualWidth  > 0 ? ActualWidth  : 140;
        var h = ActualHeight > 0 ? ActualHeight : 300;
        var virtualScreen = new Rect(
            SystemParameters.VirtualScreenLeft,  SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var win = new Rect(Left, Top, w, h);
        win.Intersect(virtualScreen);
        if (win.IsEmpty || win.Width < 40 || win.Height < 40)
            CenterOnPrimary();
    }

    // ------------------------------------------------------------------
    // DPI-safe coordinate helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the cursor position in WPF DIPs (not physical pixels).
    /// PointToScreen returns physical pixels; this converts them back via
    /// the device transform so Left/Top deltas are correct at any DPI.
    /// </summary>
    private Point CursorPositionDip(MouseEventArgs e)
    {
        var screenPx = PointToScreen(e.GetPosition(this));
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
            return src.CompositionTarget.TransformFromDevice.Transform(screenPx);
        return screenPx;
    }

    // ------------------------------------------------------------------
    // Settings observer
    // ------------------------------------------------------------------

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        var s = App.Current.Settings;
        switch (e.PropertyName)
        {
            case nameof(AppSettings.ShowCpu):
            case nameof(AppSettings.ShowGpu):
            case nameof(AppSettings.ShowRam):
            case nameof(AppSettings.ShowNetwork):
            case nameof(AppSettings.ShowStorage):
            case nameof(AppSettings.ShowDateTime):
                RebuildVisibleTiles();
                break;            case nameof(AppSettings.Orientation):
                ApplyOrientation();
                break;
            case nameof(AppSettings.AlwaysOnTop):
                Topmost = s.AlwaysOnTop;
                break;
            case nameof(AppSettings.SnapToEdges):
                break;
            case nameof(AppSettings.Opacity):
                Opacity = s.Opacity;
                break;
            case nameof(AppSettings.UiScale):
                ApplyUiScale(s.UiScale);
                break;
            case nameof(AppSettings.TileWidth):
                ThemeApplier.Apply(s, System.Windows.Application.Current.Resources);
                break;
            case nameof(AppSettings.BackgroundColor):
            case nameof(AppSettings.TileColor):
            case nameof(AppSettings.AccentColor):
            case nameof(AppSettings.TextColor):
            case nameof(AppSettings.MutedTextColor):
                ThemeApplier.Apply(s, System.Windows.Application.Current.Resources);
                break;
        }
    }

    // v1.25: the CPU tile's "Turn on temp" affordance was clicked. Open the
    // opt-in dialog; when it closes, force a refresh so the tile reflects the
    // new driver state (the service re-check inside the dialog already nudged
    // sensor enumeration, but rebuilding ensures the hint clears immediately).
    private void OnTempHintClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new CpuTempDialog { Owner = this };
            dlg.ShowDialog();
            if (dlg.DriverNowInstalled)
            {
                var s = App.Current.Settings;
                if (s.CpuTempHintDismissed)
                {
                    s.CpuTempHintDismissed = false;
                    s.CpuTempDismissChoice = "";
                    SettingsService.Save(s);
                }
            }
        }
        catch (Exception ex)
        {
            // v1.25.37: catch dialog crashes (reported: close/cancel/X crashes the app)
            System.Diagnostics.Debug.WriteLine($"CpuTempDialog error: {ex}");
        }
        RebuildVisibleTiles();
    }

    // v1.25.1: small "x" next to the pill was clicked. Prompt for what they
    // want the CPU tile to look like in the dismissed state, then persist.
    // The user can reverse this any time from Settings -> Sensors.
    private void OnTempHintDismiss(object sender, RoutedEventArgs e)
    {
        try
        {
            var dlg = new CpuTempDismissDialog { Owner = this };
            dlg.ShowDialog();
            if (dlg.DialogResult != true || string.IsNullOrEmpty(dlg.Choice)) return;

            var settings = App.Current.Settings;
            settings.CpuTempHintDismissed = true;
            settings.CpuTempDismissChoice = dlg.Choice;
            if (dlg.Choice == "HideTile") settings.ShowCpu = false;
            SettingsService.Save(settings);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CpuTempDismissDialog error: {ex}");
        }
        RebuildVisibleTiles();
    }

    public void RebuildVisibleTiles()
    {
        var s     = App.Current.Settings;
        var state = _activeState;

        // Per-device or local enable flags
        bool showCpu, showGpu, showRam, showNet, showDisk;
        bool showDateTime;
        if (_activeDeviceId.HasValue)
        {
            var dev = s.RemoteDevices.Find(d => d.Id == _activeDeviceId.Value);
            if (dev == null) { SwitchToLocal(); return; }
            showCpu = dev.Popout.ShowCpu; showGpu = dev.Popout.ShowGpu; showRam = dev.Popout.ShowRam;
            showNet = dev.Popout.ShowNetwork; showDisk = dev.Popout.ShowStorage;
            // v1.18: Clock tile is local-only, never shown on remote popouts.
            // The user's TileOrder still applies to the other 5 kinds though.
            showDateTime = false;
        }
        else if (_gameModeActive)
        {
            // v1.21: game mode no longer mutates the persisted Show* settings
            // (an app exit during game mode used to save the game-mode flags
            // as the user's normal tile config). The override happens here,
            // at render time only.
            showCpu  = s.GameModeShowCpu;     showGpu  = s.GameModeShowGpu;
            showRam  = s.GameModeShowRam;     showNet  = s.GameModeShowNetwork;
            showDisk = s.GameModeShowStorage; showDateTime = s.GameModeShowDateTime;
        }
        else
        {
            showCpu = s.ShowCpu; showGpu = s.ShowGpu; showRam = s.ShowRam;
            showNet = s.ShowNetwork; showDisk = s.ShowStorage;
            showDateTime = s.ShowDateTime;
        }

        // v1.18: walk the user's TileOrder list instead of a hardcoded array.
        // SettingsService.Load normalizes the list so unknown/missing kinds
        // are handled there -- we can trust the order here.
        _visibleTiles.Clear();
        foreach (var kindName in s.TileOrder)
        {
            if (!System.Enum.TryParse<Fluid.Shared.Protocol.TileKind>(kindName, out var kind)) continue;
            switch (kind)
            {
                case Fluid.Shared.Protocol.TileKind.Cpu:      if (showCpu)      _visibleTiles.Add(state.CpuTile);      break;
                case Fluid.Shared.Protocol.TileKind.Gpu:      if (showGpu)      _visibleTiles.Add(state.GpuTile);      break;
                case Fluid.Shared.Protocol.TileKind.Ram:      if (showRam)      _visibleTiles.Add(state.RamTile);      break;
                case Fluid.Shared.Protocol.TileKind.Network:  if (showNet)      _visibleTiles.Add(state.NetworkTile);  break;
                case Fluid.Shared.Protocol.TileKind.Storage:  if (showDisk)     _visibleTiles.Add(state.StorageTile);  break;
                case Fluid.Shared.Protocol.TileKind.DateTime: if (showDateTime) _visibleTiles.Add(state.DateTimeTile); break;
            }
        }
    }

    // ------------------------------------------------------------------
    // Device switcher
    // ------------------------------------------------------------------

    public void RebuildDeviceSwitcher()
    {
        var devices = App.Current.Settings.RemoteDevices;
        DeviceSwitcher.Visibility = devices.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;

        // Remove all device buttons (keep LOCAL which is always first child)
        while (DeviceSwitcher.Children.Count > 1)
            DeviceSwitcher.Children.RemoveAt(1);

        // Add one button per device
        foreach (var dev in devices)
        {
            var btn = new Button
            {
                Content = dev.Name.ToUpperInvariant(),
                Tag     = dev.Id.ToString(),
                Style   = FindResource(_activeDeviceId == dev.Id
                              ? "DeviceSwitchBtnActive" : "DeviceSwitchBtn") as Style
            };
            btn.Click += OnSwitchDevice;
            DeviceSwitcher.Children.Add(btn);
        }

        // Update LOCAL button style
        LocalBtn.Style = FindResource(_activeDeviceId == null
            ? "DeviceSwitchBtnActive" : "DeviceSwitchBtn") as Style;
    }

    private void OnSwitchDevice(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        var tag = btn.Tag as string;

        if (tag == "local" || string.IsNullOrEmpty(tag))
            SwitchToLocal();
        else if (Guid.TryParse(tag, out var id))
            SwitchToDevice(id);
    }

    public void SwitchToLocal()
    {
        _activeDeviceId = null;
        _activeState    = App.Current.SensorState;
        RebuildDeviceSwitcher();
        RebuildVisibleTiles();
        ApplyOrientation();
    }

    public void SwitchToDevice(Guid deviceId)
    {
        var dev = App.Current.Settings.RemoteDevices.Find(d => d.Id == deviceId);
        if (dev == null) return;

        _activeDeviceId = deviceId;
        _activeState    = App.Current.DeviceManager.GetOrCreate(dev);
        RebuildDeviceSwitcher();
        RebuildVisibleTiles();
        ApplyOrientation();
    }

    /// <summary>Called from SettingsWindow when devices are added/removed.</summary>
    public void RefreshDeviceSwitcher() => RebuildDeviceSwitcher();

    private void ApplyOrientation()
    {
        var s = App.Current.Settings;
        // v1.21: game mode overrides orientation at render time instead of
        // mutating the persisted setting.
        var effective = (_gameModeActive && _gameModeOrientation.HasValue)
            ? _gameModeOrientation.Value
            : s.Orientation;
        var orientation = effective == LayoutOrientation.Horizontal
            ? System.Windows.Controls.Orientation.Horizontal
            : System.Windows.Controls.Orientation.Vertical;

        var template = new ItemsPanelTemplate();
        var sp = new FrameworkElementFactory(typeof(StackPanel));
        sp.SetValue(StackPanel.OrientationProperty, orientation);
        template.VisualTree = sp;
        TilesItems.ItemsPanel = template;

        // v1.25.37: reduce header bar height in horizontal mode
        // (full-width widget means the header adds noticeable wasted space)
        HeaderGrid.Margin = orientation == System.Windows.Controls.Orientation.Horizontal
            ? new Thickness(2, 0, 2, 0)
            : new Thickness(2, 2, 2, 2);
    }

    // ------------------------------------------------------------------
    // Drag + snap + click-to-open-TaskManager
    // ------------------------------------------------------------------

    private void OnDragMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (sender is not IInputElement el) return;

        _isDragging = true;
        _dragMouseStartDip = CursorPositionDip(e);
        _dragWindowStart   = new Point(Left, Top);

        // v1.25.37: snapshot all visible window rects once per drag.
        _snapTargetRects = App.Current.Settings.SnapToWindows
            ? CollectSnapTargetRects()
            : new System.Collections.Generic.List<Rect>();

        el.CaptureMouse();
        e.Handled = true;
    }

    private void OnDragMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var cur = CursorPositionDip(e);
        Left = _dragWindowStart.X + (cur.X - _dragMouseStartDip.X);
        Top  = _dragWindowStart.Y + (cur.Y - _dragMouseStartDip.Y);
    }

    private void OnDragMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        if (sender is IInputElement el) el.ReleaseMouseCapture();

        var cur = CursorPositionDip(e);
        var dx = cur.X - _dragMouseStartDip.X;
        var dy = cur.Y - _dragMouseStartDip.Y;
        var dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist < ClickThreshold)
        {
            // Single click — do nothing (Task Manager requires double-click)
        }
        else if (App.Current.Settings.SnapToEdges)
        {
            ApplyEdgeSnap();
        }
    }

    private void ApplyEdgeSnap()
    {
        var work = GetCurrentMonitorWorkArea();
        if (work.IsEmpty) return;

        var w = ActualWidth;
        var h = ActualHeight;

        // v1.25.37: enhanced snap - edges flush + corner alignment.
        // Phase 1: find the closest horizontal and vertical edge snaps.
        // Phase 2: if we snapped horizontally to a window, also align
        //          vertically to the nearest corner of that same window
        //          (and vice versa). This makes the widget dock neatly
        //          against corners, not just touch edges.
        double bestDx = double.MaxValue, bestDy = double.MaxValue;
        double snapX = Left, snapY = Top;
        Rect bestHTarget = Rect.Empty, bestVTarget = Rect.Empty;

        foreach (var r in _snapTargetRects)
        {
            // Horizontal snaps (widget docks beside target)
            double d = Math.Abs((Left + w) - r.Left);
            if (d < SnapThreshold && d < bestDx) { bestDx = d; snapX = r.Left - w; bestHTarget = r; }
            d = Math.Abs(Left - r.Right);
            if (d < SnapThreshold && d < bestDx) { bestDx = d; snapX = r.Right; bestHTarget = r; }

            // Vertical snaps (widget docks above/below target)
            d = Math.Abs((Top + h) - r.Top);
            if (d < SnapThreshold && d < bestDy) { bestDy = d; snapY = r.Top - h; bestVTarget = r; }
            d = Math.Abs(Top - r.Bottom);
            if (d < SnapThreshold && d < bestDy) { bestDy = d; snapY = r.Bottom; bestVTarget = r; }
        }

        // Apply primary snaps
        if (bestDx < SnapThreshold) Left = snapX;
        if (bestDy < SnapThreshold) Top  = snapY;

        // Phase 2: corner alignment on the secondary axis.
        // If we snapped horizontally, try to align top-to-top or bottom-to-bottom
        // with the target window (whichever corner is closer).
        if (bestDx < SnapThreshold && !bestHTarget.IsEmpty && bestDy >= SnapThreshold)
        {
            double dTop = Math.Abs(Top - bestHTarget.Top);
            double dBot = Math.Abs((Top + h) - bestHTarget.Bottom);
            if (dTop < SnapThreshold * 2) Top = bestHTarget.Top;
            else if (dBot < SnapThreshold * 2) Top = bestHTarget.Bottom - h;
        }
        // If we snapped vertically, try to align left-to-left or right-to-right.
        if (bestDy < SnapThreshold && !bestVTarget.IsEmpty && bestDx >= SnapThreshold)
        {
            double dLeft = Math.Abs(Left - bestVTarget.Left);
            double dRight = Math.Abs((Left + w) - bestVTarget.Right);
            if (dLeft < SnapThreshold * 2) Left = bestVTarget.Left;
            else if (dRight < SnapThreshold * 2) Left = bestVTarget.Right - w;
        }

        // Screen-edge fallback (only on axes without a window snap)
        if (bestDx >= SnapThreshold)
        {
            if (Math.Abs(Left - work.Left) < SnapThreshold) Left = work.Left;
            else if (Math.Abs(work.Right - (Left + w)) < SnapThreshold) Left = work.Right - w;
        }
        if (bestDy >= SnapThreshold)
        {
            if (Math.Abs(Top - work.Top) < SnapThreshold) Top = work.Top;
            else if (Math.Abs(work.Bottom - (Top + h)) < SnapThreshold) Top = work.Bottom - h;
        }
    }

    /// <summary>
    /// Enumerate all visible, non-minimized, non-cloaked top-level windows
    /// (excluding ourselves), convert to DIP rects, and filter by the
    /// user's blocklist. Called once per drag start.
    /// </summary>
    private System.Collections.Generic.List<Rect> CollectSnapTargetRects()
    {
        var results = new System.Collections.Generic.List<Rect>();
        var myHwnd = new WindowInteropHelper(this).Handle;
        var blocklist = App.Current.Settings.SnapBlocklist ?? new System.Collections.Generic.List<string>();

        // DIP transform: physical pixels -> device-independent pixels
        Matrix toDip = Matrix.Identity;
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
            toDip = src.CompositionTarget.TransformFromDevice;

        var sb = new System.Text.StringBuilder(256);
        EnumWindows((hwnd, _) =>
        {
            if (hwnd == myHwnd) return true;
            if (!IsWindowVisible(hwnd)) return true;
            if (IsIconic(hwnd)) return true;

            // Skip UWP cloaked windows (virtual desktops, hidden Store apps)
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                return true;

            if (!GetWindowRect(hwnd, out RECT rc)) return true;
            int rw = rc.right - rc.left, rh = rc.bottom - rc.top;
            if (rw <= 0 || rh <= 0) return true;

            // Blocklist check (substring, case-insensitive)
            if (blocklist.Count > 0)
            {
                sb.Clear();
                GetWindowText(hwnd, sb, sb.Capacity);
                var title = sb.ToString();
                foreach (var b in blocklist)
                    if (!string.IsNullOrEmpty(b) && title.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
            }

            var tl = toDip.Transform(new Point(rc.left, rc.top));
            var br = toDip.Transform(new Point(rc.right, rc.bottom));
            results.Add(new Rect(tl, br));
            return true;
        }, IntPtr.Zero);

        return results;
    }

    // ------------------------------------------------------------------
    // Multi-monitor work-area
    // ------------------------------------------------------------------

    private Rect GetCurrentMonitorWorkArea()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return SystemParameters.WorkArea;

        var hMon = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
        if (hMon == IntPtr.Zero) return SystemParameters.WorkArea;

        var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref mi)) return SystemParameters.WorkArea;

        Matrix t = Matrix.Identity;
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
            t = src.CompositionTarget.TransformFromDevice;

        var tl = t.Transform(new Point(mi.rcWork.left, mi.rcWork.top));
        var br = t.Transform(new Point(mi.rcWork.right, mi.rcWork.bottom));
        return new Rect(tl, br);
    }

    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO lpmi);
    const uint MONITOR_DEFAULTTONEAREST = 2;
    [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct MONITORINFO { public uint cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    // v1.25.37: window-to-window snap support
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] private static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
    private const int DWMWA_CLOAKED = 14;

    // ------------------------------------------------------------------
    // Misc
    // ------------------------------------------------------------------

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        // v1.21: game mode moves the window to its snap position; recording
        // that would persist the game-mode spot if the app exits while active.
        if (_gameModeActive) return;
        var s = App.Current.Settings;
        s.WindowLeft = Left;
        s.WindowTop  = Top;
    }

    private SettingsWindow? _settingsWindow;
    public void OpenSettings()
    {
        if (_settingsWindow == null || !_settingsWindow.IsLoaded)
        {
            _settingsWindow = new SettingsWindow { Owner = this };
            // v1.25.13: Settings used to spawn CenterOwner, which puts it
            // directly on top of the widget. Instead, place it adjacent to
            // the widget on whichever side has room. We position via
            // WindowStartupLocation=Manual + Left/Top set BEFORE Show()
            // (after Show() the window flickers).
            _settingsWindow.WindowStartupLocation = WindowStartupLocation.Manual;
            PositionDialogBesideWidget(_settingsWindow, desiredWidth: 720, desiredHeight: 900);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        else _settingsWindow.Activate();
    }

    /// <summary>
    /// v1.25.13: position a dialog adjacent to the widget so it doesn't
    /// open directly on top of the widget. Tries right -> left -> below ->
    /// above of the widget, falling back to a screen-corner placement if
    /// nothing fits. desiredHeight is a cap; the dialog's SizeToContent
    /// will shrink to fit if the content doesn't need that much.
    /// </summary>
    private void PositionDialogBesideWidget(Window dlg, double desiredWidth, double desiredHeight)
    {
        const double gap = 12;  // breathing room between widget and dialog
        var work = System.Windows.Forms.Screen.FromHandle(
            new System.Windows.Interop.WindowInteropHelper(this).Handle).WorkingArea;

        double widgetLeft   = Left;
        double widgetTop    = Top;
        double widgetRight  = Left + ActualWidth;
        double widgetBottom = Top + ActualHeight;

        // Use the smaller of desiredHeight and the work-area height so we
        // never propose a Y that's offscreen on short monitors.
        double dh = System.Math.Min(desiredHeight, work.Height - 40);

        // Try RIGHT of the widget
        if (widgetRight + gap + desiredWidth <= work.Right)
        {
            dlg.Left = widgetRight + gap;
            dlg.Top  = ClampTop(widgetTop, dh, work);
            return;
        }
        // Try LEFT of the widget
        if (widgetLeft - gap - desiredWidth >= work.Left)
        {
            dlg.Left = widgetLeft - gap - desiredWidth;
            dlg.Top  = ClampTop(widgetTop, dh, work);
            return;
        }
        // Try BELOW
        if (widgetBottom + gap + dh <= work.Bottom)
        {
            dlg.Left = ClampLeft(widgetLeft, desiredWidth, work);
            dlg.Top  = widgetBottom + gap;
            return;
        }
        // Try ABOVE
        if (widgetTop - gap - dh >= work.Top)
        {
            dlg.Left = ClampLeft(widgetLeft, desiredWidth, work);
            dlg.Top  = widgetTop - gap - dh;
            return;
        }
        // Fallback: pin to top-left of work area with a small inset
        dlg.Left = work.Left + 20;
        dlg.Top  = work.Top  + 20;
    }

    private static double ClampLeft(double desired, double width, System.Drawing.Rectangle work)
    {
        if (desired + width > work.Right) desired = work.Right - width - 8;
        if (desired < work.Left)          desired = work.Left + 8;
        return desired;
    }
    private static double ClampTop(double desired, double height, System.Drawing.Rectangle work)
    {
        if (desired + height > work.Bottom) desired = work.Bottom - height - 8;
        if (desired < work.Top)             desired = work.Top + 8;
        return desired;
    }

    private void OnOpenSettings(object s, RoutedEventArgs e) => OpenSettings();
    private void OnHideToTray(object sender, RoutedEventArgs e) => Hide();
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e) { e.Cancel = true; Hide(); }
    private void OnOpenTweaks(object s, RoutedEventArgs e)
    {
        var tw = new TweaksWindow { Owner = this };
        tw.ShowDialog();
    }
    private void OnToggleTopmost(object s, RoutedEventArgs e) => App.Current.Settings.AlwaysOnTop = !App.Current.Settings.AlwaysOnTop;
    private void OnToggleSnap(object s, RoutedEventArgs e) => App.Current.Settings.SnapToEdges = !App.Current.Settings.SnapToEdges;
    private void OnExit(object s, RoutedEventArgs e) => System.Windows.Application.Current.Shutdown();

    // ------------------------------------------------------------------
    // Click-through mode (Win32 WS_EX_TRANSPARENT)
    // ------------------------------------------------------------------
    [DllImport("user32.dll")] private static extern int  GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int  SetWindowLong(IntPtr hwnd, int index, int val);
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mod, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    private const int GWL_EXSTYLE        = -20;
    private const int WS_EX_TRANSPARENT  = 0x00000020;
    private const int HOTKEY_ID          = 0x9001;
    private const int GAME_MODE_HOTKEY_ID = 0x9002;
    private const int WM_HOTKEY          = 0x0312;
    private bool _clickThrough;
    private bool _gameModeActive;

    // Saved state to restore when game mode exits.
    // v1.21: tile flags and orientation are no longer saved/restored here --
    // game mode stopped mutating the persisted settings entirely. Tile
    // visibility and orientation are overridden at render time while
    // _gameModeActive is true (see RebuildVisibleTiles / ApplyOrientation).
    private double _savedLeft, _savedTop, _savedOpacity;
    private bool   _savedClickThrough;
    private LayoutOrientation? _gameModeOrientation;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        src?.AddHook(WndProc);
        RegisterClickThroughHotkey();
        RegisterGameModeHotkey();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            if (wParam.ToInt32() == HOTKEY_ID)        { SetClickThrough(!_clickThrough); handled = true; }
            if (wParam.ToInt32() == GAME_MODE_HOTKEY_ID) { ToggleGameMode(); handled = true; }
        }
        return IntPtr.Zero;
    }

    public void RegisterGameModeHotkey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, GAME_MODE_HOTKEY_ID);
        var combo = App.Current.Settings.GameModeHotkey;
        if (string.IsNullOrEmpty(combo)) return;
        HotkeyHelper.ParseCombo(combo, out var mod, out var vk);
        if (vk != 0) RegisterHotKey(hwnd, GAME_MODE_HOTKEY_ID, mod, vk);
    }

    private void ToggleGameMode()
    {
        if (_gameModeActive) ExitGameMode();
        else EnterGameMode();
    }

    private void EnterGameMode()
    {
        var s = App.Current.Settings;
        if (!s.GameModeEnabled) return;

        // Save current window state
        _savedLeft         = Left;
        _savedTop          = Top;
        _savedOpacity      = Opacity;
        _savedClickThrough = _clickThrough;

        // Show if hidden
        if (!IsVisible) { Show(); Activate(); }

        // v1.21: flip the flag BEFORE rebuilding so RebuildVisibleTiles /
        // ApplyOrientation pick up the game-mode overrides. Persisted settings
        // are never touched -- exiting the app mid-game-mode used to save the
        // game-mode tile flags, orientation, click-through state, and window
        // position as the user's normal configuration.
        _gameModeActive = true;
        _gameModeOrientation = s.GameModeOrientation switch
        {
            "Horizontal" => LayoutOrientation.Horizontal,
            "Vertical"   => LayoutOrientation.Vertical,
            _            => (LayoutOrientation?)null,   // "Current"
        };

        // Apply opacity (window-level, not persisted)
        Opacity = s.GameModeOpacity;

        // Apply click-through without persisting it
        if (s.GameModeClickThrough) SetClickThrough(true, persist: false);

        // Rebuild tiles with the overrides active
        RebuildVisibleTiles();
        ApplyOrientation();

        // Snap to position on PRIMARY monitor
        SnapToGameModePosition(s.GameModePosition);
    }

    private void ExitGameMode()
    {
        _gameModeActive      = false;
        _gameModeOrientation = null;

        // Restore opacity
        Opacity = _savedOpacity;

        // Restore click-through to the pre-game-mode state (Settings.ClickThrough
        // was never modified, so no persist needed)
        if (_clickThrough != _savedClickThrough)
            SetClickThrough(_savedClickThrough, persist: false);

        // Restore position
        Left = _savedLeft;
        Top  = _savedTop;

        // Rebuild with normal settings
        RebuildVisibleTiles();
        ApplyOrientation();
    }

    private void SnapToGameModePosition(string position)
    {
        // Always use primary monitor work area
        var area   = SystemParameters.WorkArea;
        const double margin = 10.0;

        // Need actual size — update layout first
        UpdateLayout();
        var w = ActualWidth  > 0 ? ActualWidth  : 140;
        var h = ActualHeight > 0 ? ActualHeight : 500;

        double x = Left, y = Top;
        switch (position)
        {
            case "TopLeft":      x = area.Left   + margin;             y = area.Top    + margin; break;
            case "TopCenter":    x = area.Left   + (area.Width - w)/2; y = area.Top    + margin; break;
            case "TopRight":     x = area.Right  - w - margin;         y = area.Top    + margin; break;
            case "LeftCenter":   x = area.Left   + margin;             y = area.Top    + (area.Height - h)/2; break;
            case "RightCenter":  x = area.Right  - w - margin;         y = area.Top    + (area.Height - h)/2; break;
            case "BottomLeft":   x = area.Left   + margin;             y = area.Bottom - h - margin; break;
            case "BottomCenter": x = area.Left   + (area.Width - w)/2; y = area.Bottom - h - margin; break;
            case "BottomRight":  x = area.Right  - w - margin;         y = area.Bottom - h - margin; break;
        }
        Left = x; Top = y;
    }

    public void RegisterClickThroughHotkey()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, HOTKEY_ID);
        var combo = App.Current.Settings.ClickThroughHotkey;
        if (string.IsNullOrEmpty(combo)) return;
        HotkeyHelper.ParseCombo(combo, out var mod, out var vk);
        if (vk != 0) RegisterHotKey(hwnd, HOTKEY_ID, mod, vk);
    }

    public void SetClickThrough(bool enabled, bool persist = true)
    {
        _clickThrough = enabled;
        // v1.21: game mode passes persist=false so its temporary click-through
        // never lands in settings.json (App.OnExit always saves Settings).
        if (persist) App.Current.Settings.ClickThrough = enabled;
        var hwnd  = new WindowInteropHelper(this).Handle;
        var style = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE,
            enabled ? style | WS_EX_TRANSPARENT : style & ~WS_EX_TRANSPARENT);
        if (ClickThroughIndicator != null)
            ClickThroughIndicator.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        // v1.25.37: click-through toggle removed from context menu
        // (accessible via Settings > Behavior instead).
    }

    public bool IsClickThrough => _clickThrough;

    public void ToggleGameModePublic() => ToggleGameMode();

    // OnToggleClickThrough kept for Settings checkbox use
    private void OnToggleClickThrough(object s, RoutedEventArgs e)
        => SetClickThrough(!_clickThrough);

    // ------------------------------------------------------------------
    // Task Manager — double-click only
    // ------------------------------------------------------------------
    private void OnTileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_clickThrough) return; // can't interact in click-through mode
        try { Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true }); }
        catch { }
    }
}
