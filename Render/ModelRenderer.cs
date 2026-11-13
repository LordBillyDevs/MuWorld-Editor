using System.Numerics;
using MuWorldEditor.Formats;

namespace MuWorldEditor.Render;

/// <summary>
/// Sube los .bmd a la placa una sola vez y los dibuja en cada posicion del mapa.
/// Guarda tambien la caja que envuelve al modelo, que es lo que usa el mouse
/// para saber a que objeto le apuntaste.
/// </summary>
public sealed unsafe class ModelRenderer : IDisposable
{
    public sealed class GpuMesh
    {
        public uint Vbo;
        public int VertexCount;
        public GpuTexture Texture;
        public bool Blend;
        /// <summary>Datos sin transformar de cada vertice, para animarlo (solo modelos animados).</summary>
        public SkinVertex[] Src;
        public Vtx[] Work;
    }

    /// <summary>Un vertice tal como viene en el .bmd: hueso, posicion y normal locales.</summary>
    public struct SkinVertex
    {
        public short Node, NormalNode;
        public Vector3 Pos, Normal;
        public bool HasNormal;
        public float U, V;
    }

    public sealed class GpuModel
    {
        public BmdModel Source;
        public readonly List<GpuMesh> Meshes = new();
        public Vector3 Min, Max;
        public bool Failed;
        /// <summary>Tiene animacion propia: pajaros, mariposas, banderas.</summary>
        public bool Animated;
        internal double AnimStamp = double.NaN;
        internal Quaternion[][] RotQ;
        internal Matrix4x4[] Pose;
    }

    /// <summary>Reloj de las animaciones en segundos (lo avanza la vista).</summary>
    public static double AnimTime;
    /// <summary>false = los modelos quedan quietos en su pose inicial.</summary>
    public static bool PlayAnimations = true;
    /// <summary>Cuadros clave por segundo a velocidad normal (MU corre a 25 fps con ~0.4 cuadros por tick).</summary>
    public const double KeysPerSecond = 10.0;

    private readonly Dictionary<string, GpuModel> _cache = new(StringComparer.OrdinalIgnoreCase);
    private GpuTexture _white;

    public GpuTexture White => _white ??= GpuTexture.Solid(255, 255, 255, 255);

    public GpuModel Get(string path, IEnumerable<string> textureDirs)
    {
        if (path == null) return null;
        if (_cache.TryGetValue(path, out var m)) return m.Failed ? null : m;

        var gm = new GpuModel();
        _cache[path] = gm;
        try
        {
            var bmd = BmdModel.Load(path);
            gm.Source = bmd;
            gm.Animated = bmd.Actions.Length > 0 && bmd.Actions[0].NumKeys > 1;
            gm.Min = bmd.BoundsMin;
            gm.Max = bmd.BoundsMax;
            var dirs = textureDirs?.ToArray() ?? Array.Empty<string>();

            foreach (var mesh in bmd.Meshes)
            {
                var verts = new List<Vtx>(mesh.Triangles.Length * 3);
                var src = gm.Animated ? new List<SkinVertex>(mesh.Triangles.Length * 3) : null;
                foreach (var t in mesh.Triangles)
                {
                    AddVertex(verts, src, bmd, mesh, t.V0, t.N0, t.T0);
                    AddVertex(verts, src, bmd, mesh, t.V1, t.N1, t.T1);
                    AddVertex(verts, src, bmd, mesh, t.V2, t.N2, t.T2);
                }
                if (verts.Count == 0) continue;

                var gpu = new GpuMesh { VertexCount = verts.Count };
                if (src != null) { gpu.Src = src.ToArray(); gpu.Work = new Vtx[gpu.Src.Length]; }
                uint vbo;
                Gl.GenBuffers(1, &vbo);
                gpu.Vbo = vbo;
                Gl.BindBuffer(Gl.ARRAY_BUFFER, vbo);
                var arr = verts.ToArray();
                fixed (Vtx* p = arr)
                    Gl.BufferData(Gl.ARRAY_BUFFER, (IntPtr)(arr.Length * Vtx.Stride), p, gm.Animated ? Gl.DYNAMIC_DRAW : Gl.STATIC_DRAW);

                string texPath = MuTexture.Resolve(bmd.Directory, mesh.TexturePath, dirs);
                if (texPath != null)
                {
                    try
                    {
                        var tex = MuTexture.Load(texPath);
                        gpu.Texture = GpuTexture.FromMu(tex);
                        gpu.Blend = tex.HasAlpha;
                    }
                    catch { gpu.Texture = null; }
                }
                gm.Meshes.Add(gpu);
            }
        }
        catch
        {
            gm.Failed = true;
            return null;
        }
        return gm;
    }

