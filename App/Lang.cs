namespace MuWorldEditor.App;

/// <summary>
/// Textos de la interfaz. El espanol va escrito en el propio codigo como valor
/// por defecto; los demas idiomas viven en archivos "clave=texto" (Lang/en.txt)
/// que viajan dentro del .exe y tambien se pueden poner sueltos al lado del
/// programa para corregirlos o para agregar un idioma nuevo.
/// </summary>
public static class Lang
{
    public const string Spanish = "es";
    public const string English = "en";

    private static string _current = Spanish;
    private static Dictionary<string, string> _table = new();

    public static string Current => _current;

    public static IEnumerable<(string code, string name)> Available => new[]
    {
        (Spanish, "Espanol"),
        (English, "English")
    };

    public static void Use(string code)
    {
        _current = string.IsNullOrWhiteSpace(code) ? Spanish : code.Trim().ToLowerInvariant();
        _table = _current == Spanish ? new Dictionary<string, string>() : LoadTable(_current);
    }

    /// <summary>
    /// Devuelve el texto de la clave en el idioma activo. Si no esta traducido,
    /// se usa el texto en espanol que viene en la llamada.
    /// </summary>
    public static string T(string key, string spanish)
    {
        if (_current == Spanish) return spanish;
        return _table.TryGetValue(key, out var v) && !string.IsNullOrEmpty(v) ? v : spanish;
    }

    private static Dictionary<string, string> LoadTable(string code)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string content = Embedded.ReadText("Lang." + code + ".txt");
        if (content == null) return dict;

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim().Replace("\\n", Environment.NewLine);
            if (key.Length > 0) dict[key] = value;
        }
        return dict;
    }
}
