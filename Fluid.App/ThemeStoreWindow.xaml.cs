using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Fluid.App.Services;

namespace Fluid.App;

public partial class ThemeStoreWindow : Window
{
    private List<PackViewModel> _packs = new();

    /// <summary>Set to true when packs were downloaded/removed so the caller can refresh.</summary>
    public bool Changed { get; private set; }

    public ThemeStoreWindow()
    {
        InitializeComponent();
        foreach (var rd in Application.Current.Resources.MergedDictionaries)
            Resources.MergedDictionaries.Add(rd);
        Loaded += async (_, _) => await LoadManifestAsync();
    }

    private async System.Threading.Tasks.Task LoadManifestAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;

        var manifest = await ThemePackService.FetchManifestAsync();
        if (manifest == null)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            SummaryLabel.Text = "Could not reach GitHub — check your connection.";
            return;
        }

        var installed = ThemePackService.GetInstalledPackIds();
        _packs = manifest.Packs
            .OrderByDescending(p => p.Count)
            .Select(p => new PackViewModel
            {
                Id = p.Id,
                Name = p.Name,
                Count = p.Count,
                Filename = p.File,
                Swatches = p.Swatches,
                IsInstalled = installed.Contains(p.Id),
            })
            .ToList();

        PackGrid.ItemsSource = _packs;
        RefreshSummary();
        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private void RefreshSummary()
    {
        var total = _packs.Sum(p => p.Count);
        var installedCount = _packs.Count(p => p.IsInstalled);
        SummaryLabel.Text = $"{_packs.Count} packs · {total} themes · {installedCount} installed";

        var selected = _packs.Count(p => p.IsSelected && !p.IsInstalled);
        DownloadBtn.IsEnabled = selected > 0;
        DownloadBtn.Content = selected > 0
            ? $"Download {selected} pack{(selected > 1 ? "s" : "")}"
            : "Download selected";
        SelectionLabel.Text = selected > 0
            ? $"{selected} selected ({_packs.Where(p => p.IsSelected && !p.IsInstalled).Sum(p => p.Count)} themes)"
            : "";
        RemoveBtn.Visibility = installedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PackViewModel vm })
        {
            if (vm.IsInstalled) return; // already installed, no toggle
            vm.IsSelected = !vm.IsSelected;
            RefreshSummary();
        }
    }

    private async void OnDownloadSelected(object sender, RoutedEventArgs e)
    {
        var toDownload = _packs.Where(p => p.IsSelected && !p.IsInstalled).ToList();
        if (toDownload.Count == 0) return;

        DownloadBtn.IsEnabled = false;
        DownloadBtn.Content = "Downloading...";

        int success = 0;
        foreach (var pack in toDownload)
        {
            DownloadBtn.Content = $"Downloading {pack.Name}...";
            if (await ThemePackService.DownloadPackAsync(pack.Filename))
            {
                pack.IsInstalled = true;
                pack.IsSelected = false;
                success++;
            }
        }

        Changed = true;
        RefreshSummary();
        DownloadBtn.Content = $"Downloaded {success} pack{(success > 1 ? "s" : "")}";
    }

    private void OnRemoveInstalled(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Remove all downloaded theme packs? Built-in themes won't be affected.",
            "Remove theme packs",
            MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        foreach (var pack in _packs.Where(p => p.IsInstalled))
        {
            ThemePackService.RemovePack(pack.Id);
            pack.IsInstalled = false;
        }

        Changed = true;
        RefreshSummary();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e)
    {
        await LoadManifestAsync();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Close();
    }

    // ------------------------------------------------------------------
    // ViewModel
    // ------------------------------------------------------------------

    public class PackViewModel : INotifyPropertyChanged
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Count { get; set; }
        public string Filename { get; set; } = "";
        public string[] Swatches { get; set; } = Array.Empty<string>();

        private bool _isInstalled;
        public bool IsInstalled
        {
            get => _isInstalled;
            set { _isInstalled = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); OnPropertyChanged(nameof(StatusColor)); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(); }
        }

        public string StatusText => IsInstalled ? "installed" : "";
        public Brush StatusColor => IsInstalled
            ? new SolidColorBrush(Color.FromRgb(0x58, 0xC8, 0x58))
            : Brushes.Transparent;

        public List<SolidColorBrush> SwatchBrushes => Swatches
            .Take(7)
            .Select(hex =>
            {
                try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
                catch { return Brushes.Gray; }
            })
            .ToList();

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }
}
