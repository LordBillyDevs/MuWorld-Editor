using MuWorldEditor.Formats;
using MuWorldEditor.World;

namespace MuWorldEditor.Editing;

/// <summary>
/// Un pedazo de mapa copiado: el terreno con sus dos capas, los atributos y los
/// objetos que hay adentro. Se lleva anotado de que archivos sale cada textura y
/// cada modelo, para poder pegarlo en otro mapa y copiar tambien esos archivos.
/// </summary>
public sealed class AreaClip
{
    public int Width, Height;          // en celdas

    /// <summary>Alturas en unidades de mundo.</summary>
    public float[] Heights;
    public byte[] Layer1, Layer2, Alpha;
    public ushort[] Attributes;
    public byte[] Light;               // rgb por celda

    public sealed class ClipObject
    {
        public short Type;
        public float OffsetX, OffsetY, Z;   // posicion relativa a la esquina del area
        public float AngleX, AngleY, AngleZ;
        public float Scale = 1f;
    }

    public readonly List<ClipObject> Objects = new();

    /// <summary>De donde salio cada numero de textura usado.</summary>
    public readonly Dictionary<int, string> TileFiles = new();

    /// <summary>De donde salio cada tipo de objeto usado: el .bmd.</summary>
    public readonly Dictionary<int, string> ModelFiles = new();

    /// <summary>Carpeta del mapa de origen, para buscar las texturas de los modelos.</summary>
    public string SourceFolder = "";
    public int SourceWorld;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public string Describe() =>
        $"{Width}x{Height} celdas, {Objects.Count} objetos, " +
        $"{TileFiles.Count} texturas, {ModelFiles.Count} modelos";

    // ----------------------------------------------------------------- copiar
    /// <summary>Toma un rectangulo de celdas del mapa y todo lo que haya adentro.</summary>
    public static AreaClip Copy(MuWorld w, TileCatalog tiles, ObjectCatalog catalog,
                                int x0, int y0, int x1, int y1)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (y1 < y0) (y0, y1) = (y1, y0);
        x0 = MuMath.Clamp(x0, 0, MuWorld.Size - 1);
        y0 = MuMath.Clamp(y0, 0, MuWorld.Size - 1);
        x1 = MuMath.Clamp(x1, 0, MuWorld.Size - 1);
        y1 = MuMath.Clamp(y1, 0, MuWorld.Size - 1);

        int ancho = x1 - x0 + 1, alto = y1 - y0 + 1;
        var clip = new AreaClip
        {
            Width = ancho,
            Height = alto,
            Heights = new float[ancho * alto],
            Layer1 = new byte[ancho * alto],
            Layer2 = new byte[ancho * alto],
            Alpha = new byte[ancho * alto],
            Attributes = new ushort[ancho * alto],
            Light = new byte[ancho * alto * 3],
            SourceFolder = w.Directory,
            SourceWorld = w.WorldNumber
        };

        for (int y = 0; y < alto; y++)
            for (int x = 0; x < ancho; x++)
            {
                int src = MuWorld.Index(x0 + x, y0 + y);
                int dst = y * ancho + x;

                clip.Heights[dst] = w.Height[src];
                clip.Layer1[dst] = w.Layer1[src];
                clip.Layer2[dst] = w.Layer2[src];
                clip.Alpha[dst] = w.Alpha[src];
                clip.Attributes[dst] = w.Attribute[src];
                clip.Light[dst * 3] = w.Light[src * 3];
                clip.Light[dst * 3 + 1] = w.Light[src * 3 + 1];
                clip.Light[dst * 3 + 2] = w.Light[src * 3 + 2];

                AnotarTile(clip, tiles, w.Layer1[src]);
                AnotarTile(clip, tiles, w.Layer2[src]);
            }

