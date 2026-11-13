using MuWorldEditor.Formats;

namespace MuWorldEditor.World;

/// <summary>Todo el contenido editable de un mundo de MU, ya en memoria.</summary>
public sealed class MuWorld
{
    public const int Size = MapFiles.Size;
    public const int Cells = MapFiles.Cells;

    public string Directory = "";
    public int WorldNumber;

    /// <summary>Altura de cada celda, ya en unidades de mundo.</summary>
    public float[] Height = new float[Cells];
    /// <summary>
    /// True si el TerrainHeight.OZB es el de 24 bits de los mapas nuevos
    /// (alturas enteras desde -500). False = clasico de 8 bits (pasos de 1.5).
    /// </summary>
    public bool HeightExtended;
    public ushort[] Attribute = new ushort[Cells];
    public byte[] Layer1 = new byte[Cells];
    public byte[] Layer2 = new byte[Cells];
    public byte[] Alpha = new byte[Cells];
    public byte[] Light = new byte[Cells * 3];          // RGB por celda
    public List<MapFiles.MapObject> Objects = new();

    public bool AttExtended = true;
    public bool Dirty;
    /// <summary>El .obj vino dañado: se puede mirar pero no se reescribe.</summary>
    public bool ObjectsReadOnly;

    // cabeceras originales, para reescribir los archivos igual que como vinieron
    private byte[] _ozbHeader;
    private byte _attVersion, _attMapNumber, _attW = 255, _attH = 255;
    private byte _mapVersion, _mapNumber;
    private byte _objVersion, _objMapNumber;
    private int _objRecordSize = MapFiles.ObjBaseSize;
    private byte[] _objDefaultExtra = Array.Empty<byte>();
    private ModulusCrypto.Header _attModulus, _mapModulus, _objModulus;
    private bool _objInnerClassic;

    /// <summary>Que formato trae cada archivo, para mostrarlo en pantalla.</summary>
    public string FormatSummary
    {
        get
        {
            var parts = new List<string>();
            if (_mapModulus != null || _attModulus != null || _objModulus != null) parts.Add("Modulus");
            if (_objVersion > 0) parts.Add("OBJ v" + _objVersion);
            if (HeightExtended) parts.Add("altura 24 bits");
            return parts.Count == 0 ? "clasico" : string.Join(", ", parts);
        }
    }

    /// <summary>Altura mas baja y mas alta que admite el formato del mapa.</summary>
    public float MinHeight => HeightExtended ? MapFiles.ExtendedHeightOffset : 0f;
    public float MaxHeight => HeightExtended ? 5000f : 255 * MuMath.HeightScale;

    /// <summary>Redondea una altura al paso que se puede guardar en este mapa.</summary>
    public float Quantize(float height) => MapFiles.QuantizeHeight(height, HeightExtended);

    /// <summary>Extra por defecto para un objeto nuevo en este .obj.</summary>
    public byte[] NewObjectExtra() => _objDefaultExtra.Length == 0 ? null : (byte[])_objDefaultExtra.Clone();

    public string AttPath, MapPath, ObjPath, OzbPath;
    public string LightPath;

    public static int Index(int x, int y) => y * Size + x;

