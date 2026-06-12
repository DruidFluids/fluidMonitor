using System.Windows;
using System.Windows.Input;

namespace Fluid.App;

public partial class ToolsWindow : Window
{
    public ToolsWindow(Window? owner = null)
    {
        InitializeComponent();
        foreach (var rd in Application.Current.Resources.MergedDictionaries)
            Resources.MergedDictionaries.Add(rd);
    }

    /// <summary>
    /// Opens a sub-window positioned exactly over this one, hides Tools while
    /// the sub-window is open, then re-shows when it closes. Feels like
    /// navigating within one window.
    /// </summary>
    private void NavigateTo(Window subWindow)
    {
        subWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        subWindow.Left = Left;
        subWindow.Top = Top;
        subWindow.Width = Width;

        // Set owner to the Settings window (our owner) so it stays in front
        if (Owner != null)
            subWindow.Owner = Owner;

        Hide();

        subWindow.Closed += (_, _) =>
        {
            Show();
            Activate();
        };

        foreach (var rd in Resources.MergedDictionaries)
            subWindow.Resources.MergedDictionaries.Add(rd);

        subWindow.ShowDialog();
    }

    private void OnOpenAlerts(object sender, RoutedEventArgs e)
        => NavigateTo(new WarningsWindow());

    private void OnOpenGameMode(object sender, RoutedEventArgs e)
        => NavigateTo(new GameModeWindow());

    private void OnOpenUtilities(object sender, RoutedEventArgs e)
        => NavigateTo(new TweaksWindow());


    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
