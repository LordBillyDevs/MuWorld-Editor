using System.Numerics;
using System.Text;

namespace MuWorldEditor.Formats;

/// <summary>
/// Modelo .bmd de MU Online. Estructura verificada contra los 122 modelos de
/// Client/Data/Object1 (parseo exacto, sin bytes sobrantes).
/// </summary>
public sealed class BmdModel
{
    public sealed class Mesh
    {
        public short Texture;
        public string TexturePath = "";
        public BmdVertex[] Vertices = Array.Empty<BmdVertex>();
        public BmdNormal[] Normals = Array.Empty<BmdNormal>();
        public Vector2[] TexCoords = Array.Empty<Vector2>();
        public BmdTriangle[] Triangles = Array.Empty<BmdTriangle>();
    }

    public struct BmdVertex { public short Node; public Vector3 Position; }
    public struct BmdNormal { public short Node; public Vector3 Normal; public short BindVertex; }

    public struct BmdTriangle
    {
        public byte Polygon;
        public short V0, V1, V2;
        public short N0, N1, N2;
        public short T0, T1, T2;
    }

    public sealed class Bone
    {
        public bool Dummy;
        public string Name = "";
        public short Parent;
        public Vector3[][] Position = Array.Empty<Vector3[]>();   // [accion][key]
        public Vector3[][] Rotation = Array.Empty<Vector3[]>();
    }

    public sealed class ActionInfo
    {
        public short NumKeys;
        public bool LockPositions;
        public Vector3[] Positions = Array.Empty<Vector3>();
    }

    public string Name = "";
    public Mesh[] Meshes = Array.Empty<Mesh>();
    public Bone[] Bones = Array.Empty<Bone>();
    public ActionInfo[] Actions = Array.Empty<ActionInfo>();
    public string Directory = "";

    /// <summary>Matriz de cada hueso en la pose de reposo (accion 0, frame 0).</summary>
    public Matrix4x4[] BindPose = Array.Empty<Matrix4x4>();
    public Vector3 BoundsMin, BoundsMax;

