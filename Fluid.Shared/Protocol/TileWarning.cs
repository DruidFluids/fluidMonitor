using System.ComponentModel;
using System.Runtime.CompilerServices;
namespace Fluid.Shared.Protocol;
public enum TileKind { Cpu, Gpu, Ram, Network, Storage, DateTime }
public enum WarnMetric { Temperature, Load, UsedGB, Throughput }
public class TileWarning : INotifyPropertyChanged
{
    public TileKind Kind { get; set; }

    /// <summary>User-facing name: CPU, GPU, RAM etc. (not the enum casing).</summary>
    public string DisplayName => Kind switch
    {
        TileKind.Cpu      => "CPU",
        TileKind.Gpu      => "GPU",
        TileKind.Ram      => "RAM",
        TileKind.Network  => "Network",
        TileKind.Storage  => "Storage",
        TileKind.DateTime => "Clock",
        _                 => Kind.ToString()
    };

    private bool _enabled;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }
    private WarnMetric _metric = WarnMetric.Temperature;
    public WarnMetric Metric { get => _metric; set => Set(ref _metric, value); }
    private double _threshold = 85;
    public double Threshold { get => _threshold; set => Set(ref _threshold, value); }
    private bool _flashEnabled = true;
    public bool FlashEnabled { get => _flashEnabled; set => Set(ref _flashEnabled, value); }
    private string _flashColor = "#FFFF3333";
    public string FlashColor { get => _flashColor; set => Set(ref _flashColor, value); }
    private bool _gradientMode;
    public bool GradientMode { get => _gradientMode; set => Set(ref _gradientMode, value); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T f, T v, [CallerMemberName] string? n = null)
    {
        if (Equals(f, v)) return;
        f = v;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
