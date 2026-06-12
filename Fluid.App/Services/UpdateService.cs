using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Fluid.App.Services;

/// <summary>
/// Checks GitHub releases for a newer version, downloads the installer,
/// and launches it to perform an in-place update.
///
/// Source: https://api.github.com/repos/DruidFluids/fluidMonitor/releases/latest
///
/// Modes (persisted in AppSettings.UpdateCheckMode):
///   "Auto"   – check silently on every app launch
///   "Manual" – only when user clicks "Check now"
///   "Off"    – never (Check now button still works)
///
/// Default: Manual.
/// </summary>
public static class UpdateService
{
    private const string ApiUrl =
        "https://api.github.com/repos/DruidFluids/fluidMonitor/releases/latest";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    static UpdateService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "fluidMonitor-updater");
        _http.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
    }

    public record UpdateInfo(
        string Version,
        string DownloadUrl,
        string Changelog,
        string PublishedAt,
        long   SizeBytes
    );

    /// <summary>
    /// Checks GitHub for the latest release. Returns UpdateInfo if a newer
    /// version exists, null if current or newer, throws on network error.
    /// </summary>
    public static async Task<UpdateInfo?> CheckAsync(string currentVersion)
    {
        var response = await _http.GetAsync(ApiUrl);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tagName = root.GetProperty("tag_name").GetString() ?? "";
        var remoteVersion = tagName.TrimStart('v');
        var body = root.GetProperty("body").GetString() ?? "";
        var published = root.GetProperty("published_at").GetString() ?? "";

        // Compare versions
        if (!Version.TryParse(remoteVersion, out var remote)) return null;
        if (!Version.TryParse(currentVersion, out var current)) return null;
        if (remote <= current) return null;

        // Find the .exe asset
        string downloadUrl = "";
        long sizeBytes = 0;

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? "";
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                sizeBytes = asset.GetProperty("size").GetInt64();
                break;
            }
        }

        if (string.IsNullOrEmpty(downloadUrl)) return null;

        return new UpdateInfo(remoteVersion, downloadUrl, body, published, sizeBytes);
    }

    /// <summary>
    /// Downloads the installer .exe to %TEMP% and returns its path.
    /// Reports progress (0.0–1.0) if a callback is provided.
    /// </summary>
    public static async Task<string> DownloadAsync(
        string downloadUrl,
        IProgress<double>? progress = null)
    {
        var fileName = Path.GetFileName(new Uri(downloadUrl).LocalPath);
        var tempPath = Path.Combine(Path.GetTempPath(), fileName);

        using var response = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        long downloaded = 0;

        await using var contentStream = await response.Content.ReadAsStreamAsync();
        await using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
            downloaded += bytesRead;
            if (totalBytes > 0)
                progress?.Report((double)downloaded / totalBytes);
        }

        progress?.Report(1.0);
        return tempPath;
    }

    /// <summary>
    /// Launches the downloaded installer with /SILENT and exits the app.
    /// The installer handles service stop, uninstall, reinstall, service start.
    /// </summary>
    public static void LaunchInstallerAndExit(string installerPath)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART",
            UseShellExecute = true
        };
        System.Diagnostics.Process.Start(psi);
        System.Windows.Application.Current.Shutdown();
    }
}
