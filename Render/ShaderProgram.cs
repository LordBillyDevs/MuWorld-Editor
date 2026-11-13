using System.Numerics;
using System.Runtime.InteropServices;

namespace MuWorldEditor.Render;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Vtx
{
    public float X, Y, Z;
    public float U, V;
    public byte R, G, B, A;
    public const int Stride = 24;

    public Vtx(float x, float y, float z, float u, float v, byte r, byte g, byte b, byte a)
    { X = x; Y = y; Z = z; U = u; V = v; R = r; G = g; B = b; A = a; }
}

public sealed unsafe class ShaderProgram
{
    public uint Id;
    public int APos, AUV, AColor;
    public int UMvp, UModel, UTex, UUseTexture, UAlphaCut, UTint, UFogColor, UFogStart, UFogEnd, UTime, UWater, UAmbient, UDiffuse, UFogDensity, USky;

    public static ShaderProgram Create()
    {
        var s = new ShaderProgram();
        s.Id = Gl.BuildProgram(Shaders.Vertex, Shaders.Fragment);
        s.APos = Gl.GetAttribLocation(s.Id, "aPos");
        s.AUV = Gl.GetAttribLocation(s.Id, "aUV");
        s.AColor = Gl.GetAttribLocation(s.Id, "aColor");
        s.UMvp = Gl.GetUniformLocation(s.Id, "uMVP");
        s.UModel = Gl.GetUniformLocation(s.Id, "uModel");
        s.UTex = Gl.GetUniformLocation(s.Id, "uTex");
        s.UUseTexture = Gl.GetUniformLocation(s.Id, "uUseTexture");
        s.UAlphaCut = Gl.GetUniformLocation(s.Id, "uAlphaCut");
        s.UTint = Gl.GetUniformLocation(s.Id, "uTint");
        s.UFogColor = Gl.GetUniformLocation(s.Id, "uFogColor");
        s.UFogStart = Gl.GetUniformLocation(s.Id, "uFogStart");
        s.UFogEnd = Gl.GetUniformLocation(s.Id, "uFogEnd");
        s.UTime = Gl.GetUniformLocation(s.Id, "uTime");
        s.UWater = Gl.GetUniformLocation(s.Id, "uWater");
        s.UAmbient = Gl.GetUniformLocation(s.Id, "uAmbient");
        s.UDiffuse = Gl.GetUniformLocation(s.Id, "uDiffuse");
        s.UFogDensity = Gl.GetUniformLocation(s.Id, "uFogDensity");
        s.USky = Gl.GetUniformLocation(s.Id, "uSky");
        return s;
    }

    public void Use() => Gl.UseProgram(Id);

    public void SetMvp(Matrix4x4 m)
    {
        var f = stackalloc float[16];
        Copy(m, f);
        Gl.UniformMatrix4fv(UMvp, 1, false, f);
    }

    public void SetModel(Matrix4x4 m)
    {
        var f = stackalloc float[16];
        Copy(m, f);
        Gl.UniformMatrix4fv(UModel, 1, false, f);
    }

    public void SetTexture(bool use)
    {
        Gl.Uniform1i(UUseTexture, use ? 1 : 0);
        Gl.Uniform1i(UTex, 0);
    }

    public void SetTint(float r, float g, float b, float a) => Gl.Uniform4f(UTint, r, g, b, a);
    public void SetTime(float t) => Gl.Uniform1f(UTime, t);
    public void SetWater(bool on) => Gl.Uniform1i(UWater, on ? 1 : 0);

    public void SetLighting(Color ambient, Color diffuse)
    {
        Gl.Uniform3f(UAmbient, ambient.R / 255f, ambient.G / 255f, ambient.B / 255f);
        Gl.Uniform3f(UDiffuse, diffuse.R / 255f, diffuse.G / 255f, diffuse.B / 255f);
    }
    public void SetSky(Color c) => Gl.Uniform3f(USky, c.R / 255f, c.G / 255f, c.B / 255f);
    public void SetAlphaCut(float v) => Gl.Uniform1f(UAlphaCut, v);

    public void SetFog(bool on, Vector3 color, float start, float end, float density = 1f)
    {
        Gl.Uniform3f(UFogColor, color.X, color.Y, color.Z);
        Gl.Uniform1f(UFogStart, on ? start : 0f);
        Gl.Uniform1f(UFogEnd, on ? end : -1f);
        Gl.Uniform1f(UFogDensity, density);
    }

    /// <summary>Deja los tres atributos apuntando al VBO ya enlazado.</summary>
    public void BindAttribs()
    {
        Gl.EnableVertexAttribArray(APos);
        Gl.VertexAttribPointer(APos, 3, Gl.FLOAT, false, Vtx.Stride, (IntPtr)0);
        Gl.EnableVertexAttribArray(AUV);
        Gl.VertexAttribPointer(AUV, 2, Gl.FLOAT, false, Vtx.Stride, (IntPtr)12);
        Gl.EnableVertexAttribArray(AColor);
        Gl.VertexAttribPointer(AColor, 4, Gl.UNSIGNED_BYTE, true, Vtx.Stride, (IntPtr)20);
    }

    private static void Copy(Matrix4x4 m, float* f)
    {
        f[0] = m.M11; f[1] = m.M12; f[2] = m.M13; f[3] = m.M14;
        f[4] = m.M21; f[5] = m.M22; f[6] = m.M23; f[7] = m.M24;
        f[8] = m.M31; f[9] = m.M32; f[10] = m.M33; f[11] = m.M34;
        f[12] = m.M41; f[13] = m.M42; f[14] = m.M43; f[15] = m.M44;
    }
}
