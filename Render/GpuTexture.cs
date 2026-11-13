using MuWorldEditor.Formats;

namespace MuWorldEditor.Render;

public sealed unsafe class GpuTexture : IDisposable
{
    public uint Id;
    public int Width, Height;
    public bool HasAlpha;

    public static GpuTexture FromMu(MuTexture tex, bool repeat = true)
    {
        var g = new GpuTexture { Width = tex.Width, Height = tex.Height, HasAlpha = tex.HasAlpha };
        uint id;
        Gl.GenTextures(1, &id);
        g.Id = id;
        Gl.BindTexture(Gl.TEXTURE_2D, id);
        Gl.PixelStorei(Gl.UNPACK_ALIGNMENT, 1);

        fixed (byte* p = tex.Pixels)
            Gl.TexImage2D(Gl.TEXTURE_2D, 0, (int)Gl.RGBA, tex.Width, tex.Height, 0, Gl.RGBA, Gl.UNSIGNED_BYTE, p);

        uint wrap = repeat ? Gl.REPEAT : Gl.CLAMP_TO_EDGE;
        Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_WRAP_S, (int)wrap);
        Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_WRAP_T, (int)wrap);
        Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_MAG_FILTER, (int)Gl.LINEAR);

        if (Gl.GenerateMipmap != null)
        {
            Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_MIN_FILTER, (int)Gl.LINEAR_MIPMAP_LINEAR);
            Gl.GenerateMipmap(Gl.TEXTURE_2D);
            Gl.TexParameterf(Gl.TEXTURE_2D, Gl.TEXTURE_MAX_ANISOTROPY, 8f);
        }
        else
        {
            Gl.TexParameteri(Gl.TEXTURE_2D, Gl.TEXTURE_MIN_FILTER, (int)Gl.LINEAR);
        }

        return g;
    }

    public static GpuTexture Solid(byte r, byte g, byte b, byte a)
    {
        var tex = new MuTexture { Width = 1, Height = 1, Pixels = new[] { r, g, b, a }, HasAlpha = a != 255 };
        return FromMu(tex, false);
    }

    public void Bind(uint unit = 0)
    {
        Gl.ActiveTexture(Gl.TEXTURE0 + unit);
        Gl.BindTexture(Gl.TEXTURE_2D, Id);
    }

    public void Dispose()
    {
        if (Id == 0) return;
        uint id = Id;
        Gl.DeleteTextures(1, &id);
        Id = 0;
    }
}
