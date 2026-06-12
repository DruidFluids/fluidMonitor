using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Fluid.App.Models;
using Fluid.Shared.Protocol;

namespace Fluid.App.Services;

public sealed class SensorState
{
    public TileData CpuTile     { get; } = new(TileKind.Cpu,     "CPU");
    public TileData GpuTile     { get; } = new(TileKind.Gpu,     "GPU");
    public TileData RamTile     { get; } = new(TileKind.Ram,     "RAM");
    public TileData NetworkTile { get; } = new(TileKind.Network, "Network") { IsStacked = true };
    public TileData StorageTile { get; } = new(TileKind.Storage, "Disk") { IsStacked = true };
    // v1.16: DateTime tile -- not driven by sensor snapshots, updated by a
    // local DispatcherTimer (StartClock). Weather integration deferred.
    public TileData DateTimeTile { get; } = new(TileKind.DateTime, "Clock");

    // When non-null, these warnings are used instead of App.Current.Settings.Warnings
    public List<TileWarning>? ExternalWarnings { get; set; }

    public IReadOnlyList<TileData> AllTiles => new[]
        { CpuTile, GpuTile, RamTile, NetworkTile, StorageTile, DateTimeTile };

    private DateTime _lastApply = DateTime.MinValue;
    private static int ThrottleMs
    { get { try { return App.Current.Settings.UpdateIntervalMs; } catch { return 1000; } } }

    // Flash timers — one per tile kind, started/stopped based on threshold crossing
    private readonly Dictionary<TileKind, DispatcherTimer> _flashTimers = new();
    private readonly Dictionary<TileKind, bool>            _flashState   = new();

    public void Attach(ISensorClient client)
    {
        client.SnapshotReceived       += OnSnapshot;
        client.ConnectionStateChanged += OnConnectionChanged;
        // v1.16: clock tile is local, drives off DispatcherTimer not sensor pipe.
        StartClock();
    }

    // v1.16: clock tile updater. Ticks every 1s to advance the time display.
    // The clock tile is always populated even if ShowDateTime is false -- the
    // MainWindow's pairs[] gate decides whether to render it.
    private DispatcherTimer? _clockTimer;
    private void StartClock()
    {
        if (_clockTimer != null) return;
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();
    }
    private void UpdateClock()
    {
        var now = System.DateTime.Now;
        // v1.25.8: redesigned clock tile.
        //   * Header dropped (the time + date make the tile self-identifying).
        //   * Primary remains the time, 12-hour, with am/pm in the unit slot.
        //   * Secondary is now a two-line "Weekday,\nMonth Dayth" with the
        //     ordinal suffix on the day number ("10th", "3rd", "21st", "22nd").
        //     The newline is honored because SecondaryValueText style sets
        //     TextWrapping=Wrap; we use \n to force the break consistently
        //     instead of relying on width-based wrapping.
        DateTimeTile.SubHeader      = "";                  // drops the "Clock" label
        DateTimeTile.PrimaryValue   = now.ToString("h:mm");
        DateTimeTile.PrimaryUnit    = now.ToString("tt").ToLower();
        DateTimeTile.SecondaryValue = $"{now:dddd},\n{now:MMMM} {now.Day}{OrdinalSuffix(now.Day)}";
    }

