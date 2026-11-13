using System.Numerics;

namespace MuWorldEditor.Render;

/// <summary>Buffer dinamico para las ayudas visuales: grilla, cajas, cursor del pincel.</summary>
public sealed unsafe class LineBatch : IDisposable
{
    private uint _vbo;
    private int _capacity;
    private readonly List<Vtx> _verts = new();

    public void Clear() => _verts.Clear();
    public int Count => _verts.Count;

    public void AddLine(Vector3 a, Vector3 b, Color c)
    {
        _verts.Add(new Vtx(a.X, a.Y, a.Z, 0, 0, c.R, c.G, c.B, c.A));
        _verts.Add(new Vtx(b.X, b.Y, b.Z, 0, 0, c.R, c.G, c.B, c.A));
    }

    public void AddBox(Vector3 min, Vector3 max, Color c)
    {
        Vector3 P(float x, float y, float z) => new(x, y, z);
        var p = new[]
        {
            P(min.X,min.Y,min.Z), P(max.X,min.Y,min.Z), P(max.X,max.Y,min.Z), P(min.X,max.Y,min.Z),
            P(min.X,min.Y,max.Z), P(max.X,min.Y,max.Z), P(max.X,max.Y,max.Z), P(min.X,max.Y,max.Z)
        };
        for (int i = 0; i < 4; i++)
        {
            AddLine(p[i], p[(i + 1) % 4], c);
            AddLine(p[4 + i], p[4 + (i + 1) % 4], c);
            AddLine(p[i], p[4 + i], c);
        }
    }

    public void AddTransformedBox(Vector3 min, Vector3 max, Matrix4x4 m, Color c)
    {
        var p = new Vector3[8];
        int k = 0;
        for (int zi = 0; zi < 2; zi++)
            for (int yi = 0; yi < 2; yi++)
                for (int xi = 0; xi < 2; xi++)
                    p[k++] = Vector3.Transform(new Vector3(xi == 0 ? min.X : max.X,
                                                           yi == 0 ? min.Y : max.Y,
                                                           zi == 0 ? min.Z : max.Z), m);
        int[,] edges = { {0,1},{1,3},{3,2},{2,0},{4,5},{5,7},{7,6},{6,4},{0,4},{1,5},{2,6},{3,7} };
        for (int i = 0; i < 12; i++) AddLine(p[edges[i, 0]], p[edges[i, 1]], c);
    }

    public void Draw(ShaderProgram sp, float width = 1f)
    {
        if (_verts.Count == 0) return;
        if (_vbo == 0) { uint id; Gl.GenBuffers(1, &id); _vbo = id; }

        Gl.BindBuffer(Gl.ARRAY_BUFFER, _vbo);
        var arr = _verts.ToArray();
        int bytes = arr.Length * Vtx.Stride;
        fixed (Vtx* p = arr)
        {
            if (bytes > _capacity)
            {
                Gl.BufferData(Gl.ARRAY_BUFFER, (IntPtr)bytes, p, Gl.DYNAMIC_DRAW);
                _capacity = bytes;
            }
            else Gl.BufferSubData(Gl.ARRAY_BUFFER, (IntPtr)0, (IntPtr)bytes, p);
        }

        sp.SetModel(Matrix4x4.Identity);
        sp.SetTexture(false);
        sp.SetTint(1, 1, 1, 1);
        sp.SetAlphaCut(0.01f);
        sp.BindAttribs();
        Gl.LineWidth(width);
        Gl.Enable(Gl.BLEND);
        Gl.BlendFunc(Gl.SRC_ALPHA, Gl.ONE_MINUS_SRC_ALPHA);
        Gl.DrawArrays(Gl.LINES, 0, arr.Length);
        Gl.Disable(Gl.BLEND);
    }

    public void Dispose()
    {
        if (_vbo != 0) { uint id = _vbo; Gl.DeleteBuffers(1, &id); _vbo = 0; }
    }
}

