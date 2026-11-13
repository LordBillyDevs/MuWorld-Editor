using System.Text.Json;

namespace MuWorldEditor.App;

public sealed class Settings
{
    public string ClientDataPath { get; set; } = "";
    public int PanelWidth { get; set; } = 340;
    public string Language { get; set; } = "es";

    // ------------------------------------------------- ventana recordada
    public int WindowX { get; set; } = int.MinValue;
    public int WindowY { get; set; } = int.MinValue;
    public int WindowW { get; set; } = 1500;
    public int WindowH { get; set; } = 900;
    public bool Maximized { get; set; }
    public int ActiveTab { get; set; }
    public bool PanelHidden { get; set; }
    /// <summary>Usar la barra de titulo propia (ventana sin marco de Windows).</summary>
    public bool CustomTitleBar { get; set; } = true;
    public bool ShowMinimap { get; set; } = true;
    public bool ShowSky { get; set; } = true;
    public bool Antialias { get; set; } = true;
    public int TerrainShading { get; set; }
    public List<string> CollapsedSections { get; set; } = new();
    public List<RecentMap> Recent { get; set; } = new();

    /// <summary>Anota un mapa al principio de la lista de recientes.</summary>
    public void Touch(RecentMap m)
    {
        Recent.RemoveAll(r => string.Equals(r.Folder, m.Folder, StringComparison.OrdinalIgnoreCase));
        Recent.Insert(0, m);
        if (Recent.Count > 12) Recent.RemoveRange(12, Recent.Count - 12);
    }

    /// <summary>Carpeta donde se guardan las miniaturas de los mapas recientes.</summary>
    public static string ThumbDir
    {
        get
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MuWorldEditor", "thumbs");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static string ThumbPath(string folder)
    {
        uint h = 2166136261;
        foreach (char ch in (folder ?? "").ToLowerInvariant()) { h ^= ch; h *= 16777619; }
        return Path.Combine(ThumbDir, h.ToString("x8") + ".png");
    }

    // fuera de la carpeta del exe, para que el editor sea un unico archivo
    private static string Path0
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MuWorldEditor");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(Path0))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path0)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public static void Save(Settings s)
    {
        try { File.WriteAllText(Path0, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }
}

/// <summary>Un mapa abierto hace poco, para la pantalla de inicio.</summary>
public sealed class RecentMap
{
    public string Folder { get; set; } = "";
    public int World { get; set; }
    public string ClientDataPath { get; set; } = "";
    public string Name { get; set; } = "";
    public string Format { get; set; } = "";
    public DateTime Opened { get; set; } = DateTime.Now;
    /// <summary>Esta dentro de la carpeta Data del cliente (no es un mapa suelto).</summary>
    public bool FromClient { get; set; }
}

/// <summary>
/// Nombres de los mapas clasicos, solo para que la lista se lea comoda.
/// Se puede completar con un archivo WorldNames.csv al lado del editor.
/// </summary>
public static class WorldNames
{
    private static Dictionary<int, string> _names;

    private static readonly Dictionary<int, string> Known = new()
    {
        { 1, "Lorencia" }, { 2, "Dungeon" }, { 3, "Devias" }, { 4, "Noria" },
        { 5, "Lost Tower" }, { 6, "Exile" }, { 7, "Arena" }, { 8, "Atlans" },
        { 9, "Tarkan" }, { 10, "Devil Square" }, { 11, "Icarus" },
        { 12, "Blood Castle 1" }, { 13, "Blood Castle 2" }, { 14, "Blood Castle 3" },
        { 15, "Blood Castle 4" }, { 16, "Blood Castle 5" }, { 17, "Blood Castle 6" },
        { 18, "Chaos Castle 1" }, { 19, "Chaos Castle 2" }, { 20, "Chaos Castle 3" },
        { 21, "Chaos Castle 4" }, { 22, "Chaos Castle 5" }, { 23, "Chaos Castle 6" },
        { 24, "Kalima 1" }, { 25, "Kalima 2" }, { 26, "Kalima 3" }, { 27, "Kalima 4" },
        { 28, "Kalima 5" }, { 29, "Kalima 6" }, { 30, "Valley of Loren" },
        { 31, "Land of Trials" }, { 32, "Devil Square (alto)" }, { 33, "Aida" },
        { 34, "Crywolf" }, { 37, "Kanturu" }, { 38, "Kanturu Ruins" },
        { 39, "Kanturu Boss" }, { 41, "Balgass Barracks" }, { 42, "Balgass Refuge" },
        { 45, "Illusion Temple 1" }, { 46, "Illusion Temple 2" }, { 47, "Illusion Temple 3" },
        { 48, "Illusion Temple 4" }, { 49, "Illusion Temple 5" }, { 50, "Illusion Temple 6" },
        { 51, "Elbeland" }, { 57, "Swamp of Calmness" }, { 58, "Raklion" },
        { 63, "Santa Town" }, { 64, "Vulcanus" }, { 65, "Duel Arena" }
    };

    public static string Suffix(int world)
    {
        _names ??= LoadFile();
        if (_names.TryGetValue(world, out var n)) return "  -  " + n;
        if (Known.TryGetValue(world, out var k)) return "  -  " + k;
        return "";
    }

    private static Dictionary<int, string> LoadFile()
    {
        var d = new Dictionary<int, string>();
        string p = Path.Combine(AppContext.BaseDirectory, "WorldNames.csv");
        if (!File.Exists(p)) return d;
        foreach (var line in File.ReadAllLines(p))
        {
            var parts = line.Split(',');
            if (parts.Length >= 2 && int.TryParse(parts[0].Trim(), out int n))
                d[n] = parts[1].Trim();
        }
        return d;
    }
}
