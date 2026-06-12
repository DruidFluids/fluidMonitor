using System.Windows;
using System.Windows.Controls;

namespace Fluid.App.Behaviors;

/// <summary>
/// v1.25.8: Attached property that wires a Button to close its owning Window
/// on click, without each window needing a code-behind handler.
/// (The drag region is now handled by System.Windows.Shell.WindowChrome,
/// configured in Styles/AppWindow.xaml -- no manual drag plumbing needed.)
/// </summary>
public static class WindowClose
{
    public static readonly DependencyProperty IsCloseProperty =
        DependencyProperty.RegisterAttached("IsClose", typeof(bool), typeof(WindowClose),
            new PropertyMetadata(false, OnIsCloseChanged));

    public static bool GetIsClose(DependencyObject o) => (bool)o.GetValue(IsCloseProperty);
    public static void SetIsClose(DependencyObject o, bool v) => o.SetValue(IsCloseProperty, v);

    private static void OnIsCloseChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Button b) return;
        if ((bool)e.NewValue)  b.Click += OnClose;
        else                   b.Click -= OnClose;
    }

    private static void OnClose(object sender, RoutedEventArgs e)
    {
        if (sender is Button b && Window.GetWindow(b) is Window w) w.Close();
    }
}
