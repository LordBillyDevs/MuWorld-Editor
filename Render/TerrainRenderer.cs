using System.Numerics;
using MuWorldEditor.Formats;
using MuWorldEditor.World;

namespace MuWorldEditor.Render;

/// <summary>
/// Dibuja el terreno en pedazos de 32x32 celdas. Al pintar solo se reconstruye
/// el pedazo tocado, asi el pincel responde al instante aunque el mapa entero
/// tenga 65.536 celdas.
/// </summary>
public sealed unsafe class TerrainRenderer : IDisposable
{
    public const int ChunkCells = 32;
    public const int ChunksPerSide = MuWorld.Size / ChunkCells;

    private sealed class Batch { public int Tile; public int First; public int Count; }

    private sealed class Chunk
    {
        public int Cx, Cy;
        public uint Vbo;
        public int Capacity;
        public bool Dirty = true;
        public readonly List<Batch> Layer1 = new();
        public readonly List<Batch> Layer2 = new();
        public float MinZ, MaxZ;
    }

    private readonly Chunk[] _chunks = new Chunk[ChunksPerSide * ChunksPerSide];
    private readonly WorldRef _ref;

    private MuWorld World => _ref.World;

    public TerrainRenderer(WorldRef worldRef)
    {
        _ref = worldRef;
        for (int cy = 0; cy < ChunksPerSide; cy++)
            for (int cx = 0; cx < ChunksPerSide; cx++)
                _chunks[cy * ChunksPerSide + cx] = new Chunk { Cx = cx * ChunkCells, Cy = cy * ChunkCells };
    }

    /// <summary>
    /// Como se sombrea el terreno: 0 = luz real del mapa (TerrainLight),
    /// 1 = relieve calculado con las alturas, 2 = las dos combinadas, 3 = plano.
    /// </summary>
    public int Shading
    {
        get => _shading;
        set { if (_shading == value) return; _shading = value; InvalidateAll(); }
    }
    private static int _shading;

    public void InvalidateAll()
    {
        foreach (var c in _chunks) c.Dirty = true;
    }

    /// <summary>Marca sucios los pedazos que tocan el rectangulo de celdas dado.</summary>
    public void InvalidateCells(int x0, int y0, int x1, int y1)
    {
        int cx0 = Math.Max(0, (x0 - 1) / ChunkCells), cx1 = Math.Min(ChunksPerSide - 1, (x1 + 1) / ChunkCells);
        int cy0 = Math.Max(0, (y0 - 1) / ChunkCells), cy1 = Math.Min(ChunksPerSide - 1, (y1 + 1) / ChunkCells);
        for (int cy = cy0; cy <= cy1; cy++)
            for (int cx = cx0; cx <= cx1; cx++)
                _chunks[cy * ChunksPerSide + cx].Dirty = true;
    }

    private void Build(Chunk c, TileCatalog tiles)
    {
        var w = World;
        var byTile1 = new Dictionary<int, List<Vtx>>();
        var byTile2 = new Dictionary<int, List<Vtx>>();
        c.MinZ = float.MaxValue; c.MaxZ = float.MinValue;

        for (int y = c.Cy; y < c.Cy + ChunkCells; y++)
            for (int x = c.Cx; x < c.Cx + ChunkCells; x++)
            {
                int t1 = w.Layer1[MuWorld.Index(x, y)];
                int t2 = w.Layer2[MuWorld.Index(x, y)];
                byte alpha = w.Alpha[MuWorld.Index(x, y)];

                if (t1 != TileCatalog.None)
                    Emit(byTile1, t1, x, y, 255, tiles.RepeatOf(t1), w, ref c.MinZ, ref c.MaxZ);
                if (t2 != TileCatalog.None && alpha > 0)
                    Emit(byTile2, t2, x, y, alpha, tiles.RepeatOf(t2), w, ref c.MinZ, ref c.MaxZ);
            }

        var all = new List<Vtx>();
        c.Layer1.Clear(); c.Layer2.Clear();

        foreach (var kv in byTile1.OrderBy(k => k.Key))
        {
            c.Layer1.Add(new Batch { Tile = kv.Key, First = all.Count, Count = kv.Value.Count });
            all.AddRange(kv.Value);
        }
        foreach (var kv in byTile2.OrderBy(k => k.Key))
        {
            c.Layer2.Add(new Batch { Tile = kv.Key, First = all.Count, Count = kv.Value.Count });
            all.AddRange(kv.Value);
        }

        if (c.Vbo == 0)
        {
            uint id;
            Gl.GenBuffers(1, &id);
            c.Vbo = id;
        }

        Gl.BindBuffer(Gl.ARRAY_BUFFER, c.Vbo);
        int bytes = all.Count * Vtx.Stride;
        var arr = all.ToArray();
        fixed (Vtx* p = arr)
        {
            if (bytes > c.Capacity)
            {
                Gl.BufferData(Gl.ARRAY_BUFFER, (IntPtr)bytes, p, Gl.DYNAMIC_DRAW);
                c.Capacity = bytes;
            }
            else if (bytes > 0)
            {
                Gl.BufferSubData(Gl.ARRAY_BUFFER, (IntPtr)0, (IntPtr)bytes, p);
            }
        }

        if (c.MinZ > c.MaxZ) { c.MinZ = 0; c.MaxZ = 0; }
        c.Dirty = false;
    }