    private static readonly Vector3 SunDir = Vector3.Normalize(new Vector3(-0.45f, -0.35f, 0.82f));

    /// <summary>
    /// Luz suave: un sol de costado mas luz de cielo que llega de arriba, asi
    /// se distingue el volumen sin dejar caras negras.
    /// </summary>
    private static byte Light(Vector3 n)
    {
        float len = n.Length();
        if (len < 1e-6f) return 255;
        n /= len;
        float sun = Math.Clamp(Vector3.Dot(n, SunDir), 0f, 1f);
        float sky = n.Z * 0.5f + 0.5f;
        return (byte)(Math.Clamp(0.42f + 0.43f * sun + 0.25f * sky, 0f, 1.0f) * 255);
    }

    private static void AddVertex(List<Vtx> list, List<SkinVertex> src, BmdModel bmd, BmdModel.Mesh mesh, short vi, short ni, short ti)
    {
        if (vi < 0 || vi >= mesh.Vertices.Length) return;
        var pos = bmd.TransformVertex(mesh.Vertices[vi]);
        bool hasN = ni >= 0 && ni < mesh.Normals.Length;
        byte c = hasN ? Light(bmd.TransformNormal(mesh.Normals[ni])) : (byte)255;

        var uv = ti >= 0 && ti < mesh.TexCoords.Length ? mesh.TexCoords[ti] : Vector2.Zero;
        list.Add(new Vtx(pos.X, pos.Y, pos.Z, uv.X, uv.Y, c, c, c, 255));

        src?.Add(new SkinVertex
        {
            Node = mesh.Vertices[vi].Node,
            Pos = mesh.Vertices[vi].Position,
            HasNormal = hasN,
            NormalNode = hasN ? mesh.Normals[ni].Node : (short)-1,
            Normal = hasN ? mesh.Normals[ni].Normal : Vector3.UnitZ,
            U = uv.X, V = uv.Y
        });
    }

    /// <summary>
    /// Pone el modelo en la pose que toca segun el reloj: interpola los huesos
    /// entre dos cuadros clave de la primera accion y vuelve a subir los
    /// vertices. Se hace una vez por modelo (todas sus copias en el mapa se
    /// mueven juntas) y como mucho 30 veces por segundo.
    /// </summary>
    public void Animate(GpuModel m)
    {
        if (m == null || !m.Animated || m.Source == null) return;
        var bmd = m.Source;
        var act = bmd.Actions[0];
        int keys = act.NumKeys;
        if (keys < 2) return;

        double stamp = PlayAnimations ? Math.Floor(AnimTime * 30.0) / 30.0 : -1.0;
        if (stamp == m.AnimStamp) return;
        m.AnimStamp = stamp;

        float frame = stamp < 0 ? 0f : (float)((stamp * KeysPerSecond) % keys);
        int a = Math.Clamp((int)frame, 0, keys - 1);
        int b = (a + 1) % keys;
        float t = frame - a;

        // rotaciones como cuaterniones, calculadas una sola vez
        if (m.RotQ == null)
        {
            m.RotQ = new Quaternion[bmd.Bones.Length][];
            for (int i = 0; i < bmd.Bones.Length; i++)
            {
                var bone = bmd.Bones[i];
                if (bone.Dummy || bone.Rotation.Length == 0) { m.RotQ[i] = Array.Empty<Quaternion>(); continue; }
                var rot = bone.Rotation[0];
                var q = new Quaternion[rot.Length];
                for (int k = 0; k < rot.Length; k++)
                    q[k] = Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(MuMath.AngleMatrix(rot[k])));
                m.RotQ[i] = q;
            }
            m.Pose = new Matrix4x4[bmd.Bones.Length];
        }

