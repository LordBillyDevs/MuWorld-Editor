using System.Numerics;

namespace MuWorldEditor.Render;

/// <summary>Los seis planos de la camara, para no dibujar lo que no se ve.</summary>
public struct Frustum
{
    private Vector4 _l, _r, _b, _t, _n, _f;

    public static Frustum FromViewProjection(Matrix4x4 m)
    {
        var fr = new Frustum();
        fr._l = Normalize(new Vector4(m.M14 + m.M11, m.M24 + m.M21, m.M34 + m.M31, m.M44 + m.M41));
        fr._r = Normalize(new Vector4(m.M14 - m.M11, m.M24 - m.M21, m.M34 - m.M31, m.M44 - m.M41));
        fr._b = Normalize(new Vector4(m.M14 + m.M12, m.M24 + m.M22, m.M34 + m.M32, m.M44 + m.M42));
        fr._t = Normalize(new Vector4(m.M14 - m.M12, m.M24 - m.M22, m.M34 - m.M32, m.M44 - m.M42));
        fr._n = Normalize(new Vector4(m.M13, m.M23, m.M33, m.M43));
        fr._f = Normalize(new Vector4(m.M14 - m.M13, m.M24 - m.M23, m.M34 - m.M33, m.M44 - m.M43));
        return fr;
    }

    private static Vector4 Normalize(Vector4 p)
    {
        float len = new Vector3(p.X, p.Y, p.Z).Length();
        return len > 0 ? p / len : p;
    }

    public bool Intersects(Vector3 min, Vector3 max)
    {
        return Test(_l, min, max) && Test(_r, min, max) && Test(_b, min, max)
            && Test(_t, min, max) && Test(_n, min, max) && Test(_f, min, max);
    }

    private static bool Test(Vector4 p, Vector3 min, Vector3 max)
    {
        // el vertice mas favorable de la caja respecto del plano
        var v = new Vector3(p.X >= 0 ? max.X : min.X,
                            p.Y >= 0 ? max.Y : min.Y,
                            p.Z >= 0 ? max.Z : min.Z);
        return p.X * v.X + p.Y * v.Y + p.Z * v.Z + p.W >= 0;
    }
}