        // los objetos que caen dentro del rectangulo
        float mx0 = x0 * MuMath.TerrainScale, my0 = y0 * MuMath.TerrainScale;
        float mx1 = (x1 + 1) * MuMath.TerrainScale, my1 = (y1 + 1) * MuMath.TerrainScale;

        foreach (var o in w.Objects)
        {
            if (o.X < mx0 || o.X >= mx1 || o.Y < my0 || o.Y >= my1) continue;

            clip.Objects.Add(new ClipObject
            {
                Type = o.Type,
                OffsetX = o.X - mx0,
                OffsetY = o.Y - my0,
                Z = o.Z - w.HeightAtWorld(o.X, o.Y),   // altura respecto del piso
                AngleX = o.AngleX, AngleY = o.AngleY, AngleZ = o.AngleZ,
                Scale = o.Scale
            });

            var entry = catalog?.Get(o.Type);
            if (entry?.FullPath != null) clip.ModelFiles[o.Type] = entry.FullPath;
        }

        return clip;
    }

    private static void AnotarTile(AreaClip clip, TileCatalog tiles, byte id)
    {
        if (id == TileCatalog.None || clip.TileFiles.ContainsKey(id)) return;
        var t = tiles?.Get(id);
        if (t?.File != null) clip.TileFiles[id] = t.File;
    }

    // ------------------------------------------------------- copiar archivos
    /// <summary>
    /// Deja un archivo en la carpeta destino. Si ya hay uno con ese nombre y el
    /// contenido es el mismo, se reusa; si es distinto, se guarda como
    /// "nombre2", "nombre3"... para no pisar lo que ya estaba.
    /// </summary>
    public static string CopyAsset(string origen, string carpetaDestino, out bool copiado)
    {
        copiado = false;
        if (origen == null || !File.Exists(origen)) return null;
        Directory.CreateDirectory(carpetaDestino);

        string nombre = Path.GetFileNameWithoutExtension(origen);
        string ext = Path.GetExtension(origen);
        string destino = Path.Combine(carpetaDestino, nombre + ext);

        // ya esta ahi el mismo archivo: no hace falta copiar nada
        if (File.Exists(destino) && SameFile(origen, destino)) return destino;

        int n = 2;
        while (File.Exists(destino))
        {
            if (SameFile(origen, destino)) return destino;
            destino = Path.Combine(carpetaDestino, nombre + n + ext);
            n++;
        }

        File.Copy(origen, destino);
        copiado = true;
        return destino;
    }

    private static bool SameFile(string a, string b)
    {
        try
        {
            var fa = new FileInfo(a);
            var fb = new FileInfo(b);
            if (fa.Length != fb.Length) return false;
            return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
        }
        catch { return false; }
    }

    /// <summary>
    /// Copia el .bmd y todas las imagenes que ese modelo necesita. Devuelve la
    /// ruta del .bmd ya en la carpeta destino.
    /// </summary>
    public static string CopyModelWithTextures(string bmdOrigen, string carpetaOrigen,
                                               string carpetaDestino, List<string> copiados)
    {
        if (bmdOrigen == null || !File.Exists(bmdOrigen)) return null;

        string destino = CopyAsset(bmdOrigen, carpetaDestino, out bool seCopio);
        if (seCopio) copiados.Add(Path.GetFileName(destino));

        try
        {
            var modelo = BmdModel.Load(bmdOrigen);
            foreach (var mesh in modelo.Meshes)
            {
                string tex = MuTexture.Resolve(Path.GetDirectoryName(bmdOrigen), mesh.TexturePath, carpetaOrigen);
                if (tex == null) continue;

                // la textura tiene que conservar su nombre: el .bmd la busca asi
                string dst = Path.Combine(carpetaDestino, Path.GetFileName(tex));
                if (!File.Exists(dst))
                {
                    File.Copy(tex, dst);
                    copiados.Add(Path.GetFileName(dst));
                }
            }
        }
        catch { /* si el modelo no se puede leer, al menos queda el .bmd */ }

        return destino;
    }
}
