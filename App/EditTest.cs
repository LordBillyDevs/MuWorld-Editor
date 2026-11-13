using System.Text;
using MuWorldEditor.Editing;
using MuWorldEditor.Formats;
using MuWorldEditor.World;

namespace MuWorldEditor.App;

/// <summary>
/// Prueba automatica de la edicion: aplica cambios, deshace todo y comprueba
/// que el mapa quedo exactamente igual que al principio. Trabaja sobre una
/// copia, nunca sobre los archivos del cliente.
/// </summary>
public static class EditTest
{
    public static void Run(string dataPath, int world, string outFile)
    {
        var log = new StringBuilder();
        string src = Path.Combine(dataPath, "World" + world);
        string tmp = Path.Combine(Path.GetTempPath(), "mwe_test_World" + world);

        if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
        Directory.CreateDirectory(tmp);
        foreach (var f in Directory.GetFiles(src))
            File.Copy(f, Path.Combine(tmp, Path.GetFileName(f)));

        var w = MuWorld.Load(tmp, world);
        var history = new History();

        var heightBefore = (float[])w.Height.Clone();
        var attBefore = (ushort[])w.Attribute.Clone();
        var layer1Before = (byte[])w.Layer1.Clone();
        int objectsBefore = w.Objects.Count;

        // ---- altura ----
        var strokeH = new ArrayStroke<float>("Altura", w.Height);
        for (int y = 100; y < 110; y++)
            for (int x = 100; x < 110; x++)
            {
                int i = MuWorld.Index(x, y);
                float old = w.Height[i];
                float nv = w.Quantize(old + 12 * MuMath.HeightScale);
                w.Height[i] = nv;
                strokeH.Record(i, old, nv);
            }
        history.Push(strokeH);

        // ---- atributos ----
        var strokeA = new ArrayStroke<ushort>("Atributos", w.Attribute);
        for (int y = 50; y < 60; y++)
            for (int x = 50; x < 60; x++)
            {
                int i = MuWorld.Index(x, y);
                ushort old = w.Attribute[i];
                ushort nv = (ushort)(old | (ushort)TerrainFlag.SafeZone);
                w.Attribute[i] = nv;
                strokeA.Record(i, old, nv);
            }
        history.Push(strokeA);

        // ---- tiles ----
        var strokeT = new ArrayStroke<byte>("Tiles", w.Layer1);
        for (int i = 0; i < 500; i++)
        {
            byte old = w.Layer1[i];
            w.Layer1[i] = 7;
            strokeT.Record(i, old, 7);
        }
        history.Push(strokeT);

        // ---- objetos: agregar, mover, borrar ----
        var nuevo = new MapFiles.MapObject { Type = 0, X = 12000, Y = 12000, Z = 100, Scale = 1f, Extra = w.NewObjectExtra() };
        w.Objects.Add(nuevo);
        history.Push(new AddObjects(w.Objects, new List<MapFiles.MapObject> { nuevo }));

        var mover = w.Objects[0];
        var antes = new TransformObjects.State(mover);
        mover.X += 250; mover.AngleZ += 45;
        history.Push(new TransformObjects("Mover", new List<(MapFiles.MapObject, TransformObjects.State, TransformObjects.State)>
        {
            (mover, antes, new TransformObjects.State(mover))
        }));

        var borrar = w.Objects[5];
        int idx = w.Objects.IndexOf(borrar);
        w.Objects.Remove(borrar);
        history.Push(new RemoveObjects(w.Objects, new List<(int, MapFiles.MapObject)> { (idx, borrar) }));

        log.AppendLine($"Acciones apiladas: {history.Depth}");
        log.AppendLine($"Objetos tras editar: {w.Objects.Count} (antes {objectsBefore})");

        // ---- guardar con los cambios y volver a leer ----
        w.Save(backup: false);
        var reread = MuWorld.Load(tmp, world);
        bool sameHeight = reread.Height.AsSpan().SequenceEqual(w.Height);
        bool sameAtt = reread.Attribute.AsSpan().SequenceEqual(w.Attribute);
        bool sameLayer = reread.Layer1.AsSpan().SequenceEqual(w.Layer1);
        bool sameObjs = reread.Objects.Count == w.Objects.Count
                        && reread.Objects.Zip(w.Objects).All(p =>
                               p.First.Type == p.Second.Type &&
                               Math.Abs(p.First.X - p.Second.X) < 0.01f &&
                               Math.Abs(p.First.AngleZ - p.Second.AngleZ) < 0.01f);

        log.AppendLine($"Guardar y releer -> altura {(sameHeight ? "OK" : "FALLA")}, " +
                       $"atributos {(sameAtt ? "OK" : "FALLA")}, tiles {(sameLayer ? "OK" : "FALLA")}, " +
                       $"objetos {(sameObjs ? "OK" : "FALLA")}");

        // ---- deshacer todo ----
        while (history.CanUndo) history.Undo();

        bool undoHeight = w.Height.AsSpan().SequenceEqual(heightBefore);
        bool undoAtt = w.Attribute.AsSpan().SequenceEqual(attBefore);
        bool undoLayer = w.Layer1.AsSpan().SequenceEqual(layer1Before);
        bool undoObjs = w.Objects.Count == objectsBefore;

        log.AppendLine($"Deshacer todo -> altura {(undoHeight ? "OK" : "FALLA")}, " +
                       $"atributos {(undoAtt ? "OK" : "FALLA")}, tiles {(undoLayer ? "OK" : "FALLA")}, " +
                       $"objetos {(undoObjs ? "OK" : "FALLA")} ({w.Objects.Count} de {objectsBefore})");

        // ---- rehacer todo ----
        while (history.CanRedo) history.Redo();
        log.AppendLine($"Rehacer todo -> quedan {w.Objects.Count} objetos, " +
                       $"altura celda(105,105) = {w.Height[MuWorld.Index(105, 105)]} " +
                       $"(original {heightBefore[MuWorld.Index(105, 105)]})");

        // ---- volver a dejar el mapa original y comprobar byte a byte ----
        while (history.CanUndo) history.Undo();
        w.Save(backup: false);

        int identicos = 0, distintos = 0;
        foreach (var f in Directory.GetFiles(src, "EncTerrain*"))
        {
            string copia = Path.Combine(tmp, Path.GetFileName(f));
            if (!File.Exists(copia)) continue;
            if (File.ReadAllBytes(f).AsSpan().SequenceEqual(File.ReadAllBytes(copia))) identicos++;
            else { distintos++; log.AppendLine("  cambio: " + Path.GetFileName(f)); }
        }
        var ozb = Path.Combine(src, "TerrainHeight.OZB");
        var ozbCopy = Path.Combine(tmp, "TerrainHeight.OZB");
        if (File.Exists(ozb) && File.Exists(ozbCopy))
        {
            if (File.ReadAllBytes(ozb).AsSpan().SequenceEqual(File.ReadAllBytes(ozbCopy))) identicos++;
            else { distintos++; log.AppendLine("  cambio: TerrainHeight.OZB"); }
        }

        log.AppendLine($"Tras deshacer y guardar: {identicos} archivos identicos al original, {distintos} distintos");

        Directory.Delete(tmp, true);
        File.WriteAllText(outFile, log.ToString());
    }
}
