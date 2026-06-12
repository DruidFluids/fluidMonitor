using System.Windows;
using System.Windows.Controls;

namespace Fluid.App;

public partial class FluidMessageBox : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private FluidMessageBox() => InitializeComponent();

    /// <summary>
    /// Drop-in replacement for MessageBox.Show with themed styling.
    /// </summary>
    public static MessageBoxResult Show(string message, string title = "fluidMonitor",
        MessageBoxButton buttons = MessageBoxButton.OK, Window? owner = null)
    {
        var dlg = new FluidMessageBox();
        dlg.TitleLabel.Text = title;
        dlg.MessageLabel.Text = message;

        // Try to set owner for centering
        try
        {
            dlg.Owner = owner ?? Application.Current.MainWindow;
        }
        catch { /* no owner available */ }

        // Build buttons
        switch (buttons)
        {
            case MessageBoxButton.OK:
                dlg.AddButton("OK", MessageBoxResult.OK, primary: true);
                break;
            case MessageBoxButton.OKCancel:
                dlg.AddButton("Cancel", MessageBoxResult.Cancel);
                dlg.AddButton("OK", MessageBoxResult.OK, primary: true);
                break;
            case MessageBoxButton.YesNo:
                dlg.AddButton("No", MessageBoxResult.No);
                dlg.AddButton("Yes", MessageBoxResult.Yes, primary: true);
                break;
            case MessageBoxButton.YesNoCancel:
                dlg.AddButton("Cancel", MessageBoxResult.Cancel);
                dlg.AddButton("No", MessageBoxResult.No);
                dlg.AddButton("Yes", MessageBoxResult.Yes, primary: true);
                break;
        }

        dlg.ShowDialog();
        return dlg.Result;
    }

    private void AddButton(string text, MessageBoxResult result, bool primary = false)
    {
        var btn = new Button
        {
            Content = text,
            MinWidth = 80,
            Margin = new Thickness(ButtonPanel.Children.Count > 0 ? 8 : 0, 0, 0, 0),
            Style = (Style)FindResource(primary ? "BottomBarPrimary" : "BottomBar"),
        };
        btn.Click += (_, _) => { Result = result; DialogResult = true; Close(); };
        ButtonPanel.Children.Add(btn);
    }
}
