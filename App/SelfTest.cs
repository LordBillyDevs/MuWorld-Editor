using System.Text;
using MuWorldEditor.Formats;

namespace MuWorldEditor.App;

/// <summary>
/// Prueba de seguridad: lee y vuelve a generar cada archivo de mapa en memoria
/// y compara byte por byte con el original. Si algo no coincide, el editor no
/// deberia escribir ese archivo. No modifica nada en disco.
/// </summary>
public static class SelfTest
{
    public static void Run(string dataPath)
    {
        var sb = new StringBuilder();
        int ok = 0, fail = 0, models = 0, modelFail = 0;

        foreach (var dir in Directory.GetDirectories(dataPath, "World*").OrderBy(d => d))
        {
            string name = Path.GetFileName(dir);
            if (!int.TryParse(name.AsSpan(5), out int n)) continue;

            foreach (var f in Directory.GetFiles(dir, "*.att").Concat(Directory.GetFiles(dir, "*.map")).Concat(Directory.GetFiles(dir, "*.obj")))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (!Path.GetFileName(f).StartsWith("EncTerrain", StringComparison.OrdinalIgnoreCase)) continue;
                if (Path.GetFileName(f).Contains("KB.", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    var original = File.ReadAllBytes(f);
                    byte[] rebuilt = ext switch
                    {
                        ".att" => RebuildAtt(f),
                        ".map" => RebuildMap(f),
                        ".obj" => RebuildObj(f),
                        _ => null
                    };

                    if (rebuilt == null) continue;
                    if (rebuilt.Length == original.Length && rebuilt.AsSpan().SequenceEqual(original))
                        ok++;
                    else
                    {
                        fail++;
                        sb.AppendLine($"DIFERENTE  {name}/{Path.GetFileName(f)}  original={original.Length} generado={rebuilt.Length}");
                    }
                }
                catch (Exception ex)
                {
                    fail++;
                    sb.AppendLine($"ERROR      {name}/{Path.GetFileName(f)}  {ex.Message}");
                }
            }
        }

        foreach (var dir in Directory.GetDirectories(dataPath, "Object*").OrderBy(d => d))
            foreach (var f in Directory.GetFiles(dir, "*.bmd"))
            {
                try { BmdModel.Load(f); models++; }
                catch (Exception ex) { modelFail++; sb.AppendLine($"BMD ERROR  {Path.GetFileName(dir)}/{Path.GetFileName(f)}  {ex.Message}"); }
            }

        sb.Insert(0, $"Mapas verificados: {ok} identicos, {fail} con problemas\r\n" +
                     $"Modelos leidos: {models} bien, {modelFail} con error\r\n\r\n");

        // en la carpeta desde donde se ejecuta, para no ensuciar la del programa
        string outPath = Path.Combine(Directory.GetCurrentDirectory(), "selftest.txt");
        File.WriteAllText(outPath, sb.ToString());
    }

    private static byte[] RebuildAtt(string path)
    {
        var att = MapFiles.ReadAtt(path);
        string tmp = Path.GetTempFileName();
        MapFiles.WriteAtt(tmp, att);
        var b = File.ReadAllBytes(tmp);
        File.Delete(tmp);
        return b;
    }

    private static byte[] RebuildMap(string path)
    {
        var m = MapFiles.ReadMap(path);
        string tmp = Path.GetTempFileName();
        MapFiles.WriteMap(tmp, m);
        var b = File.ReadAllBytes(tmp);
        File.Delete(tmp);
        return b;
    }

    private static byte[] RebuildObj(string path)
    {
        var o = MapFiles.ReadObj(path);
        string tmp = Path.GetTempFileName();
        MapFiles.WriteObj(tmp, o);
        var b = File.ReadAllBytes(tmp);
        File.Delete(tmp);
        return b;
    }
}
