using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Fluid.App.Models;
using Fluid.App.Services;
using Fluid.Shared.Protocol;

namespace Fluid.App;

public partial class RemotePopoutWindow : Window
{
    private readonly ObservableCollection<TileData> _tiles = new();
    private readonly RemoteDevice _device;
    private readonly SensorState  _state;

    private bool  _isDragging;
    private Point _dragStart;
    private Point _winStart;

    public RemotePopoutWindow(RemoteDevice device)
    {
        InitializeComponent();
        _device = device;
        _state  = App.Current.DeviceManager.GetOrCreate(device);

        Title            = $"fluidMonitor — {device.Name}";
        DeviceLabel.Text = device.Name.ToUpperInvariant();
        DataContext      = _tiles;

        ApplySettings();
        BuildTiles();
    }

    public void ApplySettings()
    {
        var p = _device.Popout;

        // Sync colors from main if requested
        if (p.SyncColors)
        {
            var s = App.Current.Settings;
            p.BackgroundColor = s.BackgroundColor; p.TileColor   = s.TileColor;
            p.AccentColor     = s.AccentColor;     p.TextColor   = s.TextColor;
            p.MutedTextColor  = s.MutedTextColor;
        }

        Opacity = p.Opacity;
        ThemeApplier.ApplyPopout(p, Resources); // write to THIS window's resources only
    }

    public void BuildTiles()
    {
        var p = _device.Popout;
        var s = App.Current.Settings;
        _tiles.Clear();
        // v1.18: respect user's TileOrder (same as local widget). Clock tile is
        // local-only -- if it appears in TileOrder we skip it here.
        foreach (var kindName in s.TileOrder)
        {
            if (!System.Enum.TryParse<Fluid.Shared.Protocol.TileKind>(kindName, out var kind)) continue;
            switch (kind)
            {
                case Fluid.Shared.Protocol.TileKind.Cpu:     if (p.ShowCpu)     _tiles.Add(_state.CpuTile);     break;
                case Fluid.Shared.Protocol.TileKind.Gpu:     if (p.ShowGpu)     _tiles.Add(_state.GpuTile);     break;
                case Fluid.Shared.Protocol.TileKind.Ram:     if (p.ShowRam)     _tiles.Add(_state.RamTile);     break;
                case Fluid.Shared.Protocol.TileKind.Network: if (p.ShowNetwork) _tiles.Add(_state.NetworkTile); break;
                case Fluid.Shared.Protocol.TileKind.Storage: if (p.ShowStorage) _tiles.Add(_state.StorageTile); break;
                case Fluid.Shared.Protocol.TileKind.DateTime: /* local-only */ break;
            }
        }
    }

    private void OnClosePopout(object sender, RoutedEventArgs e) => Close();

    // ------------------------------------------------------------------
    // Drag + snap
    // ------------------------------------------------------------------
    private Point CursorDip(MouseEventArgs e)
    {
        var sp  = PointToScreen(e.GetPosition(this));
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget != null)
            return src.CompositionTarget.TransformFromDevice.Transform(sp);
        return sp;
    }

    private void OnDragMouseDown(object s, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (s is not IInputElement el) return;
        _isDragging = true;
        _dragStart  = CursorDip(e);
        _winStart   = new Point(Left, Top);
        el.CaptureMouse();
        e.Handled = true;
    }

    private void OnDragMouseMove(object s, MouseEventArgs e)
    {
        if (!_isDragging) return;
        var c = CursorDip(e);
        Left = _winStart.X + (c.X - _dragStart.X);
        Top  = _winStart.Y + (c.Y - _dragStart.Y);
    }

    private void OnDragMouseUp(object s, MouseButtonEventArgs e)
    {
        if (!_isDragging) return;
        _isDragging = false;
        if (s is IInputElement el) el.ReleaseMouseCapture();
        if (_device.Popout.SnapToEdges) SnapToEdges();
    }

    private void SnapToEdges()
    {
        const double snap = 18.0;
        var area = SystemParameters.WorkArea;
        if (Left < snap)                              Left = 0;
        if (Top  < snap)                              Top  = 0;
        if (Left + ActualWidth  > area.Right  - snap) Left = area.Right  - ActualWidth;
        if (Top  + ActualHeight > area.Bottom - snap) Top  = area.Bottom - ActualHeight;
    }
}
