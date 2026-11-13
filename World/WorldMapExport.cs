using System.Drawing.Imaging;
using MuWorldEditor.Formats;

namespace MuWorldEditor.World;

/// <summary>
/// Saca una imagen del mapa entero visto desde arriba: sirve de minimapa y para
/// ver de un vistazo como quedo el terreno, los atributos o las alturas.
/// </summary>
public static class WorldMapExport
{
    public enum Layer { Tiles, Height, Attributes }

    public static Bitmap Render(MuWorld w, TileCatalog tiles, Layer layer, int scale = 4)
    {
        int size = MuWorld.Size * scale;
        var bmp = new Bitmap(size, size, PixelFormat.Format24bppRgb);

        var data = bmp.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
        var row = new byte[data.Stride];

        // las alturas se pasan a 0..255 segun el rango del propio mapa
        float hLo = 0f, hHi = 255 * MuMath.HeightScale;
        if (w.HeightExtended && w.Height.Length > 0)
        {
            hLo = w.Height.Min(); hHi = w.Height.Max();
            if (hHi - hLo < 1f) hHi = hLo + 1f;
        }
        byte H8(float h) => (byte)Math.Clamp((h - hLo) / (hHi - hLo) * 255f, 0f, 255f);

        for (int py = 0; py < size; py++)
        {
            int y = py / scale;
            for (int px = 0; px < size; px++)
            {
                int x = px / scale;
                int i = MuWorld.Index(x, y);
                Color c = layer switch
                {
                    Layer.Height => HeightColor(H8(w.Height[i])),
                    Layer.Attributes => AttributeColor(w.Attribute[i], w.Light, i),
                    _ => TileColor(w, tiles, i)
                };
                row[px * 3 + 0] = c.B;
                row[px * 3 + 1] = c.G;
                row[px * 3 + 2] = c.R;
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + py * data.Stride, data.Stride);
        }
        bmp.UnlockBits(data);

        if (layer == Layer.Tiles) DrawObjects(bmp, w, scale);
        return bmp;
    }

    private static Color HeightColor(byte h)
    {
        int v = h;
        return Color.FromArgb(v, (int)(v * 0.85f), 255 - v);
    }

    private static Color AttributeColor(ushort att, byte[] light, int i)
    {
        var baseColor = Color.FromArgb(light[i * 3] / 3, light[i * 3 + 1] / 3, light[i * 3 + 2] / 3);
        if (att == 0) return baseColor;
        var c = AttributeInfo.ColorOf(att);
        if (c.A == 0) return baseColor;
        return Color.FromArgb((c.R + baseColor.R) / 2, (c.G + baseColor.G) / 2, (c.B + baseColor.B) / 2);
    }

    /// <summary>Color aproximado del piso: el tono medio del tile por la luz de la celda.</summary>
    private static Color TileColor(MuWorld w, TileCatalog tiles, int i)
    {
        var tint = TileTint(tiles, w.Layer1[i]);
        if (w.Layer2[i] != TileCatalog.None && w.Alpha[i] > 8)
        {
            var second = TileTint(tiles, w.Layer2[i]);
            float a = w.Alpha[i] / 255f;
            tint = Color.FromArgb(
                (int)(tint.R * (1 - a) + second.R * a),
                (int)(tint.G * (1 - a) + second.G * a),
                (int)(tint.B * (1 - a) + second.B * a));
        }

        float l = (w.Light[i * 3] + w.Light[i * 3 + 1] + w.Light[i * 3 + 2]) / 3f / 255f;
        l = Math.Clamp(l * 1.15f, 0.15f, 1.4f);
        return Color.FromArgb(
            Math.Clamp((int)(tint.R * l), 0, 255),
            Math.Clamp((int)(tint.G * l), 0, 255),
            Math.Clamp((int)(tint.B * l), 0, 255));
    }

    private static readonly Dictionary<int, Color> _tintCache = new();

    private static Color TileTint(TileCatalog tiles, int id)
    {
        if (_tintCache.TryGetValue(id, out var c)) return c;

        c = Color.FromArgb(110, 110, 110);
        var tile = tiles?.Get(id);
        if (tile?.File != null)
        {
            try
            {
                var tex = MuTexture.Load(tile.File);
                long r = 0, g = 0, b = 0; int n = 0;
                for (int p = 0; p < tex.Pixels.Length; p += 4 * 37)   // muestreo salteado
                {
                    r += tex.Pixels[p]; g += tex.Pixels[p + 1]; b += tex.Pixels[p + 2]; n++;
                }
                if (n > 0) c = Color.FromArgb((int)(r / n), (int)(g / n), (int)(b / n));
            }
            catch { }
        }
        _tintCache[id] = c;
        return c;
    }

    private static void DrawObjects(Bitmap bmp, MuWorld w, int scale)
    {
        using var g = Graphics.FromImage(bmp);
        using var brush = new SolidBrush(Color.FromArgb(150, 255, 240, 180));
        foreach (var o in w.Objects)
        {
            float px = o.X / MuMath.TerrainScale * scale;
            float py = o.Y / MuMath.TerrainScale * scale;
            g.FillRectangle(brush, px - 1, py - 1, 2, 2);
        }
    }

    public static void ClearCache() => _tintCache.Clear();
}