    private static void Emit(Dictionary<int, List<Vtx>> map, int tile, int x, int y, byte alpha,
                             float repeat, MuWorld w, ref float minZ, ref float maxZ)
    {
        if (!map.TryGetValue(tile, out var list)) map[tile] = list = new List<Vtx>(256);

        float s = MuMath.TerrainScale;
        float u0 = x / repeat, u1 = (x + 1) / repeat;
        float v0 = y / repeat, v1 = (y + 1) / repeat;

        Vtx V(int px, int py, float u, float v)
        {
            int cx = Math.Min(px, MuWorld.Size - 1);
            int cy = Math.Min(py, MuWorld.Size - 1);
            int i = MuWorld.Index(cx, cy);
            float z = w.Height[i];
            byte r = w.Light[i * 3], g = w.Light[i * 3 + 1], b = w.Light[i * 3 + 2];
            if (_shading != 0)
            {
                float k = _shading == 3 ? 1f : Relief(w, cx, cy);
                if (_shading == 2)
                {
                    r = (byte)Math.Clamp(r * k * 1.1f, 0, 255);
                    g = (byte)Math.Clamp(g * k * 1.1f, 0, 255);
                    b = (byte)Math.Clamp(b * k * 1.1f, 0, 255);
                }
                else
                {
                    r = g = b = (byte)Math.Clamp(k * 235f, 0, 255);
                }
            }
            return new Vtx(px * s, py * s, z, u, v, r, g, b, alpha);
        }

        var a = V(x, y, u0, v0);
        var b = V(x + 1, y, u1, v0);
        var c2 = V(x + 1, y + 1, u1, v1);
        var d = V(x, y + 1, u0, v1);

        minZ = MathF.Min(minZ, MathF.Min(MathF.Min(a.Z, b.Z), MathF.Min(c2.Z, d.Z)));
        maxZ = MathF.Max(maxZ, MathF.Max(MathF.Max(a.Z, b.Z), MathF.Max(c2.Z, d.Z)));

        list.Add(a); list.Add(b); list.Add(c2);
        list.Add(a); list.Add(c2); list.Add(d);
    }

    private static readonly Vector3 Sun = Vector3.Normalize(new Vector3(-0.45f, -0.35f, 0.82f));

    /// <summary>Luz de la celda segun la pendiente: sol de costado mas luz de cielo.</summary>
    private static float Relief(MuWorld w, int x, int y)
    {
        float hl = w.HeightAt(x - 1, y), hr = w.HeightAt(x + 1, y);
        float hd = w.HeightAt(x, y - 1), hu = w.HeightAt(x, y + 1);
        var n = Vector3.Normalize(new Vector3(hl - hr, hd - hu, 2f * MuMath.TerrainScale));
        float sun = Math.Max(0f, Vector3.Dot(n, Sun));
        float sky = n.Z * 0.5f + 0.5f;
        return 0.30f + 0.62f * sun + 0.22f * sky;
    }

    public int DrawCalls { get; private set; }

    public void Render(ShaderProgram sp, TileCatalog tiles, bool layer1, bool layer2, Frustum frustum, bool animateWater = true)
    {
        DrawCalls = 0;
        sp.SetModel(Matrix4x4.Identity);
        sp.SetTexture(true);
        sp.SetTint(1, 1, 1, 1);
        sp.SetAlphaCut(0.02f);

        foreach (var c in _chunks)
        {
            if (c.Dirty) Build(c, tiles);

            var min = new Vector3(c.Cx * MuMath.TerrainScale, c.Cy * MuMath.TerrainScale, c.MinZ);
            var max = new Vector3((c.Cx + ChunkCells) * MuMath.TerrainScale, (c.Cy + ChunkCells) * MuMath.TerrainScale, c.MaxZ + 1);
            if (!frustum.Intersects(min, max)) continue;

            Gl.BindBuffer(Gl.ARRAY_BUFFER, c.Vbo);
            sp.BindAttribs();

            if (layer1)
                foreach (var b in c.Layer1)
                {
                    sp.SetWater(animateWater && tiles.IsWater(b.Tile));
                    tiles.TextureOf(b.Tile).Bind();
                    Gl.DrawArrays(Gl.TRIANGLES, b.First, b.Count);
                    DrawCalls++;
                }

            if (layer2 && c.Layer2.Count > 0)
            {
                Gl.Enable(Gl.BLEND);
                Gl.BlendFunc(Gl.SRC_ALPHA, Gl.ONE_MINUS_SRC_ALPHA);
                Gl.DepthMask(false);
                foreach (var b in c.Layer2)
                {
                    sp.SetWater(animateWater && tiles.IsWater(b.Tile));
                    tiles.TextureOf(b.Tile).Bind();
                    Gl.DrawArrays(Gl.TRIANGLES, b.First, b.Count);
                    DrawCalls++;
                }
                Gl.DepthMask(true);
                Gl.Disable(Gl.BLEND);
            }
        }
        sp.SetWater(false);
    }

    public void Dispose()
    {
        foreach (var c in _chunks)
            if (c.Vbo != 0) { uint id = c.Vbo; Gl.DeleteBuffers(1, &id); c.Vbo = 0; }
    }
}