        var pose = m.Pose;
        for (int i = 0; i < bmd.Bones.Length; i++)
        {
            var bone = bmd.Bones[i];
            var q = m.RotQ[i];
            if (bone.Dummy || q.Length < keys || bone.Position.Length == 0 || bone.Position[0].Length < keys)
            {
                pose[i] = i < bmd.BindPose.Length ? bmd.BindPose[i] : Matrix4x4.Identity;
                continue;
            }
            var pa = bone.Position[0][a];
            var pb = bone.Position[0][b];
            var local = Matrix4x4.CreateFromQuaternion(Quaternion.Slerp(q[a], q[b], t));
            var p = Vector3.Lerp(pa, pb, t);
            local.M41 = p.X; local.M42 = p.Y; local.M43 = p.Z;
            pose[i] = bone.Parent >= 0 && bone.Parent < i ? local * pose[bone.Parent] : local;
        }

        foreach (var mesh in m.Meshes)
        {
            var src = mesh.Src;
            var work = mesh.Work;
            if (src == null || work == null) continue;
            for (int i = 0; i < src.Length; i++)
            {
                ref readonly var sv = ref src[i];
                var pos = sv.Node >= 0 && sv.Node < pose.Length ? Vector3.Transform(sv.Pos, pose[sv.Node]) : sv.Pos;
                byte c = 255;
                if (sv.HasNormal)
                    c = Light(sv.NormalNode >= 0 && sv.NormalNode < pose.Length ? Vector3.TransformNormal(sv.Normal, pose[sv.NormalNode]) : sv.Normal);
                work[i] = new Vtx(pos.X, pos.Y, pos.Z, sv.U, sv.V, c, c, c, 255);
            }
            Gl.BindBuffer(Gl.ARRAY_BUFFER, mesh.Vbo);
            fixed (Vtx* p = work)
                Gl.BufferSubData(Gl.ARRAY_BUFFER, IntPtr.Zero, (IntPtr)(work.Length * Vtx.Stride), p);
        }
    }

    /// <summary>
    /// Marca el objeto seleccionado dibujando su malla en lineas por encima del
    /// modelo, para no taparle la textura.
    /// </summary>
    public void DrawWire(ShaderProgram sp, GpuModel model, Matrix4x4 transform, Color color)
    {
        if (model == null) return;

        sp.SetModel(transform);
        sp.SetTexture(false);
        sp.SetTint(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        sp.SetAlphaCut(0.01f);

        Gl.PolygonMode(Gl.FRONT_AND_BACK, Gl.LINE);
        Gl.LineWidth(1.4f);
        Gl.Enable(Gl.BLEND);
        Gl.BlendFunc(Gl.SRC_ALPHA, Gl.ONE_MINUS_SRC_ALPHA);
        Gl.DepthMask(false);

        foreach (var mesh in model.Meshes)
        {
            Gl.BindBuffer(Gl.ARRAY_BUFFER, mesh.Vbo);
            sp.BindAttribs();
            Gl.DrawArrays(Gl.TRIANGLES, 0, mesh.VertexCount);
        }

        Gl.DepthMask(true);
        Gl.Disable(Gl.BLEND);
        Gl.PolygonMode(Gl.FRONT_AND_BACK, Gl.FILL);
        sp.SetTint(1, 1, 1, 1);
    }

    /// <summary>
    /// Contorno del modelo (seleccion o mouse encima): primero se marca su
    /// silueta en el stencil sin pintar, despues se dibuja la malla con lineas
    /// gruesas solo por fuera de esa silueta. Se ve aunque este detras de algo.
    /// </summary>
    public void DrawOutline(ShaderProgram sp, GpuModel model, Matrix4x4 transform, Color color, float width)
    {
        if (model == null) return;

        Gl.Clear(Gl.STENCIL_BUFFER_BIT);
        Gl.Enable(Gl.STENCIL_TEST);
        Gl.Disable(Gl.DEPTH_TEST);

        // 1) silueta en el stencil
        Gl.ColorMask(false, false, false, false);
        Gl.StencilMask(0xFF);
        Gl.StencilFunc(Gl.ALWAYS, 1, 0xFF);
        Gl.StencilOp(Gl.KEEP, Gl.KEEP, Gl.REPLACE);
        Draw(sp, model, transform, Vector4.One);

        // 2) lineas gruesas fuera de la silueta
        Gl.ColorMask(true, true, true, true);
        Gl.StencilMask(0x00);
        Gl.StencilFunc(Gl.NOTEQUAL, 1, 0xFF);
        sp.SetModel(transform);
        sp.SetTexture(false);
        sp.SetTint(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);
        sp.SetAlphaCut(0.01f);
        Gl.PolygonMode(Gl.FRONT_AND_BACK, Gl.LINE);
        Gl.LineWidth(width);
        Gl.Enable(Gl.BLEND);
        Gl.BlendFunc(Gl.SRC_ALPHA, Gl.ONE_MINUS_SRC_ALPHA);
        foreach (var mesh in model.Meshes)
        {
            Gl.BindBuffer(Gl.ARRAY_BUFFER, mesh.Vbo);
            sp.BindAttribs();
            Gl.DrawArrays(Gl.TRIANGLES, 0, mesh.VertexCount);
        }
        Gl.Disable(Gl.BLEND);
        Gl.PolygonMode(Gl.FRONT_AND_BACK, Gl.FILL);
        Gl.LineWidth(1f);

        Gl.StencilMask(0xFF);
        Gl.Disable(Gl.STENCIL_TEST);
        Gl.Enable(Gl.DEPTH_TEST);
        sp.SetTint(1, 1, 1, 1);
    }

    public void Draw(ShaderProgram sp, GpuModel model, Matrix4x4 transform, Vector4 tint)
    {
        if (model == null) return;
        if (model.Animated) Animate(model);
        sp.SetModel(transform);
        sp.SetTint(tint.X, tint.Y, tint.Z, tint.W);

        foreach (var mesh in model.Meshes)
        {
            (mesh.Texture ?? White).Bind();
            sp.SetTexture(true);
            if (mesh.Blend)
            {
                Gl.Enable(Gl.BLEND);
                Gl.BlendFunc(Gl.SRC_ALPHA, Gl.ONE_MINUS_SRC_ALPHA);
                sp.SetAlphaCut(0.35f);
            }
            else sp.SetAlphaCut(0.02f);

            Gl.BindBuffer(Gl.ARRAY_BUFFER, mesh.Vbo);
            sp.BindAttribs();
            Gl.DrawArrays(Gl.TRIANGLES, 0, mesh.VertexCount);

            if (mesh.Blend) Gl.Disable(Gl.BLEND);
        }
    }

    /// <summary>
    /// Olvida un modelo del cache para que se vuelva a leer del disco. Se usa
    /// despues de cambiarle la textura o el archivo.
    /// </summary>
    public void Forget(string path)
    {
        if (path == null) return;
        foreach (var clave in _cache.Keys.Where(k => string.Equals(k, path, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var m = _cache[clave];
            foreach (var mesh in m.Meshes)
            {
                if (mesh.Vbo != 0) { uint id = mesh.Vbo; Gl.DeleteBuffers(1, &id); }
                mesh.Texture?.Dispose();
            }
            _cache.Remove(clave);
        }
    }

    /// <summary>Vacia todo el cache: al cambiar de mapa o tras tocar archivos.</summary>
    public void ForgetAll()
    {
        foreach (var m in _cache.Values)
            foreach (var mesh in m.Meshes)
            {
                if (mesh.Vbo != 0) { uint id = mesh.Vbo; Gl.DeleteBuffers(1, &id); }
                mesh.Texture?.Dispose();
            }
        _cache.Clear();
    }

    public void Dispose()
    {
        foreach (var m in _cache.Values)
            foreach (var mesh in m.Meshes)
            {
                if (mesh.Vbo != 0) { uint id = mesh.Vbo; Gl.DeleteBuffers(1, &id); }
                mesh.Texture?.Dispose();
            }
        _cache.Clear();
        _white?.Dispose();
    }
}
