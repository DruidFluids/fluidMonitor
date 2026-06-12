using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Fluid.Shared.Protocol;

namespace Fluid.App.Models;

// TileKind is defined in Fluid.Shared.Protocol.TileKind

public class TileData : INotifyPropertyChanged
{
    public TileKind Kind   { get; }
    public string   Header { get; }

    private string _subHeader = "";
    public string SubHeader { get => _subHeader; set => Set(ref _subHeader, value); }

    private string _primaryValue = "\u2014";
    public string PrimaryValue { get => _primaryValue; set => Set(ref _primaryValue, value); }

    private string _primaryUnit = "";
    public string PrimaryUnit { get => _primaryUnit; set => Set(ref _primaryUnit, value); }

    private bool _isStacked;
    public bool IsStacked { get => _isStacked; set => Set(ref _isStacked, value); }

    private string _line1Label = "", _line1Value = "", _line2Label = "", _line2Value = "";
    public string Line1Label { get => _line1Label; set => Set(ref _line1Label, value); }
    public string Line1Value { get => _line1Value; set => Set(ref _line1Value, value); }
    public string Line2Label { get => _line2Label; set => Set(ref _line2Label, value); }
    public string Line2Value { get => _line2Value; set => Set(ref _line2Value, value); }

    private string _secondaryValue = "";
    public string SecondaryValue { get => _secondaryValue; set => Set(ref _secondaryValue, value); }

    // v1.20.3 / v1.23: spacing between the Line1/Line2 label (↓↑ or R:/W:)
    // and the value. v1.23 reworked the mechanism: the label column is now a
    // FIXED 40px (slider max) so the *-width value column never moves when
    // the slider changes. The slider instead drives LabelMargin -- a right
    // margin on the right-aligned label that pushes the arrow away from the
    // value text. Slider range stays 8-40: 8 = hugging the text, 40 = 32px gap.
    private double _labelColumnWidth = 16.0;
    public double LabelColumnWidth
    {
        get => _labelColumnWidth;
        set
        {
            if (Set(ref _labelColumnWidth, value))
                OnPropertyChanged(nameof(LabelMargin));
        }
    }
    public System.Windows.Thickness LabelMargin =>
        new(0, 0, System.Math.Max(0, _labelColumnWidth - 8), 0);

    // v1.20.3: active-traffic flags for Network ↓/↑. Set by SensorState when
    // the value is non-zero. The widget's tile template binds these to opacity
    // animations / drop shadows depending on AppSettings.NetworkTrafficIndicator.
    private bool _line1Active;
    private bool _line2Active;
    public bool Line1Active { get => _line1Active; set => Set(ref _line1Active, value); }
    public bool Line2Active { get => _line2Active; set => Set(ref _line2Active, value); }

    // Warning flash — DispatcherTimer toggles FlashActive; DataTrigger swaps tile background
    private bool _flashActive;
    public bool FlashActive { get => _flashActive; set => Set(ref _flashActive, value); }

    private SolidColorBrush? _flashBrush;
    public SolidColorBrush? FlashBrush { get => _flashBrush; set => Set(ref _flashBrush, value); }

    // Gradient accent override — null means use global AccentBrush
    private string? _accentOverride;
    public string? AccentOverride { get => _accentOverride; set => Set(ref _accentOverride, value); }

    // v1.25: when true, the tile shows a small "Turn on temperature" affordance
    // in place of the (em-dash) temperature value. Only the CPU tile sets this,
    // and only when no CPU-temp sensor driver is present. Clicking it opens the
    // opt-in dialog (wired in MainWindow). The flag also drives hiding the bare
    // em-dash so the tile never looks broken.
    private bool _tempHintVisible;
    public bool TempHintVisible { get => _tempHintVisible; set => Set(ref _tempHintVisible, value); }

    public TileData(TileKind kind, string header) { Kind = kind; Header = header; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }
}
