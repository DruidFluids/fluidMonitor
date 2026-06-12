using System;
using System.Diagnostics;
using System.Windows;
using Fluid.App.Services;

namespace Fluid.App;

public partial class CpuTempDialog : Window
{
    // v1.25: docs link points at the GitHub readme for now (repo flips public
    // soon). Single constant — repoint to a docs site later in one line.
    private const string DocsUrl = "https://github.com/DruidFluids/fluidMonitor#readme";

    /// <summary>True if the driver ended up installed by the time the dialog closed.</summary>
    public bool DriverNowInstalled { get; private set; }

    public CpuTempDialog()
    {
        InitializeComponent();
        DriverNowInstalled = CpuSensorDriver.IsInstalled();
    }

    private void Show(FrameworkElement panel)
    {
        PrimaryPanel.Visibility  = ReferenceEquals(panel, PrimaryPanel)  ? Visibility.Visible : Visibility.Collapsed;
        InfoPanel.Visibility     = ReferenceEquals(panel, InfoPanel)     ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = ReferenceEquals(panel, ProgressPanel) ? Visibility.Visible : Visibility.Collapsed;
        ResultPanel.Visibility   = ReferenceEquals(panel, ResultPanel)   ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMoreInfo(object sender, RoutedEventArgs e) => Show(InfoPanel);
    private void OnBack(object sender, RoutedEventArgs e)     => Show(PrimaryPanel);
    private void OnCancel(object sender, RoutedEventArgs e)   => Close();

    private void OnOpenHomePage(object sender, RoutedEventArgs e) => OpenUrl(CpuSensorDriver.HomePageUrl);
    private void OnOpenSource(object sender, RoutedEventArgs e)   => OpenUrl(CpuSensorDriver.SourceUrl);
    private void OnOpenDocs(object sender, RoutedEventArgs e)     => OpenUrl(DocsUrl);
    private void OnOpenDownload(object sender, RoutedEventArgs e) => OpenUrl(CpuSensorDriver.DownloadUrl);

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        Show(ProgressPanel);
        ProgressText.Text = "Downloading the sensor driver…";

        var outcome = await CpuSensorDriver.InstallAsync();

        // After the installer runs, nudge the service to re-enumerate sensors
        // so the CPU tile lights up immediately (no restart).
        if (outcome.Result is CpuSensorDriver.Result.Installed or CpuSensorDriver.Result.AlreadyPresent)
            await TryRecheckServiceAsync();

        ShowResult(outcome);
    }

    private void ShowResult(CpuSensorDriver.Outcome outcome)
    {
        DriverNowInstalled = CpuSensorDriver.IsInstalled();
        FallbackBox.Visibility = Visibility.Collapsed;
        ResultCloseBtn.Content = "Done";

        switch (outcome.Result)
        {
            case CpuSensorDriver.Result.Installed:
            case CpuSensorDriver.Result.AlreadyPresent:
                ResultTitle.Text = "CPU temperature is on";
                ResultBody.Text  = "The sensor driver is installed. Your CPU temperature now appears on the widget.";
                break;

            case CpuSensorDriver.Result.Cancelled:
                // User declined the UAC prompt — not an error, just back out.
                Show(PrimaryPanel);
                return;

            default: // Failed
                ResultTitle.Text = "Automatic setup didn't finish";
                ResultBody.Text  = string.IsNullOrEmpty(outcome.Detail)
                    ? "The automatic install didn't complete."
                    : outcome.Detail;
                FallbackBox.Visibility = Visibility.Visible;
                break;
        }
        Show(ResultPanel);
    }

    private async void OnRecheck(object sender, RoutedEventArgs e)
    {
        Show(ProgressPanel);
        ProgressText.Text = "Checking for the sensor driver…";
        await TryRecheckServiceAsync();

        if (CpuSensorDriver.IsInstalled())
        {
            ShowResult(new CpuSensorDriver.Outcome(CpuSensorDriver.Result.Installed));
        }
        else
        {
            ShowResult(new CpuSensorDriver.Outcome(CpuSensorDriver.Result.Failed,
                "The driver still isn't installed. Once you've installed it, click Check again."));
        }
    }

    private static async System.Threading.Tasks.Task TryRecheckServiceAsync()
    {
        try { await CmdClient.RecheckSensorsAsync(); } catch { }
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
    }
}
