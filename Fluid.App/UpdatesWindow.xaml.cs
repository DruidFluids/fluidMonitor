using System;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Fluid.App.Services;

namespace Fluid.App;

public partial class UpdatesWindow : Window
{
    private readonly Window? _toolsWindow;
    private UpdateService.UpdateInfo? _pendingUpdate;

    public UpdatesWindow(Window? toolsWindow = null)
    {
        InitializeComponent();
        _toolsWindow = toolsWindow;

        foreach (var rd in Application.Current.Resources.MergedDictionaries)
            Resources.MergedDictionaries.Add(rd);

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        CurrentVersionLabel.Text = $"v{version?.Major}.{version?.Minor}.{version?.Build}";

        LoadUpdateMode();
        RefreshLastChecked();
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        Close();
        if (_toolsWindow != null && _toolsWindow.IsLoaded)
            _toolsWindow.Activate();
    }

    private void LoadUpdateMode()
    {
        var mode = App.Current.Settings.UpdateCheckMode ?? "Manual";
        UpdateAutoBtn.IsChecked   = mode == "Auto";
        UpdateManualBtn.IsChecked = mode == "Manual";
        UpdateOffBtn.IsChecked    = mode == "Off";
    }

    private void OnUpdateModeChanged(object sender, RoutedEventArgs e)
    {
        var mode = UpdateAutoBtn.IsChecked == true ? "Auto"
                 : UpdateOffBtn.IsChecked == true  ? "Off"
                 : "Manual";
        App.Current.Settings.UpdateCheckMode = mode;
        SettingsService.Save(App.Current.Settings);
    }

    private async void OnCheckNow(object sender, RoutedEventArgs e)
    {
        CheckNowBtn.IsEnabled = false;
        UpdateStatusText.Text = "Checking...";
        NewVersionRow.Visibility = Visibility.Collapsed;
        ChangelogBox.Visibility = Visibility.Collapsed;
        DownloadBtn.Visibility = Visibility.Collapsed;
        LaterBtn.Visibility = Visibility.Collapsed;

        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var current = $"{version?.Major}.{version?.Minor}.{version?.Build}";
            var update = await UpdateService.CheckAsync(current);

            App.Current.Settings.LastUpdateCheck = DateTime.Now.ToString("o");
            SettingsService.Save(App.Current.Settings);
            RefreshLastChecked();

            if (update == null)
            {
                UpdateStatusText.Text = "Up to date";
                UpdateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x58, 0xC8, 0x58));
            }
            else
            {
                _pendingUpdate = update;
                ShowUpdateAvailable(update);
            }
        }
        catch
        {
            UpdateStatusText.Text = "Check failed — try again later";
            UpdateStatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xC0, 0x60, 0x60));
        }
        finally
        {
            CheckNowBtn.IsEnabled = true;
        }
    }

    private void ShowUpdateAvailable(UpdateService.UpdateInfo update)
    {
        UpdateStatusText.Text = "";
        UpdateSection.BorderBrush = (Brush)FindResource("AccentBrush");
        UpdateSection.BorderThickness = new Thickness(1);

        NewVersionRow.Visibility = Visibility.Visible;
        NewVersionLabel.Text = $"v{update.Version}";

        ChangelogBox.Visibility = Visibility.Visible;
        var lines = update.Changelog.Split('\n');
        var bullets = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith("- ") || t.StartsWith("* "))
                bullets.AppendLine(t);
        }
        ChangelogText.Text = bullets.Length > 0 ? bullets.ToString().TrimEnd() : update.Changelog.Trim();

        CheckNowBtn.Visibility = Visibility.Collapsed;
        DownloadBtn.Visibility = Visibility.Visible;
        LaterBtn.Visibility = Visibility.Visible;
    }

    private async void OnDownload(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate == null) return;
        DownloadBtn.IsEnabled = false;
        DownloadBtn.Content = "Downloading...";

        try
        {
            var path = await UpdateService.DownloadAsync(
                _pendingUpdate.DownloadUrl,
                new Progress<double>(p =>
                    Dispatcher.Invoke(() =>
                        DownloadBtn.Content = $"Downloading {p:P0}...")));

            DownloadBtn.Content = "Installing...";
            UpdateService.LaunchInstallerAndExit(path);
        }
        catch (Exception ex)
        {
            DownloadBtn.Content = "Download failed";
            DownloadBtn.IsEnabled = true;
            UpdateStatusText.Text = ex.Message;
        }
    }

    private void OnLater(object sender, RoutedEventArgs e)
    {
        UpdateSection.BorderBrush = Brushes.Transparent;
        UpdateSection.BorderThickness = new Thickness(0);
        NewVersionRow.Visibility = Visibility.Collapsed;
        ChangelogBox.Visibility = Visibility.Collapsed;
        DownloadBtn.Visibility = Visibility.Collapsed;
        LaterBtn.Visibility = Visibility.Collapsed;
        CheckNowBtn.Visibility = Visibility.Visible;
        UpdateStatusText.Text = "";
    }

    private void RefreshLastChecked()
    {
        var raw = App.Current.Settings.LastUpdateCheck;
        if (string.IsNullOrEmpty(raw))
        {
            LastCheckedLabel.Text = "Last checked: never";
        }
        else if (DateTime.TryParse(raw, out var dt))
        {
            var ago = DateTime.Now - dt;
            LastCheckedLabel.Text = ago.TotalMinutes < 1   ? "Last checked: just now"
                                 : ago.TotalMinutes < 60   ? $"Last checked: {(int)ago.TotalMinutes} min ago"
                                 : ago.TotalHours < 24     ? $"Last checked: {(int)ago.TotalHours}h ago"
                                 : $"Last checked: {dt:MMM d}";
        }
    }

    private void OnDragMove(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
