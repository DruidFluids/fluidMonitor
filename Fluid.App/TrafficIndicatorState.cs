using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Fluid.App;

// v1.20.3: Singleton the tile templates bind to for the Network traffic
// indicator animation, via {x:Static local:TrafficIndicatorState.Instance}.
// v1.21: exposes the STYLE string ("Off" / "Blink" / "Fade" / "Glow") instead
// of a bool. The previous bool meant Blink, Fade, and Glow all rendered the
// same animation; the XAML triggers now match on the specific style value.
// The Settings window updates it when AppSettings.NetworkTrafficIndicator
// changes, and App.OnStartup seeds it from the loaded setting.
public class TrafficIndicatorState : INotifyPropertyChanged
{
    public static TrafficIndicatorState Instance { get; } = new();

    private string _style = "Off";
    public string Style
    {
        get => _style;
        set
        {
            if (_style == value) return;
            _style = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Style)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
