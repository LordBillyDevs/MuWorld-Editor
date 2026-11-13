using MuWorldEditor.Formats;
using MuWorldEditor.World;

namespace MuWorldEditor.Editing;

/// <summary>Que partes del area copiada se pegan.</summary>
public sealed class PasteOptions
{
    public bool Terrain = true;      // alturas
    public bool Tiles = true;        // las dos capas de piso y su mezcla
    public bool Attributes = true;   // banderas de celda
    public bool Objects = true;      // los objetos
    public bool Light;               // la luz del terreno, normalmente no
}

/// <summary>Lo que paso al pegar, para contarselo al usuario.</summary>
public sealed class PasteReport
{
    public int Cells, ObjectsPasted;
    public readonly List<string> CopiedFiles = new();
    public readonly List<string> Renamed = new();
    public bool SameMap;

    public string Describe()
    {
        var partes = new List<string> { $"{Cells} celdas", $"{ObjectsPasted} objetos" };
        if (CopiedFiles.Count > 0) partes.Add($"{CopiedFiles.Count} archivos copiados");
        if (Renamed.Count > 0) partes.Add($"{Renamed.Count} renombrados");
        return string.Join(", ", partes);
    }
}

public static class AreaPaste
{
    /// <summary>
    /// Pega el area en el mapa, con la esquina en la celda indicada. Si el mapa
    /// es otro, primero copia los .bmd y las texturas que hagan falta a su
    /// carpeta y renumera lo que sea necesario para no pisar nada.
    /// </summary>
    public static IUndoable Paste(AreaClip clip, MuWorld w, TileCatalog tiles, ObjectCatalog catalog,
                                  int destX, int destY, PasteOptions opt, out PasteReport report)
    {
        report = new PasteReport();
        if (clip == null || clip.IsEmpty) return null;

        string carpetaDestino = w.Directory;
        report.SameMap = string.Equals(
            Path.GetFullPath(clip.SourceFolder ?? ""),
            Path.GetFullPath(carpetaDestino ?? ""),
            StringComparison.OrdinalIgnoreCase);

        // ---- traducciones de numeros: del mapa de origen al de destino ----
        var tileMap = BuildTileMap(clip, tiles, carpetaDestino, report);
        var modelMap = BuildModelMap(clip, catalog, carpetaDestino, report);

        // ---- los cambios se guardan para poder deshacer ----
        var partes = new List<IUndoable>();
        var strokeH = new ArrayStroke<float>("Altura", w.Height);
        var strokeL1 = new ArrayStroke<byte>("Capa 1", w.Layer1);
        var strokeL2 = new ArrayStroke<byte>("Capa 2", w.Layer2);
        var strokeA = new ArrayStroke<byte>("Mezcla", w.Alpha);
        var strokeAtt = new ArrayStroke<ushort>("Atributos", w.Attribute);

        for (int y = 0; y < clip.Height; y++)
            for (int x = 0; x < clip.Width; x++)
            {
                int cx = destX + x, cy = destY + y;
                if (cx < 0 || cy < 0 || cx >= MuWorld.Size || cy >= MuWorld.Size) continue;

                int dst = MuWorld.Index(cx, cy);
                int src = y * clip.Width + x;
                report.Cells++;

                if (opt.Terrain)
                    SetHeight(strokeH, w, dst, clip.Heights[src]);

                if (opt.Tiles)
                {
                    Set(strokeL1, w.Layer1, dst, Traducir(tileMap, clip.Layer1[src]));
                    Set(strokeL2, w.Layer2, dst, Traducir(tileMap, clip.Layer2[src]));
                    Set(strokeA, w.Alpha, dst, clip.Alpha[src]);
                }

                if (opt.Attributes)
                {
                    ushort viejo = w.Attribute[dst];
                    if (viejo != clip.Attributes[src])
                    {
                        w.Attribute[dst] = clip.Attributes[src];
                        strokeAtt.Record(dst, viejo, clip.Attributes[src]);
                    }
                }

                if (opt.Light)
                {
                    w.Light[dst * 3] = clip.Light[src * 3];
                    w.Light[dst * 3 + 1] = clip.Light[src * 3 + 1];
                    w.Light[dst * 3 + 2] = clip.Light[src * 3 + 2];
                }
            }

        if (strokeH.Count > 0) partes.Add(strokeH);
        if (strokeL1.Count > 0) partes.Add(strokeL1);
        if (strokeL2.Count > 0) partes.Add(strokeL2);
        if (strokeA.Count > 0) partes.Add(strokeA);
        if (strokeAtt.Count > 0) partes.Add(strokeAtt);

        // ---- objetos ----
        if (opt.Objects && clip.Objects.Count > 0)
        {
            var agregados = new List<MapFiles.MapObject>();
            float baseX = destX * MuMath.TerrainScale;
            float baseY = destY * MuMath.TerrainScale;

            foreach (var co in clip.Objects)
            {
                float nx = baseX + co.OffsetX;
                float ny = baseY + co.OffsetY;
                if (nx < 0 || ny < 0 || nx >= MuWorld.Size * MuMath.TerrainScale
                                     || ny >= MuWorld.Size * MuMath.TerrainScale) continue;

                short tipo = co.Type;
                if (modelMap.TryGetValue(co.Type, out int nuevo)) tipo = (short)nuevo;

                agregados.Add(new MapFiles.MapObject
                {
                    Type = tipo,
                    X = nx,
                    Y = ny,
                    // se respeta la altura que tenia sobre el piso
                    Z = w.HeightAtWorld(nx, ny) + co.Z,
                    AngleX = co.AngleX, AngleY = co.AngleY, AngleZ = co.AngleZ,
                    Scale = co.Scale,
                    Extra = w.NewObjectExtra()
                });
            }

            if (agregados.Count > 0)
            {
                w.Objects.AddRange(agregados);
                partes.Add(new AddObjects(w.Objects, agregados));
                report.ObjectsPasted = agregados.Count;
            }
        }

        w.Dirty = true;
        if (partes.Count == 0) return null;
        return partes.Count == 1 ? partes[0] : new CompositeAction("Pegar area", partes);
    }

