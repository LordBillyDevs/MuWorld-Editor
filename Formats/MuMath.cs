using System.Numerics;

namespace MuWorldEditor.Formats;

public static class MuMath
{
    public const float TerrainScale = 100f;
    public const float HeightScale = 1.5f;

    /// <summary>
    /// Equivalente al AngleMatrix de MU (herencia Quake/Half-Life): angulos en
    /// radianes X=roll, Y=pitch, Z=yaw. Devuelve la matriz lista para usar con
    /// Vector3.Transform (convencion vector-fila de System.Numerics).
    /// </summary>
    public static Matrix4x4 AngleMatrix(Vector3 angles)
    {
        float sy = MathF.Sin(angles.Z), cy = MathF.Cos(angles.Z);
        float sp = MathF.Sin(angles.Y), cp = MathF.Cos(angles.Y);
        float sr = MathF.Sin(angles.X), cr = MathF.Cos(angles.X);

        var m = Matrix4x4.Identity;
        m.M11 = cp * cy;
        m.M12 = cp * sy;
        m.M13 = -sp;

        m.M21 = sr * sp * cy - cr * sy;
        m.M22 = sr * sp * sy + cr * cy;
        m.M23 = sr * cp;

        m.M31 = cr * sp * cy + sr * sy;
        m.M32 = cr * sp * sy - sr * cy;
        m.M33 = cr * cp;
        return m;
    }

    public static Matrix4x4 AngleMatrixDegrees(Vector3 anglesDeg) =>
        AngleMatrix(new Vector3(
            anglesDeg.X * MathF.PI / 180f,
            anglesDeg.Y * MathF.PI / 180f,
            anglesDeg.Z * MathF.PI / 180f));

    public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);
}