/// <summary>Lo mismo pero para triangulos sueltos (marcas de atributo, seleccion).</summary>
public sealed unsafe class QuadBatch : IDisposable
{
    private uint _vbo;
    private int _capacity;
    private readonly List<Vtx> _verts = new();

    public void Clear() => _verts.Clear();
    public int Count => _verts.Count;

    public void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color col)
    {
        void V(Vector3 p) => _verts.Add(new Vtx(p.X, p.Y, p.Z, 0, 0, col.R, col.G, col.B, col.A));
        V(a); V(b); V(c);
        V(a); V(c); V(d);
    }

    public void Draw(ShaderProgram sp)
    {
        if (_verts.Count == 0) return;
        if (_vbo == 0) { uint id; Gl.GenBuffers(1, &id); _vbo = id; }

        Gl.BindBuffer(Gl.ARRAY_BUFFER, _vbo);
        var arr = _verts.ToArray();
        int bytes = arr.Length * Vtx.Stride;
        fixed (Vtx* p = arr)
        {
            if (bytes > _capacity)
            {
                Gl.BufferData(Gl.ARRAY_BUFFER, (IntPtr)bytes, p, Gl.DYNAMIC_DRAW);
                _capacity = bytes;
            }
            else Gl.BufferSubData(Gl.ARRAY_BUFFER, (IntPtr)0, (IntPtr)bytes, p);
        }

        sp.SetModel(Matrix4x4.Identity);
        sp.SetTexture(false);
        sp.SetTint(1, 1, 1, 1);
        sp.SetAlphaCut(0.01f);
        sp.BindAttribs();
        Gl.Enable(Gl.BLEND);
        Gl.BlendFunc(Gl.SRC_ALPHA, Gl.ONE_MINUS_SRC_ALPHA);
        Gl.DepthMask(false);
        Gl.DrawArrays(Gl.TRIANGLES, 0, arr.Length);
        Gl.DepthMask(true);
        Gl.Disable(Gl.BLEND);
    }

    public void Dispose()
    {
        if (_vbo != 0) { uint id = _vbo; Gl.DeleteBuffers(1, &id); _vbo = 0; }
    }
}

/// <summary>Un rectangulo con textura, para dibujar el minimapa sobre la vista.</summary>
public sealed unsafe class TexturedQuad : IDisposable
{
    private uint _vbo;

    public void Draw(ShaderProgram sp, GpuTexture tex, RectangleF r, Color tint)
    {
        if (tex == null) return;
        if (_vbo == 0) { uint id; Gl.GenBuffers(1, &id); _vbo = id; }

        var v = new Vtx[6];
        Vtx P(float x, float y, float u, float w) => new(x, y, 0, u, w, tint.R, tint.G, tint.B, tint.A);
        v[0] = P(r.Left, r.Top, 0, 0); v[1] = P(r.Right, r.Top, 1, 0); v[2] = P(r.Right, r.Bottom, 1, 1);
        v[3] = P(r.Left, r.Top, 0, 0); v[4] = P(r.Right, r.Bottom, 1, 1); v[5] = P(r.Left, r.Bottom, 0, 1);

        Gl.BindBuffer(Gl.ARRAY_BUFFER, _vbo);
        fixed (Vtx* p = v) Gl.BufferData(Gl.ARRAY_BUFFER, (IntPtr)(6 * Vtx.Stride), p, Gl.DYNAMIC_DRAW);

        sp.SetModel(Matrix4x4.Identity);
        tex.Bind();
        sp.SetTexture(true);
        sp.SetTint(1, 1, 1, 1);
        sp.SetAlphaCut(0.01f);
        sp.BindAttribs();
        Gl.Enable(Gl.BLEND);
        Gl.BlendFunc(Gl.SRC_ALPHA, Gl.ONE_MINUS_SRC_ALPHA);
        Gl.DrawArrays(Gl.TRIANGLES, 0, 6);
        Gl.Disable(Gl.BLEND);
    }

    public void Dispose()
    {
        if (_vbo != 0) { uint id = _vbo; Gl.DeleteBuffers(1, &id); _vbo = 0; }
    }
}
