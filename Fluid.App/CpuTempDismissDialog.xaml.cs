using System.Windows;

namespace Fluid.App;

public partial class CpuTempDismissDialog : Window
{
    /// <summary>Set when the user confirms a dismissal. "LoadOnly" or "HideTile". Empty on cancel.</summary>
    public string Choice { get; private set; } = "";

    public CpuTempDismissDialog() { InitializeComponent(); }

    private void OnPickLoadOnly(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Choice = "LoadOnly";
        DialogResult = true;
        Close();
    }

    private void OnPickHideTile(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        Choice = "HideTile";
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();
}
