using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace Fluid.App.Services;

/// <summary>
/// Manages downloadable theme packs hosted on GitHub.
/// Manifest + pack JSON files live at:
///   https://raw.githubusercontent.com/DruidFluids/fluidMonitor/main/themes/
/// Downloaded packs are stored in %AppData%\fluidMonitor\themes\
/// </summary>
public static class ThemePackService
{
    private const string BaseUrl =
        "https://raw.githubusercontent.com/DruidFluids/fluidMonitor/main/themes/";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    static ThemePackService()
    {
        _http.DefaultRequestHeaders.Add("User-Agent", "fluidMonitor-themes");
    }

    /// <summary>Local folder where downloaded pack files are stored.</summary>
    public static string PackFolder
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "fluidMonitor", "themes");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    // ------------------------------------------------------------------
    // Data records
    // ------------------------------------------------------------------

    public record PackInfo(
        string Id,
        string Name,
        int Count,
        string File,
        string[] Swatches);

    public record Manifest(int Version, PackInfo[] Packs);

    public record ThemeEntry(
        string Name, string Bg, string Tile, string Accent,
        string Text, string Muted, string Category);

    public record PackFile(string Franchise, int Version, ThemeEntry[] Themes);

    // ------------------------------------------------------------------
    // Manifest
    // ------------------------------------------------------------------

    public static async Task<Manifest?> FetchManifestAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(BaseUrl + "manifest.json");
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<Manifest>(json, opts);
        }
        catch { return null; }
    }

    // ------------------------------------------------------------------
    // Install / remove
    // ------------------------------------------------------------------

    public static HashSet<string> GetInstalledPackIds()
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(PackFolder)) return ids;
        foreach (var f in Directory.GetFiles(PackFolder, "*.json"))
            ids.Add(Path.GetFileNameWithoutExtension(f));
        return ids;
    }

    public static async Task<bool> DownloadPackAsync(string filename)
    {
        try
        {
            var json = await _http.GetStringAsync(BaseUrl + filename);
            // Validate it parses
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            JsonSerializer.Deserialize<PackFile>(json, opts);
            await File.WriteAllTextAsync(Path.Combine(PackFolder, filename), json);
            return true;
        }
        catch { return false; }
    }

    public static bool RemovePack(string packId)
    {
        var path = Path.Combine(PackFolder, packId + ".json");
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    // ------------------------------------------------------------------
    // Load themes from installed packs
    // ------------------------------------------------------------------

    public static List<ThemeEntry> LoadAllInstalledThemes()
    {
        var all = new List<ThemeEntry>();
        if (!Directory.Exists(PackFolder)) return all;

        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        foreach (var file in Directory.GetFiles(PackFolder, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var pack = JsonSerializer.Deserialize<PackFile>(json, opts);
                if (pack?.Themes != null)
                    all.AddRange(pack.Themes);
            }
            catch { /* skip corrupt files */ }
        }
        return all;
    }

    public static int InstalledThemeCount()
    {
        return LoadAllInstalledThemes().Count;
    }
}
