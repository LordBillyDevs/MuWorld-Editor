using System.Drawing.Imaging;

namespace MuWorldEditor.Formats;

/// <summary>
/// Guarda una imagen comun (PNG, JPG, BMP) en los formatos que lee el cliente
/// de MU: .OZJ es un JPEG con 24 bytes adelante y .OZT un TGA con 4 bytes.
/// </summary>
public static class TextureWriter
{
    /// <summary>
    /// Escribe la imagen en el formato que corresponde segun la extension del
    /// destino. Si el archivo ya existia, se conserva su encabezado y se hace
    /// una copia en _backup.
    /// </summary>
    public static void Save(Bitmap image, string destino)
    {
        string ext = Path.GetExtension(destino).ToLowerInvariant();
        Backup(destino);

        switch (ext)
        {
            case ".ozt": File.WriteAllBytes(destino, BuildOzt(image, destino)); break;
            case ".ozb": File.WriteAllBytes(destino, BuildOzb(image, destino)); break;
            case ".tga": File.WriteAllBytes(destino, BuildTga(image)); break;
            case ".png": image.Save(destino, ImageFormat.Png); break;
            case ".jpg": case ".jpeg": File.WriteAllBytes(destino, Jpeg(image)); break;
            default: File.WriteAllBytes(destino, BuildOzj(image, destino)); break;   // .ozj
        }
    }

    /// <summary>True si la imagen tiene pixeles transparentes.</summary>
    public static bool HasAlpha(Bitmap bmp)
    {
        if (!Image.IsAlphaPixelFormat(bmp.PixelFormat)) return false;
        for (int y = 0; y < bmp.Height; y += Math.Max(1, bmp.Height / 64))
            for (int x = 0; x < bmp.Width; x += Math.Max(1, bmp.Width / 64))
                if (bmp.GetPixel(x, y).A < 250) return true;
        return false;
    }

    /// <summary>
    /// Que extension le corresponde a una imagen: con transparencia va OZT,
    /// sin transparencia OZJ, que ocupa mucho menos.
    /// </summary>
    public static string BestExtension(Bitmap bmp) => HasAlpha(bmp) ? ".OZT" : ".OZJ";

    private static byte[] Jpeg(Bitmap bmp, long quality = 95)
    {
        using var ms = new MemoryStream();
        var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var pars = new EncoderParameters(1);
        pars.Param[0] = new EncoderParameter(Encoder.Quality, quality);

        // el JPEG no guarda transparencia: lo aplanamos sobre negro
        using var plano = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(plano))
        {
            g.Clear(Color.Black);
            g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
        }
        plano.Save(ms, enc, pars);
        return ms.ToArray();
    }

    private static byte[] BuildOzj(Bitmap bmp, string destino)
    {
        var jpeg = Jpeg(bmp);
        var header = KeepHeader(destino, 24);
        return Concat(header, jpeg);
    }

    private static byte[] BuildOzt(Bitmap bmp, string destino)
    {
        var tga = BuildTga(bmp);
        var header = KeepHeader(destino, 4);
        return Concat(header, tga);
    }

    private static byte[] BuildOzb(Bitmap bmp, string destino)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Bmp);
        return Concat(KeepHeader(destino, 4), ms.ToArray());
    }

    /// <summary>TGA de 32 bits sin comprimir, como los que usa el cliente.</summary>
    private static byte[] BuildTga(Bitmap bmp)
    {
        int w = bmp.Width, h = bmp.Height;
        var data = new byte[18 + w * h * 4];

        data[2] = 2;                       // sin comprimir, color verdadero
        data[12] = (byte)(w & 0xFF); data[13] = (byte)(w >> 8);
        data[14] = (byte)(h & 0xFF); data[15] = (byte)(h >> 8);
        data[16] = 32;                     // bits por pixel
        data[17] = 0x08;                   // 8 bits de alpha, filas de abajo hacia arriba

        int p = 18;
        for (int y = h - 1; y >= 0; y--)          // el TGA guarda la ultima fila primero
            for (int x = 0; x < w; x++)
            {
                var c = bmp.GetPixel(x, y);
                data[p++] = c.B;
                data[p++] = c.G;
                data[p++] = c.R;
                data[p++] = c.A;
            }
        return data;
    }

    /// <summary>Conserva el encabezado del archivo que se reemplaza.</summary>
    private static byte[] KeepHeader(string destino, int size)
    {
        var header = new byte[size];
        if (File.Exists(destino))
        {
            var old = File.ReadAllBytes(destino);
            if (old.Length >= size) Array.Copy(old, header, size);
        }
        return header;
    }

    private static byte[] Concat(byte[] a, byte[] b)
    {
        var r = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, r, 0, a.Length);
        Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
        return r;
    }

    public static void Backup(string path)
    {
        if (!File.Exists(path)) return;
        string dir = Path.Combine(Path.GetDirectoryName(path)!, "_backup");
        Directory.CreateDirectory(dir);
        string dst = Path.Combine(dir, Path.GetFileName(path) + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak");
        if (!File.Exists(dst)) File.Copy(path, dst);
    }
}
