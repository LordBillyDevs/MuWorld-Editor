using System.Numerics;

namespace MuWorldEditor.Render;

/// <summary>
/// Camara orbital sobre un punto del terreno. Coordenadas de MU: X e Y sobre el
/// piso (0..25600) y Z hacia arriba.
/// </summary>
public sealed class Camera
{
    public Vector3 Target = new(12800, 12800, 0);
    public float Distance = 3000f;
    public float Yaw = 45f * MathF.PI / 180f;
    public float Pitch = 45f * MathF.PI / 180f;
    public float Fov = 45f * MathF.PI / 180f;
    public float Near = 20f, Far = 60000f;

    public Matrix4x4 View { get; private set; } = Matrix4x4.Identity;
    public Matrix4x4 Projection { get; private set; } = Matrix4x4.Identity;
    public Matrix4x4 ViewProjection { get; private set; } = Matrix4x4.Identity;
    public Vector3 Position { get; private set; }

    public void Update(int width, int height)
    {
        Pitch = Math.Clamp(Pitch, 0.05f, MathF.PI / 2f - 0.02f);
        Distance = Math.Clamp(Distance, 80f, 40000f);

        float cp = MathF.Cos(Pitch), sp = MathF.Sin(Pitch);
        var offset = new Vector3(MathF.Cos(Yaw) * cp, MathF.Sin(Yaw) * cp, sp) * Distance;
        Position = Target + offset;

        View = Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitZ);
        Projection = Matrix4x4.CreatePerspectiveFieldOfView(Fov, Math.Max(0.1f, (float)width / Math.Max(1, height)), Near, Far);
        ViewProjection = View * Projection;
    }

    /// <summary>Rayo desde el pixel de pantalla hacia la escena.</summary>
    public void ScreenRay(int px, int py, int width, int height, out Vector3 origin, out Vector3 dir)
    {
        float ndcX = 2f * px / Math.Max(1, width) - 1f;
        float ndcY = 1f - 2f * py / Math.Max(1, height);

        Matrix4x4.Invert(ViewProjection, out var inv);
        var near = Vector4.Transform(new Vector4(ndcX, ndcY, -1f, 1f), inv);
        var far = Vector4.Transform(new Vector4(ndcX, ndcY, 1f, 1f), inv);
        var n = new Vector3(near.X, near.Y, near.Z) / near.W;
        var f = new Vector3(far.X, far.Y, far.Z) / far.W;

        origin = n;
        dir = Vector3.Normalize(f - n);
    }

    public void Pan(float dx, float dy)
    {
        // desplaza el objetivo sobre el plano del piso, segun hacia donde mira
        var forward = new Vector3(-MathF.Cos(Yaw), -MathF.Sin(Yaw), 0);
        var right = new Vector3(-forward.Y, forward.X, 0);
        Target += right * dx + forward * dy;
        Target = new Vector3(Math.Clamp(Target.X, -5000, 30000), Math.Clamp(Target.Y, -5000, 30000), Target.Z);
    }
}