    // English ordinal suffix for a day-of-month. 11/12/13 are the irregulars.
    private static string OrdinalSuffix(int day) =>
        (day % 100) is >= 11 and <= 13
            ? "th"
            : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };

    private void OnSnapshot(SensorSnapshot s)
    {
        var now = DateTime.UtcNow;
        // v1.21: 10% tolerance. The service pushes at a fixed cadence; a strict
        // comparison against the full interval meant a snapshot arriving even
        // 1ms "early" (normal pipe jitter) was dropped, halving the effective
        // update rate (e.g. a 1500ms setting rendered at ~3000ms).
        if ((now - _lastApply).TotalMilliseconds < ThrottleMs * 0.9) return;
        _lastApply = now;
        Dispatch(() => Apply(s));
    }

    private void OnConnectionChanged(bool connected) =>
        Dispatch(() => { if (!connected) MarkDisconnected(); });

    private static string Temp(float? tempC)
    {
        // v1.25.54: treat 0 and negative as missing (phantom zero from
        // LHM when the driver is uninstalled but service hasn't restarted)
        if (!tempC.HasValue || tempC.Value <= 0f) return "\u2014";
        bool f;
        try { f = App.Current.Settings.UseFahrenheit; } catch { f = false; }
        if (f) { var v = tempC.Value * 9f / 5f + 32f; return $"{v:0}[a]\u00B0F[/a]"; }
        return $"{tempC.Value:0}[a]\u00B0C[/a]";
    }

    private void Apply(SensorSnapshot s)
    {
        // Warnings FIRST (v1.21). CheckWarning sets tile.AccentOverride, and
        // Fmt reads the override when rendering tagged text. Evaluating
        // warnings after the text was assigned meant the gradient color was
        // always one snapshot behind.
        CheckWarning(CpuTile,     TileKind.Cpu,     s.Cpu.TempC,             s.Cpu.LoadPercent,       0, 0);
        CheckWarning(GpuTile,     TileKind.Gpu,     s.Gpu.TempC,             s.Gpu.LoadPercent,       0, 0);
        CheckWarning(RamTile,     TileKind.Ram,     null,                    s.Ram.LoadPercent,       s.Ram.UsedGb, 0);
        CheckWarning(NetworkTile, TileKind.Network, null,                    0,                       0, s.Network.DownBytesPerSec / 1024 / 1024);
        CheckWarning(StorageTile, TileKind.Storage, null,                    0,                       0, s.Storage.ReadBytesPerSec / 1024 / 1024);

        // CPU
        var cpuCustom = "";
        try { cpuCustom = App.Current.Settings.CpuCustomName; } catch { }
        CpuTile.SubHeader    = !string.IsNullOrEmpty(cpuCustom) ? cpuCustom : Shorten(s.Cpu.Name);
        // v1.25.1: hint trigger now treats TempC <= 0 as missing (fixes the
        // LHM-phantom-zero rendering as a real "0 C" reading) and respects
        // user dismissal. Dismissal choice "LoadOnly" suppresses the temp
        // slot but keeps the tile visible; "HideTile" is handled by
        // RebuildVisibleTiles excluding the tile entirely.
        bool cpuTempMissing = !s.Cpu.TempC.HasValue || s.Cpu.TempC.Value <= 0f;
        bool driverPresent;
        bool dismissed;
        string dismissChoice;
        try
        {
            driverPresent = Services.CpuSensorDriver.IsInstalled();
            dismissed     = App.Current.Settings.CpuTempHintDismissed;
            dismissChoice = App.Current.Settings.CpuTempDismissChoice ?? "";
        }
        catch { driverPresent = true; dismissed = false; dismissChoice = ""; }

        bool showHint = cpuTempMissing && !driverPresent && !dismissed;
        // v1.25.60: if user dismissed with LoadOnly, always show load-only
        // when temp is missing — even if IsInstalled() still returns true
        // (driver files may linger on disk until reboot).
        bool loadOnly = cpuTempMissing && (
            (!driverPresent && dismissed && dismissChoice == "LoadOnly") ||
            (dismissed && dismissChoice == "LoadOnly" && s.Cpu.TempC.GetValueOrDefault() <= 0f));
        CpuTile.TempHintVisible = showHint;
        CpuTile.PrimaryValue = (showHint || loadOnly || cpuTempMissing)
            ? $"{s.Cpu.LoadPercent:0}[a]%[/a]"
            : $"{Temp(s.Cpu.TempC)}  {s.Cpu.LoadPercent:0}[a]%[/a]";
        CpuTile.PrimaryUnit  = "";
        CpuTile.SecondaryValue = s.Cpu.ClockMhz.HasValue ? $"{s.Cpu.ClockMhz:0} [a]MHz[/a]" : "";

        // GPU (primary)
        var gpuCustom  = "";         try {
            gpuCustom  = App.Current.Settings.GpuCustomName;
                    } catch { }
        UpdateGpuTile(GpuTile, s.Gpu, gpuCustom);

        // RAM
        var ramSpeed = s.Ram.MemorySpeedMhz > 0
            ? (s.Ram.MemoryType.Length > 0 ? $"{s.Ram.MemoryType}-{s.Ram.MemorySpeedMhz}" : $"{s.Ram.MemorySpeedMhz} MHz")
            : "";
        RamTile.SubHeader      = ramSpeed;
        // v1.23: redesigned per user request -- big centered current usage
        // ("17.4 GB"), with "27% of 64.0 GB" as the secondary line.
        RamTile.PrimaryValue   = $"{s.Ram.UsedGb:0.0}";
        RamTile.PrimaryUnit    = "GB";
        RamTile.SecondaryValue = $"{s.Ram.LoadPercent:0}[a]%[/a] of {s.Ram.TotalGb:0.0} [a]GB[/a]";

        // Network
        var adapterLabel = "";
        try
        {
            var sel = App.Current.Settings.NetworkAdapterName;
            adapterLabel = string.IsNullOrEmpty(sel) ? "All adapters" : sel;
        }
        catch { }
        NetworkTile.SubHeader  = adapterLabel;
        NetworkTile.Line1Label = "↓";
        NetworkTile.Line1Value = FmtNet(s.Network.DownBytesPerSec);
        NetworkTile.Line2Label = "↑";
        NetworkTile.Line2Value = FmtNet(s.Network.UpBytesPerSec);
        // v1.20.3: width of arrow column + active-traffic flags
        try
        {
            NetworkTile.LabelColumnWidth = App.Current.Settings.NetworkArrowSpacing;
            NetworkTile.Line1Active      = s.Network.DownBytesPerSec > 0;
            NetworkTile.Line2Active      = s.Network.UpBytesPerSec   > 0;
        }
        catch { }

        // Disk
        StorageTile.SubHeader  = "";
        StorageTile.Line1Label = "R:";
        StorageTile.Line1Value = FmtDisk(s.Storage.ReadBytesPerSec);
        StorageTile.Line2Label = "W:";
        StorageTile.Line2Value = FmtDisk(s.Storage.WriteBytesPerSec);
        // v1.25: NVMe drive temp (driver-free) renders as a third row under
        // R:/W: via the tile's SecondaryValue slot. Null (non-NVMe / aggregate
        // / unsupported) leaves it blank so the tile just shows R:/W:.
        StorageTile.SecondaryValue = s.Storage.TempC.HasValue
            ? $"T: {Temp(s.Storage.TempC)}"
            : "";
        // v1.20.3: width of label column + disk model in SubHeader
        try
        {
            StorageTile.LabelColumnWidth = App.Current.Settings.DiskLabelSpacing;
            // v1.24: SubHeader honors DiskLabelStyle (Letter / Model / Both).
            // Letters come from the service (v1.24 protocol field); when an
            // older service leaves them empty, fall back to the model.
            var letters = s.Storage.DriveLetters ?? "";
            var model   = s.Storage.Model ?? "";
            StorageTile.SubHeader = App.Current.Settings.DiskLabelStyle switch
            {
                "Model" => model,
                "Both"  => letters.Length > 0 && model.Length > 0 ? $"{letters} \u00B7 {model}"
                           : letters.Length > 0 ? letters : model,
                _       => letters.Length > 0 ? letters : model, // "Letter" default
            };
        }
        catch { }

        // v1.21: warnings moved to the TOP of Apply (see above).
    }

    private void CheckWarning(TileData tile, TileKind kind,
        float? tempC, float load, float usedGb, double throughputMbs)
    {
        TileWarning? warn = null;
        try
        {
            var warnings = ExternalWarnings
                ?? (System.Windows.Application.Current != null ? App.Current.Settings.Warnings : null);
            if (warnings == null) return;
            foreach (var w in warnings) { if (w.Kind == kind) { warn = w; break; } }
        }
        catch { return; }

        if (warn == null || !warn.Enabled)
        {
            StopFlash(tile, kind);
            tile.AccentOverride = null;
            return;
        }

        double current = warn.Metric switch
        {
            WarnMetric.Temperature => tempC ?? 0,
            WarnMetric.Load        => load,
            WarnMetric.UsedGB      => usedGb,
            WarnMetric.Throughput  => throughputMbs,
            _                      => 0
        };

        bool exceeded = current >= warn.Threshold;

        // Gradient mode — activates within 15° of threshold
        if (warn.GradientMode && warn.Metric == WarnMetric.Temperature && tempC.HasValue)
        {
            double dist = warn.Threshold - tempC.Value;
            tile.AccentOverride = dist > 15 ? null : GradientColor(dist);
        }
        else
        {
            tile.AccentOverride = null;
        }

        // Flash
        if (exceeded && warn.FlashEnabled)
            StartFlash(tile, kind, warn.FlashColor);
        else
            StopFlash(tile, kind);
    }

    private void StartFlash(TileData tile, TileKind kind, string hexColor)
    {
        SolidColorBrush brush;
        try { brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor)); }
        catch { brush = new SolidColorBrush(Colors.Red); }
        tile.FlashBrush = brush;

        if (_flashTimers.ContainsKey(kind)) return; // already flashing

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        timer.Tick += (_, _) =>
        {
            _flashState[kind] = !_flashState.GetValueOrDefault(kind);
            tile.FlashActive  = _flashState[kind];
        };
        _flashTimers[kind] = timer;
        _flashState[kind]  = false;
        timer.Start();
    }

    private void StopFlash(TileData tile, TileKind kind)
    {
        if (_flashTimers.TryGetValue(kind, out var t)) { t.Stop(); _flashTimers.Remove(kind); }
        _flashState.Remove(kind);
        tile.FlashActive = false;
    }

    /// <summary>
    /// Returns accent hex color based on distance below threshold.
    /// dist=15 → blue, dist=10 → purple, dist=4 → red-purple, dist≤0 → bright red.
    /// </summary>
    private static string GradientColor(double dist)
    {
        // Waypoints: blue(15) → purple(10) → redpurple(4) → red(0)
        (double pos, byte r, byte g, byte b)[] stops =
        {
            (15, 0x00, 0x66, 0xCC), // cool blue
            (10, 0x66, 0x33, 0xCC), // purple
            ( 4, 0xCC, 0x33, 0x66), // red-purple
            ( 0, 0xFF, 0x22, 0x00), // bright red
        };

        if (dist >= stops[0].pos) return $"#FF{stops[0].r:X2}{stops[0].g:X2}{stops[0].b:X2}";
        if (dist <= stops[^1].pos) return $"#FF{stops[^1].r:X2}{stops[^1].g:X2}{stops[^1].b:X2}";

        for (int i = 0; i < stops.Length - 1; i++)
        {
            var (p1, r1, g1, b1) = stops[i];
            var (p2, r2, g2, b2) = stops[i + 1];
            if (dist <= p1 && dist >= p2)
            {
                var t = (p1 - dist) / (p1 - p2);
                var r = (byte)(r1 + (r2 - r1) * t);
                var g = (byte)(g1 + (g2 - g1) * t);
                var b = (byte)(b1 + (b2 - b1) * t);
                return $"#FF{r:X2}{g:X2}{b:X2}";
            }
        }
        return $"#FF{stops[^1].r:X2}{stops[^1].g:X2}{stops[^1].b:X2}";
    }

    private void MarkDisconnected()
    {
        foreach (var t in AllTiles)
        {
            t.PrimaryValue = "\u2014"; t.PrimaryUnit = "";
            t.SecondaryValue = "offline"; t.SubHeader = "";
            t.Line1Label = ""; t.Line1Value = ""; t.Line2Label = ""; t.Line2Value = "";
        }
    }

    private void UpdateGpuTile(TileData tile, Fluid.Shared.Protocol.GpuInfo gpu, string customName)
    {
        tile.SubHeader    = !string.IsNullOrEmpty(customName) ? customName : Shorten(gpu.Name);
        tile.PrimaryValue = $"{Temp(gpu.TempC)}  {gpu.LoadPercent:0}[a]%[/a]";
        tile.PrimaryUnit  = "";
        var gpuMhz  = gpu.ClockMhz.HasValue ? $"{gpu.ClockMhz:0} [a]MHz[/a]" : "";
        var gpuVram = "";
        if (gpu.VramUsedMb is > 0 && gpu.VramTotalMb is > 0)
        {
            var vu = gpu.VramUsedMb!.Value / 1024f;
            var vt = gpu.VramTotalMb!.Value / 1024f;
            gpuVram = $"{vu:0.0}/{vt:0.0} [a]GB[/a]";
        }
        tile.SecondaryValue = (gpuMhz.Length > 0 && gpuVram.Length > 0)
            ? gpuMhz + "\n" + gpuVram : gpuMhz + gpuVram;
    }

    private static string FmtNet(double bps)
    {
        if (bps < 1024)               return $"{bps:0} [a]B/s[/a]";
        if (bps < 1024 * 1024)        return $"{bps / 1024:0.0} [a]KB/s[/a]";
        if (bps < 1024L * 1024 * 1024) return $"{bps / 1024 / 1024:0.0} [a]MB/s[/a]";
        return $"{bps / 1024 / 1024 / 1024:0.0} [a]GB/s[/a]";
    }

    private static string FmtDisk(double bps)
    {
        if (bps < 1024)               return $"{bps:0} [a]B/s[/a]";
        if (bps < 1024 * 1024)        return $"{bps / 1024:0} [a]KB/s[/a]";
        if (bps < 1024L * 1024 * 1024) return $"{bps / 1024 / 1024:0.0} [a]MB/s[/a]";
        return $"{bps / 1024 / 1024 / 1024:0.0} [a]GB/s[/a]";
    }

    private static string Shorten(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var n = name.Trim();
        foreach (var p in new[] { "AMD ", "NVIDIA ", "Intel(R) ", "Intel " })
            if (n.StartsWith(p, StringComparison.OrdinalIgnoreCase)) { n = n[p.Length..]; break; }
        n = n.Replace("(R)", "").Replace("(TM)", "").Replace("(tm)", "");
        // v1.21: strip any "<N>-Core" token generically. The old explicit
        // suffix list (" 16-Core Processor" etc.) was unreachable because the
        // bare " Processor" entry matched first and broke out of the loop --
        // "Ryzen 9 9950X3D 16-Core Processor" rendered as "Ryzen 9 9950X3D 16-Core".
        n = System.Text.RegularExpressions.Regex.Replace(
                n, @"\s+\d+-Core\b", "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        foreach (var s in new[] { " Processor", " CPU", " Graphics" })
            if (n.EndsWith(s, StringComparison.OrdinalIgnoreCase)) { n = n[..^s.Length]; break; }
        return n.Trim();
    }

    private static void Dispatch(Action a)
    {
        var app = System.Windows.Application.Current;
        if (app == null) return;
        if (app.Dispatcher.CheckAccess()) a(); else app.Dispatcher.BeginInvoke(a);
    }
}
