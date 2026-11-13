using MuWorldEditor.Formats;
using MuWorldEditor.Render;

namespace MuWorldEditor.World;

/// <summary>Texturas de piso de un mundo. Los nombres son los del cliente de MU.</summary>
public sealed class TileCatalog : IDisposable
{
    public sealed class Tile
    {
        public int Id;
        public string Name = "";
        public string File;
        public float RepeatCells = 4f;
        public GpuTexture Texture;
        public bool Loaded;
        /// <summary>El numero lo dedujo el editor: puede no coincidir con el cliente.</summary>
        public bool Guessed;
    }

    public const int None = 255;

    private static readonly (string name, string file, float repeat)[] Standard =
    {
        ("Grass 1",   "TileGrass01",  4f),
        ("Grass 2",   "TileGrass02",  2f),
        ("Ground 1",  "TileGround01", 4f),
        ("Ground 2",  "TileGround02", 2f),
        ("Ground 3",  "TileGround03", 2f),
        ("Water",     "TileWater01",  4f),
        ("Wood",      "TileWood01",   2f),
        ("Rock 1",    "TileRock01",   2f),
        ("Rock 2",    "TileRock02",   2f),
        ("Rock 3",    "TileRock03",   2f),
        ("Rock 4",    "TileRock04",   2f),
        ("Rock 5",    "TileRock05",   2f),
        ("Rock 6",    "TileRock06",   2f),
        ("Rock 7",    "TileRock07",   2f),
        ("Rock 8",    "TileRock08",   2f),
        ("Rock 9",    "TileRock09",   2f),
        ("Rock 10",   "TileRock10",   2f),
        ("Rock 11",   "TileRock11",   2f),
        ("Rock 12",   "TileRock12",   2f),
        ("Rock 13",   "TileRock13",   2f),
    };

    /// <summary>Numero con el que arrancan los tiles extra (ExtTile1 = este numero).</summary>
    public const int ExtBase = 20;

    public readonly Dictionary<int, Tile> Tiles = new();
    private readonly string _dir;
    private GpuTexture _fallback;

    /// <summary>Texturas que estan en la carpeta pero sin un numero conocido.</summary>
    public readonly List<string> Unassigned = new();

    public TileCatalog(string worldDir)
    {
        _dir = worldDir;

        // 1) los nombres que usa el cliente de MU, con su numero de siempre
        for (int i = 0; i < Standard.Length; i++)
        {
            var (name, file, repeat) = Standard[i];
            string path = MuTexture.Resolve(worldDir, file + ".jpg");
            Tiles[i] = new Tile { Id = i, Name = name, File = path, RepeatCells = repeat };
        }

        // 2) los extra: ExtTile1, ExtTile2... que es como los busca el cliente
        for (int i = 1; i <= 200; i++)
        {
            string path = MuTexture.Resolve(worldDir, "ExtTile" + i + ".jpg")
                       ?? MuTexture.Resolve(worldDir, "ExtTile" + i.ToString("D2") + ".jpg");
            if (path == null) continue;
            int id = ExtBase + i - 1;
            Tiles[id] = new Tile { Id = id, Name = "Ext " + i, File = path, RepeatCells = 2f };
        }

        // 3) cualquier otra textura de piso que este en la carpeta y no encaje
        //    arriba: se agrega igual, para poder verla y usarla
        ScanFolder(worldDir);

        // 4) la lista editable manda sobre todo lo anterior
        LoadList();
    }

