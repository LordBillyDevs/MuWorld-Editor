using MuWorldEditor.Formats;

namespace MuWorldEditor.World;

/// <summary>
/// Traduce el numero de tipo guardado en el .obj al archivo .bmd que le
/// corresponde. La tabla vive en ObjectList/WorldN.csv (mismo formato que usa
/// el MWE: ID,Name,Filename,BBox) y se puede editar desde el propio editor.
/// Si falta, se arma sola a partir de lo que haya en la carpeta del cliente.
/// </summary>
public sealed class ObjectCatalog
{
    public sealed class Entry
    {
        public int Id;
        public string Name = "";
        public string FileName = "";
        public string FullPath;
        public bool Exists => FullPath != null;
        /// <summary>El numero lo eligio el editor: puede no coincidir con el cliente.</summary>
        public bool Guessed;
    }

    public readonly Dictionary<int, Entry> Entries = new();
    public string ClientDataPath = "";
    public int WorldNumber;
    public string ListPath;
    /// <summary>Carpeta del mapa abierto, cuando no esta dentro del cliente.</summary>
    public string MapFolder;

    public static string ListDirectory =>
        Path.Combine(AppContext.BaseDirectory, "ObjectList");

    public static ObjectCatalog Load(string clientDataPath, int worldNumber, IEnumerable<int> typesUsed,
                                    string mapFolder = null)
    {
        var cat = new ObjectCatalog
        {
            ClientDataPath = clientDataPath,
            WorldNumber = worldNumber,
            MapFolder = mapFolder
        };
        cat.ListPath = Path.Combine(ListDirectory, "World" + worldNumber + ".csv");

        string csv = App.Embedded.ReadText("ObjectList.World" + worldNumber + ".csv");
        if (csv != null) cat.ReadCsvText(csv);

        cat.ResolveFiles();
        cat.FillMissing(typesUsed);
        return cat;
    }

    private void ReadCsvText(string content)
    {
        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("ID,")) continue;

            var parts = line.Split(',');
            if (parts.Length < 3) continue;
            if (!int.TryParse(parts[0].Trim(), out int id)) continue;

