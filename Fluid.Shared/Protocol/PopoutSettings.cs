using System.Collections.Generic;

namespace Fluid.Shared.Protocol;

/// <summary>
/// Per-device configuration used by both the main Settings window (when that
/// device is selected) and the popout display window.
/// </summary>
public class PopoutSettings
{
    // Display
    public double Opacity     { get; set; } = 0.9;
    public bool   SnapToEdges { get; set; } = true;
    public double TileWidth   { get; set; } = 130;
    public double TileHeight  { get; set; } = 110;

    // Tiles
    public bool ShowCpu     { get; set; } = true;
    public bool ShowGpu     { get; set; } = true;
    public bool ShowRam     { get; set; } = true;
    public bool ShowNetwork { get; set; } = true;
    public bool ShowStorage { get; set; } = true;

    // Colors
    public bool   SyncColors      { get; set; } = false;
    public string BackgroundColor { get; set; } = "#E61E1E22";
    public string TileColor       { get; set; } = "#FF2A2A30";
    public string AccentColor     { get; set; } = "#FF00A8FF";
    public string TextColor       { get; set; } = "#FFE8E8EC";
    public string MutedTextColor  { get; set; } = "#FF9A9AA8";

    // Font sizes
    public int PrimaryFontSizeOffset   { get; set; } = 0;
    public int SecondaryFontSizeOffset { get; set; } = 0;

    // Custom tile labels
    public string CpuCustomName { get; set; } = "";
    public string GpuCustomName { get; set; } = "";

    // Warnings (CPU and GPU temperature only)
    public List<TileWarning> Warnings { get; set; } = new()
    {
        new TileWarning { Kind = TileKind.Cpu, Metric = WarnMetric.Temperature, Threshold = 85 },
        new TileWarning { Kind = TileKind.Gpu, Metric = WarnMetric.Temperature, Threshold = 85 },
    };
}
