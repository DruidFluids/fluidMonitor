using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Fluid.App.Models;

namespace Fluid.App.Services;

public static class SettingsService
{
    private static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "fluidMonitor");
    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    // v1.21.1: true when the last Load() found no usable settings.json (fresh
    // install or unrecoverable file). MainWindow uses this to center the
    // widget on the primary monitor instead of trusting the default coords.
    public static bool LastLoadWasFresh { get; private set; }

    public static AppSettings Load()
    {
        LastLoadWasFresh = false;
        try
        {
            AppSettings s;
            if (!File.Exists(FilePath))
            {
                // v1.21: fresh install -- no migrations apply, start at current.
                s = new AppSettings { SchemaVersion = AppSettings.CurrentSchemaVersion };
                LastLoadWasFresh = true;
            }
            else
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts);
                if (loaded == null)
                {
                    loaded = new AppSettings { SchemaVersion = AppSettings.CurrentSchemaVersion };
                    LastLoadWasFresh = true;
                }
                s = loaded;
            }
            MigrateSchema(s);
            NormalizeTileOrder(s);
            return s;
        }
        catch
        {
            LastLoadWasFresh = true;
            return new AppSettings();
        }
    }

    // v1.19: schema migrations run exactly once per upgrade. Each branch
    // bumps SchemaVersion so it won't fire again. Save() is called after
    // migration to persist the new schema marker.
    private static void MigrateSchema(AppSettings s)
    {
        bool dirty = false;

        // v1->v2: UserPresets semantics changed -- they now ONLY hold full
        // combo state (colors + skin + fonts), and the +button under Colors
        // saves to a separate CustomColors list. Per user instruction, we
        // wipe existing UserPresets on this upgrade so the user starts clean.
        if (s.SchemaVersion < 2)
        {
            s.UserPresets.Clear();
            // CustomColors didn't exist pre-v1.19; nothing to migrate INTO it.
            s.SchemaVersion = 2;
            dirty = true;
        }

        // v2->v3 (v1.23): Clock tile moves to the very top of the display
        // order, per user request. One-shot so a later drag-reorder that puts
        // the clock elsewhere is never overridden on subsequent launches.
        if (s.SchemaVersion < 3)
        {
            var dt = nameof(Fluid.Shared.Protocol.TileKind.DateTime);
            if (s.TileOrder.Remove(dt))
                s.TileOrder.Insert(0, dt);
            s.SchemaVersion = 3;
            dirty = true;
        }

        if (dirty)
        {
            // Persist immediately so the migration is one-shot
            try { File.WriteAllText(FilePath, JsonSerializer.Serialize(s, JsonOpts)); }
            catch { /* not fatal -- worst case migration re-runs next launch */ }
        }
    }

    // v1.18: ensure TileOrder contains every known TileKind exactly once.
    // Old settings.json files won't have TileOrder at all -- the property
    // defaults to the full list. New TileKinds added in future versions
    // get appended at the end so they show up but don't disturb the user's
    // existing ordering for kinds they've explicitly arranged.
    private static void NormalizeTileOrder(AppSettings s)
    {
        if (s.TileOrder == null)
            s.TileOrder = new System.Collections.Generic.List<string>();

        // Drop bogus entries (no matching enum value)
        var validKinds = System.Enum.GetNames(typeof(Fluid.Shared.Protocol.TileKind));
        s.TileOrder = s.TileOrder
            .Where(k => System.Array.IndexOf(validKinds, k) >= 0)
            .Distinct()
            .ToList();

        // Append any kinds missing from the order list
        foreach (var k in validKinds)
            if (!s.TileOrder.Contains(k))
                s.TileOrder.Add(k);
    }

    public static void Save(AppSettings s)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s, JsonOpts));
        }
        catch { /* not fatal */ }
    }
}