    public static BmdModel Load(string path)
    {
        var raw = File.ReadAllBytes(path);
        if (raw.Length < 8 || raw[0] != (byte)'B' || raw[1] != (byte)'M' || raw[2] != (byte)'D')
            throw new InvalidDataException("No es un .bmd: " + path);

        byte[] body;
        if (raw[3] == 15)
        {
            // seasons nuevas: LEA-256 sobre los bloques enteros de 16 bytes
            int encSize = BitConverter.ToInt32(raw, 4);
            encSize = Math.Min(encSize, raw.Length - 8);
            body = new byte[encSize];
            Buffer.BlockCopy(raw, 8, body, 0, encSize);
            LeaCrypto.DecryptEcb(body, 0, encSize);
        }
        else if (raw[3] == 12)
        {
            int encSize = BitConverter.ToInt32(raw, 4);
            encSize = Math.Min(encSize, raw.Length - 8);
            var slice = new byte[encSize];
            Buffer.BlockCopy(raw, 8, slice, 0, encSize);
            body = MuCrypto.MapFileDecrypt(slice);
        }
        else
        {
            body = new byte[raw.Length - 4];
            Buffer.BlockCopy(raw, 4, body, 0, body.Length);
        }

        var m = new BmdModel { Directory = Path.GetDirectoryName(path) ?? "" };
        using var ms = new MemoryStream(body);
        using var r = new BinaryReader(ms, Encoding.ASCII);

        m.Name = ReadFixedString(r, 32);
        short numMeshes = r.ReadInt16();
        short numBones = r.ReadInt16();
        short numActions = r.ReadInt16();

        m.Meshes = new Mesh[numMeshes];
        for (int i = 0; i < numMeshes; i++)
        {
            var mesh = new Mesh();
            short nv = r.ReadInt16(), nn = r.ReadInt16(), nt = r.ReadInt16(), ntri = r.ReadInt16();
            mesh.Texture = r.ReadInt16();

            mesh.Vertices = new BmdVertex[nv];
            for (int v = 0; v < nv; v++)
            {
                short node = r.ReadInt16(); r.ReadInt16();
                mesh.Vertices[v] = new BmdVertex { Node = node, Position = ReadVec3(r) };
            }

            mesh.Normals = new BmdNormal[nn];
            for (int n = 0; n < nn; n++)
            {
                short node = r.ReadInt16(); r.ReadInt16();
                var nor = ReadVec3(r);
                short bind = r.ReadInt16(); r.ReadInt16();
                mesh.Normals[n] = new BmdNormal { Node = node, Normal = nor, BindVertex = bind };
            }

            mesh.TexCoords = new Vector2[nt];
            for (int t = 0; t < nt; t++)
                mesh.TexCoords[t] = new Vector2(r.ReadSingle(), r.ReadSingle());

            mesh.Triangles = new BmdTriangle[ntri];
            for (int t = 0; t < ntri; t++)
            {
                long start = ms.Position;
                var tri = new BmdTriangle { Polygon = r.ReadByte() };
                ms.Position = start + 2;
                tri.V0 = r.ReadInt16(); tri.V1 = r.ReadInt16(); tri.V2 = r.ReadInt16(); r.ReadInt16();
                tri.N0 = r.ReadInt16(); tri.N1 = r.ReadInt16(); tri.N2 = r.ReadInt16(); r.ReadInt16();
                tri.T0 = r.ReadInt16(); tri.T1 = r.ReadInt16(); tri.T2 = r.ReadInt16(); r.ReadInt16();
                ms.Position = start + 64;
                mesh.Triangles[t] = tri;
            }

            mesh.TexturePath = ReadFixedString(r, 32);
            m.Meshes[i] = mesh;
        }

        m.Actions = new ActionInfo[numActions];
        for (int a = 0; a < numActions; a++)
        {
            var act = new ActionInfo { NumKeys = r.ReadInt16(), LockPositions = r.ReadByte() != 0 };
            if (act.LockPositions)
            {
                act.Positions = new Vector3[act.NumKeys];
                for (int k = 0; k < act.NumKeys; k++) act.Positions[k] = ReadVec3(r);
            }
            m.Actions[a] = act;
        }

        m.Bones = new Bone[numBones];
        for (int b = 0; b < numBones; b++)
        {
            var bone = new Bone { Dummy = r.ReadByte() != 0 };
            if (!bone.Dummy)
            {
                bone.Name = ReadFixedString(r, 32);
                bone.Parent = r.ReadInt16();
                bone.Position = new Vector3[numActions][];
                bone.Rotation = new Vector3[numActions][];
                for (int a = 0; a < numActions; a++)
                {
                    int keys = m.Actions[a].NumKeys;
                    var pos = new Vector3[keys];
                    for (int k = 0; k < keys; k++) pos[k] = ReadVec3(r);
                    var rot = new Vector3[keys];
                    for (int k = 0; k < keys; k++) rot[k] = ReadVec3(r);
                    bone.Position[a] = pos;
                    bone.Rotation[a] = rot;
                }
            }
            m.Bones[b] = bone;
        }

        m.BuildBindPose();
        return m;
    }

    public void BuildBindPose()
    {
        BindPose = new Matrix4x4[Bones.Length];
        for (int i = 0; i < Bones.Length; i++)
        {
            var bone = Bones[i];
            if (bone.Dummy || bone.Position.Length == 0 || bone.Position[0].Length == 0)
            {
                BindPose[i] = Matrix4x4.Identity;
                continue;
            }

            var local = MuMath.AngleMatrix(bone.Rotation[0][0]);
            local.M41 = bone.Position[0][0].X;
            local.M42 = bone.Position[0][0].Y;
            local.M43 = bone.Position[0][0].Z;

            BindPose[i] = bone.Parent >= 0 && bone.Parent < i
                ? local * BindPose[bone.Parent]
                : local;
        }

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var mesh in Meshes)
            foreach (var v in mesh.Vertices)
            {
                var p = TransformVertex(v);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        if (min.X > max.X) { min = Vector3.Zero; max = Vector3.Zero; }
        BoundsMin = min; BoundsMax = max;
    }

    public Vector3 TransformVertex(in BmdVertex v)
    {
        int n = v.Node;
        if (n < 0 || n >= BindPose.Length) return v.Position;
        return Vector3.Transform(v.Position, BindPose[n]);
    }

    public Vector3 TransformNormal(in BmdNormal nrm)
    {
        int n = nrm.Node;
        if (n < 0 || n >= BindPose.Length) return nrm.Normal;
        return Vector3.TransformNormal(nrm.Normal, BindPose[n]);
    }

    private static Vector3 ReadVec3(BinaryReader r) => new(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    private static string ReadFixedString(BinaryReader r, int len)
    {
        var b = r.ReadBytes(len);
        int end = Array.IndexOf(b, (byte)0);
        if (end < 0) end = b.Length;
        return Encoding.ASCII.GetString(b, 0, end).Trim();
    }
}
