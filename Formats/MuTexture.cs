using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace MuWorldEditor.Formats;

/// <summary>
/// Imagen RGBA lista para subir a OpenGL. Lee los envoltorios de MU
/// (OZJ = 24 bytes + JPEG, OZT = 4 bytes + TGA, OZB = 4 bytes + BMP)
/// y tambien los formatos crudos.
/// </summary>
public sealed class MuTexture
{
    public int Width, Height;
    public byte[] Pixels = Array.Empty<byte>();   // RGBA
    public bool HasAlpha;

    /// <summary>Pasa la textura a una imagen normal, para mostrarla en pantalla.</summary>
    public Bitmap ToBitmap()
    {
        var bmp = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, Width, Height),
                                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var row = new byte[Width * 4];
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                int i = (y * Width + x) * 4;
                row[x * 4 + 0] = Pixels[i + 2];   // R G B A -> B G R A
                row[x * 4 + 1] = Pixels[i + 1];
                row[x * 4 + 2] = Pixels[i + 0];
                row[x * 4 + 3] = Pixels[i + 3];
            }
            Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, Width * 4);
        }
        bmp.UnlockBits(data);
        return bmp;
    }

    public static MuTexture Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        string ext = Path.GetExtension(path).ToLowerInvariant();

        switch (ext)
        {
            case ".ozj": return FromImageBytes(raw, 24);
            case ".ozb": return FromImageBytes(raw, 4);
            case ".ozt": return FromTga(raw, 4);
            case ".tga": return FromTga(raw, 0);
            default: return FromImageBytes(raw, 0);
        }
    }

    /// <summary>
    /// Busca la textura de un modelo: el .bmd nombra "tree.jpg" pero en disco
    /// esta como "tree.OZJ". Prueba todas las variantes razonables.
    /// </summary>
    public static string Resolve(string directory, string name, params string[] extraDirs)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string bare = Path.GetFileNameWithoutExtension(name);
        string ext = Path.GetExtension(name).ToLowerInvariant();

        var candidates = new List<string>();
        switch (ext)
        {
            case ".jpg": case ".jpeg": candidates.Add(bare + ".OZJ"); break;
            case ".tga": candidates.Add(bare + ".OZT"); break;
            case ".bmp": candidates.Add(bare + ".OZB"); break;
        }
        candidates.Add(name);
        candidates.AddRange(new[] { bare + ".OZJ", bare + ".OZT", bare + ".OZB",
                                    bare + ".jpg", bare + ".tga", bare + ".bmp", bare + ".png" });

        var dirs = new List<string> { directory };
        dirs.AddRange(extraDirs.Where(d => !string.IsNullOrEmpty(d)));

        foreach (var dir in dirs)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) continue;
            foreach (var c in candidates)
            {
                string p = Path.Combine(dir, c);
                if (File.Exists(p)) return p;
            }
        }
        return null;
    }

    private static MuTexture FromImageBytes(byte[] raw, int skip)
    {
        using var ms = new MemoryStream(raw, skip, raw.Length - skip);
        using var bmp = new Bitmap(ms);
        return FromBitmap(bmp);
    }

    public static MuTexture FromBitmap(Bitmap bmp)
    {
        var tex = new MuTexture { Width = bmp.Width, Height = bmp.Height };
        tex.Pixels = new byte[bmp.Width * bmp.Height * 4];

        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[Math.Abs(data.Stride)];
            for (int y = 0; y < bmp.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, bmp.Width * 4);
                for (int x = 0; x < bmp.Width; x++)
                {
                    int s = x * 4, d = (y * bmp.Width + x) * 4;
                    tex.Pixels[d + 0] = row[s + 2];  // B G R A -> R G B A
                    tex.Pixels[d + 1] = row[s + 1];
                    tex.Pixels[d + 2] = row[s + 0];
                    tex.Pixels[d + 3] = row[s + 3];
                    if (row[s + 3] != 255) tex.HasAlpha = true;
                }
            }
        }
        finally { bmp.UnlockBits(data); }
        return tex;
    }

    private static MuTexture FromTga(byte[] raw, int skip)
    {
        int p = skip;
        byte idLen = raw[p + 0];
        byte cmType = raw[p + 1];
        byte imgType = raw[p + 2];
        int cmLength = BitConverter.ToUInt16(raw, p + 5);
        byte cmDepth = raw[p + 7];
        int width = BitConverter.ToUInt16(raw, p + 12);
        int height = BitConverter.ToUInt16(raw, p + 14);
        byte bpp = raw[p + 16];
        byte descriptor = raw[p + 17];
        bool topDown = (descriptor & 0x20) != 0;

        int off = p + 18 + idLen;
        if (cmType == 1) off += cmLength * (cmDepth / 8);

        var tex = new MuTexture { Width = width, Height = height };
        tex.Pixels = new byte[width * height * 4];
        int bytes = bpp / 8;
        var pixel = new byte[4];
        int count = width * height;
        int written = 0;

        void Store(int index, byte[] px)
        {
            int x = index % width;
            int y = index / width;
            if (!topDown) y = height - 1 - y;
            int d = (y * width + x) * 4;
            tex.Pixels[d + 0] = px[2];
            tex.Pixels[d + 1] = px[1];
            tex.Pixels[d + 2] = px[0];
            tex.Pixels[d + 3] = bytes == 4 ? px[3] : (byte)255;
            if (tex.Pixels[d + 3] != 255) tex.HasAlpha = true;
        }

        if (imgType == 10 || imgType == 11)   // RLE
        {
            while (written < count && off < raw.Length)
            {
                byte header = raw[off++];
                int len = (header & 0x7F) + 1;
                if ((header & 0x80) != 0)
                {
                    Array.Copy(raw, off, pixel, 0, bytes); off += bytes;
                    for (int i = 0; i < len && written < count; i++) Store(written++, pixel);
                }
                else
                {
                    for (int i = 0; i < len && written < count; i++)
                    {
                        Array.Copy(raw, off, pixel, 0, bytes); off += bytes;
                        Store(written++, pixel);
                    }
                }
            }
        }
        else                                    // sin comprimir
        {
            for (; written < count && off + bytes <= raw.Length; written++)
            {
                Array.Copy(raw, off, pixel, 0, bytes); off += bytes;
                Store(written, pixel);
            }
        }
        return tex;
    }
}