    /// <summary>
    /// Averigua a que numero de mundo pertenece una carpeta, mirando el nombre
    /// de sus archivos (EncTerrain12.map) o el de la carpeta (World12).
    /// </summary>
    public static int? DetectWorldNumber(string folder)
    {
        if (!System.IO.Directory.Exists(folder)) return null;

        foreach (var pattern in new[] { "*.map", "*.att", "*.obj" })
            foreach (var f in System.IO.Directory.GetFiles(folder, pattern))
            {
                var name = Path.GetFileNameWithoutExtension(f);
                var m = System.Text.RegularExpressions.Regex.Match(
                    name, @"(?:EncTerrain|Terrain)(\d+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (m.Success && int.TryParse(m.Groups[1].Value, out int n) && n >= 0) return n;
            }

        var dirName = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
        var md = System.Text.RegularExpressions.Regex.Match(
            dirName ?? "", @"World(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (md.Success && int.TryParse(md.Groups[1].Value, out int dn)) return dn;

        return null;
    }

    /// <summary>Que archivos de mapa hay en una carpeta.</summary>
    public static bool LooksLikeWorld(string folder)
    {
        if (!System.IO.Directory.Exists(folder)) return false;
        int? worldNumber = DetectWorldNumber(folder);
        return worldNumber.HasValue && HasMapForWorld(folder, worldNumber.Value);
    }

    /// <summary>Comprueba que la carpeta contiene el mapa canonico del mundo indicado.</summary>
    public static bool HasMapForWorld(string folder, int worldNumber)
    {
        if (!System.IO.Directory.Exists(folder)) return false;
        return FindFile(folder,
                        "EncTerrain" + worldNumber + ".map",
                        "Terrain" + worldNumber + ".map") != null;
    }

    public float HeightAt(int x, int y)
    {
        x = MuMath.Clamp(x, 0, Size - 1);
        y = MuMath.Clamp(y, 0, Size - 1);
        return Height[Index(x, y)];
    }

    /// <summary>Altura interpolada en coordenadas de mundo (0..25600).</summary>
    public float HeightAtWorld(float wx, float wy)
    {
        float fx = wx / MuMath.TerrainScale;
        float fy = wy / MuMath.TerrainScale;
        int x = (int)MathF.Floor(fx), y = (int)MathF.Floor(fy);
        float dx = fx - x, dy = fy - y;

        float h00 = HeightAt(x, y), h10 = HeightAt(x + 1, y);
        float h01 = HeightAt(x, y + 1), h11 = HeightAt(x + 1, y + 1);
        return (h00 * (1 - dx) + h10 * dx) * (1 - dy) + (h01 * (1 - dx) + h11 * dx) * dy;
    }

    // ------------------------------------------------------------------ IO
    public static MuWorld Load(string directory, int worldNumber)
    {
        var w = new MuWorld { Directory = directory, WorldNumber = worldNumber };

        w.AttPath = FindFile(directory, "EncTerrain" + worldNumber + ".att", "Terrain" + worldNumber + ".att");
        w.MapPath = FindFile(directory, "EncTerrain" + worldNumber + ".map", "Terrain" + worldNumber + ".map");
        w.ObjPath = FindFile(directory, "EncTerrain" + worldNumber + ".obj", "Terrain" + worldNumber + ".obj");
        w.OzbPath = FindFile(directory, "TerrainHeight.OZB");
        w.LightPath = FindFile(directory, "TerrainLight.OZJ", "TerrainLight.jpg", "TerrainLight.OZB");

        if (w.MapPath == null)
            throw new InvalidDataException(
                $"No se encontro EncTerrain{worldNumber}.map ni Terrain{worldNumber}.map en {directory}");

        if (w.AttPath != null)
        {
            var att = MapFiles.ReadAtt(w.AttPath);
            w.Attribute = att.Data;
            w.AttExtended = att.Extended;
            w._attVersion = att.Version; w._attMapNumber = att.MapNumber;
            w._attW = att.Width; w._attH = att.Height;
            w._attModulus = att.Modulus;
        }

        if (w.MapPath != null)
        {
            var map = MapFiles.ReadMap(w.MapPath);
            w.Layer1 = map.Layer1; w.Layer2 = map.Layer2; w.Alpha = map.Alpha;
            w._mapVersion = map.Version; w._mapNumber = map.MapNumber;
            w._mapModulus = map.Modulus;
        }

        if (w.ObjPath != null)
        {
            var obj = MapFiles.ReadObj(w.ObjPath);
            w.Objects = obj.Objects;
            w.ObjectsReadOnly = obj.Truncated;
            w._objVersion = obj.Version; w._objMapNumber = obj.MapNumber;
            w._objRecordSize = obj.RecordSize;
            w._objDefaultExtra = obj.DefaultExtra;
            w._objModulus = obj.Modulus;
            w._objInnerClassic = obj.ModulusInnerClassic;
        }

        if (w.OzbPath != null)
        {
            var h = MapFiles.ReadOzb(w.OzbPath);
            w.Height = h.Height;
            w.HeightExtended = h.Extended;
            w._ozbHeader = h.Header;
        }

        w.LoadLight();
        return w;
    }

    private void LoadLight()
    {
        for (int i = 0; i < Light.Length; i++) Light[i] = 255;
        if (LightPath == null) return;
        try
        {
            var tex = MuTexture.Load(LightPath);
            if (tex.Width < Size || tex.Height < Size) return;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int s = (y * tex.Width + x) * 4, d = (y * Size + x) * 3;
                    Light[d] = tex.Pixels[s];
                    Light[d + 1] = tex.Pixels[s + 1];
                    Light[d + 2] = tex.Pixels[s + 2];
                }
        }
        catch { /* sin lightmap se dibuja plano */ }
    }

    public void Save(bool backup = true)
    {
        if (AttPath != null)
        {
            Backup(AttPath, backup);
            MapFiles.WriteAtt(AttPath, new MapFiles.AttributeFile
            {
                Version = _attVersion,
                MapNumber = _attMapNumber,
                Width = _attW,
                Height = _attH,
                Extended = AttExtended,
                Data = Attribute,
                Modulus = _attModulus
            });
        }

        if (MapPath != null)
        {
            Backup(MapPath, backup);
            MapFiles.WriteMap(MapPath, new MapFiles.MappingFile
            {
                Version = _mapVersion,
                MapNumber = _mapNumber,
                Layer1 = Layer1,
                Layer2 = Layer2,
                Alpha = Alpha,
                Modulus = _mapModulus
            });
        }

        if (ObjPath != null && !ObjectsReadOnly)
        {
            Backup(ObjPath, backup);
            MapFiles.WriteObj(ObjPath, new MapFiles.ObjectFile
            {
                Version = _objVersion,
                MapNumber = _objMapNumber,
                Objects = Objects,
                RecordSize = _objRecordSize,
                DefaultExtra = _objDefaultExtra,
                Modulus = _objModulus,
                ModulusInnerClassic = _objInnerClassic
            });
        }

        if (OzbPath != null && _ozbHeader != null)
        {
            Backup(OzbPath, backup);
            MapFiles.WriteOzb(OzbPath, new MapFiles.HeightFile { Header = _ozbHeader, Height = Height, Extended = HeightExtended });
        }

        Dirty = false;
    }

    private static void Backup(string path, bool enabled)
    {
        if (!enabled || !File.Exists(path)) return;
        string dir = Path.Combine(Path.GetDirectoryName(path)!, "_backup");
        System.IO.Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string dst = Path.Combine(dir, Path.GetFileName(path) + "." + stamp + ".bak");
        if (!File.Exists(dst)) File.Copy(path, dst);
    }

    private static string FindFile(string dir, params string[] names)
    {
        foreach (var n in names)
        {
            string p = Path.Combine(dir, n);
            if (File.Exists(p)) return p;
            // el sistema de archivos de Windows no distingue mayusculas, pero
            // por las dudas buscamos tambien con comodin
            var hits = System.IO.Directory.GetFiles(dir, n);
            if (hits.Length > 0) return hits[0];
        }
        return null;
    }
}

/// <summary>Caja que sostiene el mundo activo, para que el resto no guarde referencias viejas.</summary>
public sealed class WorldRef
{
    public MuWorld World = new();
}