    /// <summary>
    /// Busca en la carpeta del mapa todos los archivos de piso y suma los que
    /// no estaban. Asi aparecen solos los que agregues vos.
    /// </summary>
    private void ScanFolder(string worldDir)
    {
        if (!Directory.Exists(worldDir)) return;

        var yaUsados = new HashSet<string>(
            Tiles.Values.Where(t => t.File != null).Select(t => Path.GetFileNameWithoutExtension(t.File)),
            StringComparer.OrdinalIgnoreCase);

        var encontrados = new List<string>();
        foreach (var ext in new[] { "*.OZJ", "*.OZT", "*.jpg", "*.tga" })
            foreach (var f in Directory.GetFiles(worldDir, "Tile" + ext))
            {
                string nombre = Path.GetFileNameWithoutExtension(f);
                if (yaUsados.Contains(nombre)) continue;
                if (nombre.StartsWith("AlphaTile", StringComparison.OrdinalIgnoreCase)) continue;
                if (encontrados.Any(e => string.Equals(Path.GetFileNameWithoutExtension(e), nombre, StringComparison.OrdinalIgnoreCase)))
                    continue;
                encontrados.Add(f);
            }

        encontrados.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));

        // se les da el primer numero libre, asi quedan a continuacion de los de
        // siempre y no al final de la lista tras un salto de 200
        int libre = ExtBase;
        foreach (var f in encontrados)
        {
            while (Tiles.ContainsKey(libre) && libre < 255) libre++;
            if (libre >= 255) break;

            string nombre = Path.GetFileNameWithoutExtension(f);
            Tiles[libre] = new Tile { Id = libre, Name = nombre, File = f, RepeatCells = 2f, Guessed = true };
            Unassigned.Add(nombre);
            libre++;
        }
    }

    public static string ListDirectory => Path.Combine(AppContext.BaseDirectory, "TileList");

    public string ListPath { get; private set; }

    /// <summary>
    /// Lee TileList/<carpeta>.csv si existe: ahi se puede corregir a mano que
    /// numero le toca a cada textura, porque eso depende de cada cliente.
    /// </summary>
    private void LoadList()
    {
        string carpeta = Path.GetFileName(_dir?.TrimEnd(Path.DirectorySeparatorChar) ?? "");
        ListPath = Path.Combine(ListDirectory, carpeta + ".csv");
        if (!File.Exists(ListPath)) return;

        foreach (var raw in File.ReadAllLines(ListPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("ID,")) continue;

            var parts = line.Split(',');
            if (parts.Length < 3 || !int.TryParse(parts[0].Trim(), out int id)) continue;

            string archivo = parts[2].Trim();
            string ruta = MuTexture.Resolve(_dir, archivo) ?? MuTexture.Resolve(_dir, archivo + ".jpg");
            float repeat = parts.Length > 3 && float.TryParse(parts[3].Trim(), out float r) ? r : 2f;

            Tiles[id] = new Tile
            {
                Id = id,
                Name = parts[1].Trim(),
                File = ruta,
                RepeatCells = repeat
            };
        }
    }

    /// <summary>Escribe la lista actual para poder corregirla a mano.</summary>
    public void SaveList()
    {
        Directory.CreateDirectory(ListDirectory);
        using var w = new StreamWriter(ListPath);
        w.WriteLine("# Numero de textura, nombre y archivo. El numero es el que");
        w.WriteLine("# guarda el mapa y tiene que coincidir con el de tu cliente.");
        w.WriteLine("ID,Nombre,Archivo,Repeticion");
        foreach (var t in Tiles.Values.Where(t => t.File != null).OrderBy(t => t.Id))
            w.WriteLine($"{t.Id},{t.Name},{Path.GetFileName(t.File)},{t.RepeatCells}");
    }

    public Tile Get(int id) => Tiles.TryGetValue(id, out var t) ? t : null;

    public float RepeatOf(int id) => Get(id)?.RepeatCells ?? 4f;

    /// <summary>El agua se dibuja animada, como en el editor original.</summary>
    public bool IsWater(int id)
    {
        var t = Get(id);
        return t != null && t.File != null &&
               Path.GetFileName(t.File).StartsWith("TileWater", StringComparison.OrdinalIgnoreCase);
    }

    public GpuTexture TextureOf(int id)
    {
        var t = Get(id);
        if (t == null) return Fallback();
        if (!t.Loaded)
        {
            t.Loaded = true;
            if (t.File != null)
            {
                try { t.Texture = GpuTexture.FromMu(MuTexture.Load(t.File)); }
                catch { t.Texture = null; }
            }
        }
        return t.Texture ?? Fallback();
    }

    private GpuTexture Fallback() => _fallback ??= GpuTexture.Solid(120, 120, 120, 255);

    public IEnumerable<Tile> Available => Tiles.Values.Where(t => t.File != null).OrderBy(t => t.Id);

    public void Dispose()
    {
        foreach (var t in Tiles.Values) t.Texture?.Dispose();
        _fallback?.Dispose();
    }
}
