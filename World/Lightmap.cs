using System.Drawing.Imaging;
using System.Numerics;
using MuWorldEditor.Formats;

namespace MuWorldEditor.World;

/// <summary>
/// Calculo de la luz del terreno, el boton "Lightmap" del editor original.
/// Saca la normal de cada celda a partir de las alturas vecinas, le aplica una
/// luz direccional y, si se pide, proyecta la sombra de los cerros.
/// </summary>
public static class Lightmap
{
    public sealed class Options
    {
        /// <summary>Direccion desde donde llega la luz (por defecto, como el cliente de MU).</summary>
        public Vector3 LightDirection = Vector3.Normalize(new Vector3(0.5f, -0.5f, 0.8f));
        public Color Ambient = Color.FromArgb(60, 60, 70);
        public Color Diffuse = Color.FromArgb(230, 225, 210);
        public bool Shadows = true;
        public float ShadowStrength = 0.55f;
    }

    public static void Calculate(MuWorld w, Options opt)
    {
        int size = MuWorld.Size;
        var light = Vector3.Normalize(opt.LightDirection);

        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var n = Normal(w, x, y);
                float lambert = Math.Clamp(Vector3.Dot(n, light), 0f, 1f);

                float shadow = 1f;
                if (opt.Shadows) shadow = ShadowFactor(w, x, y, light, opt.ShadowStrength);

                float r = opt.Ambient.R + opt.Diffuse.R * lambert * shadow;
                float g = opt.Ambient.G + opt.Diffuse.G * lambert * shadow;
                float b = opt.Ambient.B + opt.Diffuse.B * lambert * shadow;

                int i = MuWorld.Index(x, y) * 3;
                w.Light[i] = (byte)Math.Clamp(r, 0, 255);
                w.Light[i + 1] = (byte)Math.Clamp(g, 0, 255);
                w.Light[i + 2] = (byte)Math.Clamp(b, 0, 255);
            }
    }

    private static Vector3 Normal(MuWorld w, int x, int y)
    {
        float hl = w.HeightAt(x - 1, y), hr = w.HeightAt(x + 1, y);
        float hd = w.HeightAt(x, y - 1), hu = w.HeightAt(x, y + 1);
        // dos celdas de separacion en X e Y
        var n = new Vector3(hl - hr, hd - hu, 2f * MuMath.TerrainScale);
        return Vector3.Normalize(n);
    }

    /// <summary>Camina hacia la luz: si algo mas alto tapa el paso, hay sombra.</summary>
    private static float ShadowFactor(MuWorld w, int x, int y, Vector3 light, float strength)
    {
        float h = w.HeightAt(x, y);
        float px = x, py = y;
        float stepX = light.X, stepY = light.Y;
        float climb = light.Z * MuMath.TerrainScale;

        for (int step = 1; step <= 48; step++)
        {
            px += stepX; py += stepY;
            if (px < 0 || py < 0 || px >= MuWorld.Size || py >= MuWorld.Size) break;

            float rayHeight = h + climb * step;
            float ground = w.HeightAt((int)px, (int)py);
            if (ground > rayHeight) return 1f - strength;
        }
        return 1f;
    }

    /// <summary>Escribe TerrainLight.OZJ (y el .jpg suelto si existia).</summary>
    public static void Save(MuWorld w)
    {
        using var bmp = new Bitmap(MuWorld.Size, MuWorld.Size, PixelFormat.Format24bppRgb);
        for (int y = 0; y < MuWorld.Size; y++)
            for (int x = 0; x < MuWorld.Size; x++)
            {
                int i = MuWorld.Index(x, y) * 3;
                bmp.SetPixel(x, y, Color.FromArgb(w.Light[i], w.Light[i + 1], w.Light[i + 2]));
            }

        using var ms = new MemoryStream();
        var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var pars = new EncoderParameters(1);
        pars.Param[0] = new EncoderParameter(Encoder.Quality, 95L);
        bmp.Save(ms, enc, pars);
        var jpeg = ms.ToArray();

        string ozj = Path.Combine(w.Directory, "TerrainLight.OZJ");
        byte[] header = new byte[24];
        if (File.Exists(ozj))
        {
            // conservamos el encabezado que ya tenia el archivo
            var old = File.ReadAllBytes(ozj);
            if (old.Length >= 24) Array.Copy(old, header, 24);
            Backup(ozj);
        }

        var outBytes = new byte[24 + jpeg.Length];
        Buffer.BlockCopy(header, 0, outBytes, 0, 24);
        Buffer.BlockCopy(jpeg, 0, outBytes, 24, jpeg.Length);
        File.WriteAllBytes(ozj, outBytes);

        string plain = Path.Combine(w.Directory, "TerrainLight.jpg");
        if (File.Exists(plain)) { Backup(plain); File.WriteAllBytes(plain, jpeg); }
    }

    private static void Backup(string path)
    {
        string dir = Path.Combine(Path.GetDirectoryName(path)!, "_backup");
        Directory.CreateDirectory(dir);
        string dst = Path.Combine(dir, Path.GetFileName(path) + "." + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bak");
        if (!File.Exists(dst)) File.Copy(path, dst);
    }
}