            Entries[id] = new Entry
            {
                Id = id,
                Name = parts[1].Trim(),
                FileName = parts[2].Trim()
            };
        }
    }

    public IEnumerable<string> SearchDirs()
    {
        // primero donde esta el mapa: un mapa descargado suele traer sus modelos
        if (!string.IsNullOrEmpty(MapFolder))
        {
            yield return MapFolder;
            var padre = Path.GetDirectoryName(MapFolder);
            if (!string.IsNullOrEmpty(padre))
            {
                yield return Path.Combine(padre, "Object" + WorldNumber);
                yield return Path.Combine(padre, "World" + WorldNumber);
            }
        }
        yield return Path.Combine(ClientDataPath, "Object" + WorldNumber);
        yield return Path.Combine(ClientDataPath, "World" + WorldNumber);
        yield return Path.Combine(ClientDataPath, "Object1");
    }

    private void ResolveFiles()
    {
        foreach (var e in Entries.Values)
        {
            if (string.IsNullOrEmpty(e.FileName)) continue;
            foreach (var dir in SearchDirs())
            {
                if (!Directory.Exists(dir)) continue;
                string p = Path.Combine(dir, e.FileName);
                if (File.Exists(p)) { e.FullPath = p; break; }
                var hits = Directory.GetFiles(dir, Path.GetFileNameWithoutExtension(e.FileName) + ".bmd");
                if (hits.Length > 0) { e.FullPath = hits[0]; break; }
            }
        }
    }

    /// <summary>
    /// Suma al catalogo todos los .bmd que haya en las carpetas del mapa y no
    /// esten ya registrados. Asi los modelos nuevos que copies aparecen solos
    /// en la lista de objetos nuevos, sin tocar ningun archivo de listas.
    /// </summary>
    public int AddModelsFromDisk()
    {
        var yaUsados = new HashSet<string>(
            Entries.Values.Where(e => e.FullPath != null).Select(e => Path.GetFileName(e.FullPath)),
            StringComparer.OrdinalIgnoreCase);

        int agregados = 0;
        foreach (var ruta in AvailableModels())
        {
            string archivo = Path.GetFileName(ruta);
            if (yaUsados.Contains(archivo)) continue;

            int id = FreeTypeId();
            if (id > 4000) break;

            Entries[id] = new Entry
            {
                Id = id,
                Name = Path.GetFileNameWithoutExtension(ruta),
                FileName = archivo,
                FullPath = ruta,
                Guessed = true
            };
            yaUsados.Add(archivo);
            agregados++;
        }
        return agregados;
    }

    /// <summary>
    /// Para los tipos que el mapa usa pero la tabla no cubre, prueba el nombre
    /// clasico ObjectNN.bmd (NN = tipo + 1), que es como los guarda el cliente
    /// cuando no tienen nombre propio.
    /// </summary>
    private void FillMissing(IEnumerable<int> typesUsed)
    {
        foreach (int t in typesUsed.Distinct())
        {
            if (Entries.TryGetValue(t, out var have) && have.Exists) continue;

            string guess = "Object" + (t + 1).ToString("D2") + ".bmd";
            string found = null;
            foreach (var dir in SearchDirs())
            {
                if (!Directory.Exists(dir)) continue;
                var hits = Directory.GetFiles(dir, guess);
                if (hits.Length > 0) { found = hits[0]; break; }
            }

            if (!Entries.TryGetValue(t, out var e))
                Entries[t] = e = new Entry { Id = t, Name = "Object" + t, FileName = guess };
            if (found != null)
            {
                e.FullPath = found;
                if (string.IsNullOrEmpty(e.FileName)) e.FileName = Path.GetFileName(found);
            }
        }
    }

    public Entry Get(int type) => Entries.TryGetValue(type, out var e) ? e : null;

    /// <summary>Primer numero de tipo que no este usado, para un objeto nuevo.</summary>
    public int FreeTypeId()
    {
        for (int i = 0; i < 4000; i++)
            if (!Entries.ContainsKey(i)) return i;
        return Entries.Keys.Max() + 1;
    }

    /// <summary>Registra o reemplaza un tipo, apuntandolo a un archivo .bmd.</summary>
    public Entry SetEntry(int id, string name, string fullPath)
    {
        var e = new Entry
        {
            Id = id,
            Name = name,
            FileName = Path.GetFileName(fullPath),
            FullPath = File.Exists(fullPath) ? fullPath : null
        };
        Entries[id] = e;
        return e;
    }

    /// <summary>Donde conviene dejar los archivos nuevos de este mapa.</summary>
    public string WriteFolder()
    {
        foreach (var dir in SearchDirs())
            if (Directory.Exists(dir)) return dir;
        return MapFolder ?? ClientDataPath;
    }

    public string DisplayName(int type)
    {
        var e = Get(type);
        if (e == null) return "Tipo " + type;
        return string.IsNullOrEmpty(e.Name) ? "Tipo " + type : e.Name;
    }

    public void SaveCsv()
    {
        Directory.CreateDirectory(ListDirectory);
        using var w = new StreamWriter(ListPath);
        w.WriteLine("ID,Name,Filename,BBox");
        foreach (var e in Entries.Values.OrderBy(e => e.Id))
            w.WriteLine($"{e.Id},{e.Name},{e.FileName},0");
    }

    /// <summary>Modelos .bmd disponibles en las carpetas del mundo, para agregar objetos nuevos.</summary>
    public List<string> AvailableModels()
    {
        var list = new List<string>();
        foreach (var dir in SearchDirs())
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var f in Directory.GetFiles(dir, "*.bmd"))
            {
                string n = Path.GetFileName(f);
                if (!list.Any(x => string.Equals(Path.GetFileName(x), n, StringComparison.OrdinalIgnoreCase)))
                    list.Add(f);
            }
        }
        list.Sort((a, b) => string.Compare(Path.GetFileName(a), Path.GetFileName(b), StringComparison.OrdinalIgnoreCase));
        return list;
    }
}