    /// <summary>Pega una altura redondeandola al formato del mapa de destino.</summary>
    private static void SetHeight(ArrayStroke<float> stroke, MuWorld w, int index, float value)
    {
        float viejo = w.Height[index];
        float nuevo = w.Quantize(value);
        if (viejo == nuevo) return;
        w.Height[index] = nuevo;
        stroke.Record(index, viejo, nuevo);
    }

    private static void Set(ArrayStroke<byte> stroke, byte[] target, int index, byte value)
    {
        byte viejo = target[index];
        if (viejo == value) return;
        target[index] = value;
        stroke.Record(index, viejo, value);
    }

    private static byte Traducir(Dictionary<int, int> mapa, byte id) =>
        id != TileCatalog.None && mapa.TryGetValue(id, out int nuevo) ? (byte)nuevo : id;

    /// <summary>
    /// Lleva las texturas de piso al mapa destino y devuelve, para cada numero
    /// del mapa de origen, que numero le toca en el destino.
    /// </summary>
    private static Dictionary<int, int> BuildTileMap(AreaClip clip, TileCatalog tiles,
                                                     string carpetaDestino, PasteReport report)
    {
        var mapa = new Dictionary<int, int>();
        if (report.SameMap) return mapa;      // mismo mapa: los numeros ya sirven

        foreach (var (idOrigen, archivoOrigen) in clip.TileFiles)
        {
            string nombre = Path.GetFileNameWithoutExtension(archivoOrigen);

            // si el destino ya tiene una textura con ese nombre, se usa esa
            var existente = tiles.Tiles.Values.FirstOrDefault(
                t => t.File != null &&
                     string.Equals(Path.GetFileNameWithoutExtension(t.File), nombre, StringComparison.OrdinalIgnoreCase));

            if (existente != null)
            {
                mapa[idOrigen] = existente.Id;
                continue;
            }

            // si no, se copia el archivo y se le da un numero libre
            string destino = AreaClip.CopyAsset(archivoOrigen, carpetaDestino, out bool copiado);
            if (destino == null) continue;
            if (copiado) report.CopiedFiles.Add(Path.GetFileName(destino));
            if (!string.Equals(Path.GetFileName(destino), Path.GetFileName(archivoOrigen), StringComparison.OrdinalIgnoreCase))
                report.Renamed.Add(Path.GetFileName(archivoOrigen) + " -> " + Path.GetFileName(destino));

            int libre = NumeroLibreDeTile(tiles);
            if (libre < 0) continue;

            tiles.Tiles[libre] = new TileCatalog.Tile
            {
                Id = libre,
                Name = Path.GetFileNameWithoutExtension(destino),
                File = destino,
                RepeatCells = tiles.Get(idOrigen)?.RepeatCells ?? 2f,
                Guessed = true
            };
            mapa[idOrigen] = libre;
        }
        return mapa;
    }

    private static int NumeroLibreDeTile(TileCatalog tiles)
    {
        for (int i = TileCatalog.ExtBase; i < 255; i++)
            if (!tiles.Tiles.ContainsKey(i)) return i;
        for (int i = 0; i < 255; i++)
            if (!tiles.Tiles.ContainsKey(i)) return i;
        return -1;
    }

    /// <summary>
    /// Lleva los modelos al mapa destino (con sus imagenes) y devuelve, para
    /// cada tipo del origen, que tipo le toca en el destino.
    /// </summary>
    private static Dictionary<int, int> BuildModelMap(AreaClip clip, ObjectCatalog catalog,
                                                      string carpetaDestino, PasteReport report)
    {
        var mapa = new Dictionary<int, int>();
        if (report.SameMap) return mapa;

        string carpetaDestinoModelos = catalog?.WriteFolder() ?? carpetaDestino;

        foreach (var (tipoOrigen, bmdOrigen) in clip.ModelFiles)
        {
            string nombre = Path.GetFileName(bmdOrigen);

            // si el destino ya conoce ese modelo, se reusa su tipo
            var existente = catalog.Entries.Values.FirstOrDefault(
                e => e.FullPath != null &&
                     string.Equals(Path.GetFileName(e.FullPath), nombre, StringComparison.OrdinalIgnoreCase));

            if (existente != null)
            {
                mapa[tipoOrigen] = existente.Id;
                continue;
            }

            var copiados = new List<string>();
            string destino = AreaClip.CopyModelWithTextures(
                bmdOrigen, clip.SourceFolder, carpetaDestinoModelos, copiados);
            if (destino == null) continue;

            report.CopiedFiles.AddRange(copiados);
            if (!string.Equals(Path.GetFileName(destino), nombre, StringComparison.OrdinalIgnoreCase))
                report.Renamed.Add(nombre + " -> " + Path.GetFileName(destino));

            int libre = catalog.FreeTypeId();
            var entry = catalog.SetEntry(libre, Path.GetFileNameWithoutExtension(destino), destino);
            entry.Guessed = true;
            mapa[tipoOrigen] = libre;
        }
        return mapa;
    }
}
