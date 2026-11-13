using System.Reflection;

namespace MuWorldEditor.App;

/// <summary>
/// Archivos que viajan dentro del .exe. Si al lado del ejecutable hay una
/// version en disco con el mismo nombre, esa manda: asi se puede corregir una
/// lista de objetos sin recompilar.
/// </summary>
public static class Embedded
{
    public static string ReadText(string logicalName)
    {
        string onDisk = Path.Combine(AppContext.BaseDirectory, logicalName.Replace('.', Path.DirectorySeparatorChar));
        // el nombre logico de las listas es "ObjectList.WorldN.csv": solo el
        // primer punto es separador de carpeta
        int slash = logicalName.IndexOf('.');
        if (slash > 0)
        {
            string candidate = Path.Combine(AppContext.BaseDirectory,
                                            logicalName[..slash],
                                            logicalName[(slash + 1)..]);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        if (File.Exists(onDisk)) return File.ReadAllText(onDisk);

        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream(logicalName);
        if (s == null) return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    public static bool Exists(string logicalName) => ReadText(logicalName) != null;

    /// <summary>El icono del programa, sacado del main.exe del cliente.</summary>
    public static Icon AppIcon()
    {
        try
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico");
            return s != null ? new Icon(s) : null;
        }
        catch { return null; }
    }
}
