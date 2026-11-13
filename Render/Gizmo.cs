using System.Numerics;

namespace MuWorldEditor.Render;

public enum GizmoAxis { None, X, Y, Z, XY, XZ, YZ }

/// <summary>
/// Las flechas que aparecen sobre el objeto seleccionado, como en el editor
/// original: una por eje para mover, anillos para girar y cubos para escalar.
/// Siempre se ven del mismo tamano, no importa el zoom.
/// </summary>
public sealed class Gizmo
{
    public Vector3 Origin;
    public GizmoAxis Hover = GizmoAxis.None;
    public GizmoAxis Active = GizmoAxis.None;
    public bool Visible;

    /// <summary>Largo del eje en unidades de mundo, calculado para la camara actual.</summary>
    public float Length { get; private set; } = 300f;

    private static readonly Color ColX = Color.FromArgb(235, 70, 70);
    private static readonly Color ColY = Color.FromArgb(90, 220, 90);
    private static readonly Color ColZ = Color.FromArgb(90, 150, 255);
    private static readonly Color ColHot = Color.FromArgb(255, 230, 90);

    public void Update(Camera cam) => Length = cam.Distance * 0.16f;

    public static Vector3 Direction(GizmoAxis a) => a switch
    {
        GizmoAxis.X => Vector3.UnitX,
        GizmoAxis.Y => Vector3.UnitY,
        GizmoAxis.Z => Vector3.UnitZ,
        _ => Vector3.Zero
    };

    private Color ColorOf(GizmoAxis a)
    {
        if (Active == a || (Active == GizmoAxis.None && Hover == a)) return ColHot;
        return a switch
        {
            GizmoAxis.X => ColX, GizmoAxis.Y => ColY, GizmoAxis.Z => ColZ,
            GizmoAxis.XY => ColZ, GizmoAxis.XZ => ColY, GizmoAxis.YZ => ColX,
            _ => ColZ
        };
    }

    public static bool IsPlane(GizmoAxis a) => a is GizmoAxis.XY or GizmoAxis.XZ or GizmoAxis.YZ;

    /// <summary>Los dos ejes que forman el plano, y su normal.</summary>
    public static void PlaneAxes(GizmoAxis a, out Vector3 u, out Vector3 v, out Vector3 n)
    {
        switch (a)
        {
            case GizmoAxis.XZ: u = Vector3.UnitX; v = Vector3.UnitZ; n = Vector3.UnitY; break;
            case GizmoAxis.YZ: u = Vector3.UnitY; v = Vector3.UnitZ; n = Vector3.UnitX; break;
            default: u = Vector3.UnitX; v = Vector3.UnitY; n = Vector3.UnitZ; break;
        }
    }

    private Vector3[] PlaneQuad(GizmoAxis a)
    {
        PlaneAxes(a, out var u, out var v, out _);
        float a0 = Length * 0.28f, a1 = Length * 0.5f;
        return new[] { Origin + u * a0 + v * a0, Origin + u * a1 + v * a0, Origin + u * a1 + v * a1, Origin + u * a0 + v * a1 };
    }

    // ------------------------------------------------------------- dibujo
    public void Draw(LineBatch lines, QuadBatch quads, Editing.GizmoMode mode, Camera cam)
    {
        if (!Visible) return;
        Update(cam);

        switch (mode)
        {
            case Editing.GizmoMode.Move: DrawArrows(lines, quads); break;
            case Editing.GizmoMode.Rotate: DrawRings(lines, cam); break;
            case Editing.GizmoMode.Scale: DrawScale(lines, quads); break;
        }
    }

    private void DrawArrows(LineBatch lines, QuadBatch quads)
    {
        // cuadraditos para mover en dos ejes a la vez
        foreach (var plane in new[] { GizmoAxis.XY, GizmoAxis.XZ, GizmoAxis.YZ })
        {
            var q = PlaneQuad(plane);
            var col = ColorOf(plane);
            bool hot = col == ColHot;
            quads.AddQuad(q[0], q[1], q[2], q[3], Color.FromArgb(hot ? 150 : 70, col));
            for (int i = 0; i < 4; i++) lines.AddLine(q[i], q[(i + 1) % 4], Color.FromArgb(hot ? 255 : 200, col));
        }
        Cube(quads, Origin, Length * 0.035f, Color.FromArgb(235, 240, 240, 245));

        foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
        {
            var dir = Direction(axis);
            var col = ColorOf(axis);
            var tip = Origin + dir * Length;

            lines.AddLine(Origin, tip, col);
            ArrowHead(quads, tip, dir, Length * 0.22f, Length * 0.075f, col);
        }
    }

    private void DrawScale(LineBatch lines, QuadBatch quads)
    {
        foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
        {
            var dir = Direction(axis);
            var col = ColorOf(axis);
            var tip = Origin + dir * Length;
            lines.AddLine(Origin, tip, col);
            Cube(quads, tip, Length * 0.06f, col);
        }
    }

    private void DrawRings(LineBatch lines, Camera cam)
    {
        const int steps = 56;
        foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
        {
            var col = ColorOf(axis);
            Vector3 u, v;
            switch (axis)
            {
                case GizmoAxis.X: u = Vector3.UnitY; v = Vector3.UnitZ; break;
                case GizmoAxis.Y: u = Vector3.UnitZ; v = Vector3.UnitX; break;
                default: u = Vector3.UnitX; v = Vector3.UnitY; break;
            }

            Vector3 prev = default;
            for (int i = 0; i <= steps; i++)
            {
                float a = i / (float)steps * MathF.Tau;
                var p = Origin + (u * MathF.Cos(a) + v * MathF.Sin(a)) * Length;
                if (i > 0) lines.AddLine(prev, p, col);
                prev = p;
            }
        }
    }

    private static void ArrowHead(QuadBatch quads, Vector3 tip, Vector3 dir, float len, float radius, Color col)
    {
        var baseC = tip - dir * len;
        Perp(dir, out var u, out var v);
        const int seg = 10;

        for (int i = 0; i < seg; i++)
        {
            float a0 = i / (float)seg * MathF.Tau;
            float a1 = (i + 1) / (float)seg * MathF.Tau;
            var p0 = baseC + (u * MathF.Cos(a0) + v * MathF.Sin(a0)) * radius;
            var p1 = baseC + (u * MathF.Cos(a1) + v * MathF.Sin(a1)) * radius;
            quads.AddQuad(p0, p1, tip, tip, col);        // costado del cono
            quads.AddQuad(baseC, p1, p0, baseC, col);    // tapa
        }
    }

    private static void Cube(QuadBatch quads, Vector3 c, float r, Color col)
    {
        Vector3 P(int x, int y, int z) => c + new Vector3(x * r, y * r, z * r);
        var a = P(-1, -1, -1); var b = P(1, -1, -1); var d = P(1, 1, -1); var e = P(-1, 1, -1);
        var f = P(-1, -1, 1); var g = P(1, -1, 1); var h = P(1, 1, 1); var i = P(-1, 1, 1);

        quads.AddQuad(a, b, d, e, col);
        quads.AddQuad(f, g, h, i, col);
        quads.AddQuad(a, b, g, f, col);
        quads.AddQuad(e, d, h, i, col);
        quads.AddQuad(a, e, i, f, col);
        quads.AddQuad(b, d, h, g, col);
    }

    private static void Perp(Vector3 dir, out Vector3 u, out Vector3 v)
    {
        var helper = MathF.Abs(dir.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        u = Vector3.Normalize(Vector3.Cross(dir, helper));
        v = Vector3.Normalize(Vector3.Cross(dir, u));
    }

    // ------------------------------------------------------------ picking
    /// <summary>Que eje esta debajo del mouse, mirando la pantalla en pixeles.</summary>
    public GizmoAxis Pick(Camera cam, int mouseX, int mouseY, int width, int height, Editing.GizmoMode mode)
    {
        if (!Visible) return GizmoAxis.None;
        Update(cam);

        var mouse = new Vector2(mouseX, mouseY);
        GizmoAxis best = GizmoAxis.None;
        float bestDist = mode == Editing.GizmoMode.Rotate ? 12f : 11f;

        if (mode == Editing.GizmoMode.Move)
        {
            foreach (var plane in new[] { GizmoAxis.XY, GizmoAxis.XZ, GizmoAxis.YZ })
            {
                var q = PlaneQuad(plane);
                var pts = new Vector2[4];
                bool ok = true;
                for (int i = 0; i < 4 && ok; i++) ok = Project(cam, q[i], width, height, out pts[i]);
                if (ok && InsideQuad(mouse, pts)) return plane;
            }
        }

        foreach (var axis in new[] { GizmoAxis.X, GizmoAxis.Y, GizmoAxis.Z })
        {
            float d;
            if (mode == Editing.GizmoMode.Rotate)
            {
                d = RingDistance(cam, axis, mouse, width, height);
            }
            else
            {
                if (!Project(cam, Origin, width, height, out var a)) continue;
                if (!Project(cam, Origin + Direction(axis) * Length, width, height, out var b)) continue;
                d = SegmentDistance(mouse, a, b);
            }

            if (d < bestDist) { bestDist = d; best = axis; }
        }
        return best;
    }

    private static bool InsideQuad(Vector2 p, Vector2[] q)
    {
        // el punto esta del mismo lado de los cuatro bordes
        int sign = 0;
        for (int i = 0; i < 4; i++)
        {
            var a = q[i]; var b = q[(i + 1) % 4];
            float cross = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
            int s = cross > 0 ? 1 : cross < 0 ? -1 : 0;
            if (s == 0) continue;
            if (sign == 0) sign = s;
            else if (s != sign) return false;
        }
        return true;
    }

    /// <summary>Donde cruza el rayo del mouse el plano del cuadradito elegido.</summary>
    public bool PlanePoint(Camera cam, GizmoAxis plane, int mouseX, int mouseY, int w, int h, out Vector3 point)
    {
        point = default;
        if (!IsPlane(plane)) return false;
        PlaneAxes(plane, out _, out _, out var n);
        cam.ScreenRay(mouseX, mouseY, w, h, out var ro, out var rd);
        float denom = Vector3.Dot(n, rd);
        if (MathF.Abs(denom) < 1e-4f) return false;
        float t = Vector3.Dot(Origin - ro, n) / denom;
        if (t <= 0) return false;
        point = ro + rd * t;
        return true;
    }

    private float RingDistance(Camera cam, GizmoAxis axis, Vector2 mouse, int w, int h)
    {
        Vector3 u, v;
        switch (axis)
        {
            case GizmoAxis.X: u = Vector3.UnitY; v = Vector3.UnitZ; break;
            case GizmoAxis.Y: u = Vector3.UnitZ; v = Vector3.UnitX; break;
            default: u = Vector3.UnitX; v = Vector3.UnitY; break;
        }

        float best = float.MaxValue;
        const int steps = 48;
        Vector2 prev = default;
        bool havePrev = false;
        for (int i = 0; i <= steps; i++)
        {
            float a = i / (float)steps * MathF.Tau;
            var p = Origin + (u * MathF.Cos(a) + v * MathF.Sin(a)) * Length;
            if (!Project(cam, p, w, h, out var sp)) { havePrev = false; continue; }
            if (havePrev) best = MathF.Min(best, SegmentDistance(mouse, prev, sp));
            prev = sp; havePrev = true;
        }
        return best;
    }

    public static bool Project(Camera cam, Vector3 world, int w, int h, out Vector2 screen)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), cam.ViewProjection);
        if (clip.W <= 0.0001f) { screen = default; return false; }
        var ndc = new Vector3(clip.X, clip.Y, clip.Z) / clip.W;
        screen = new Vector2((ndc.X * 0.5f + 0.5f) * w, (1f - (ndc.Y * 0.5f + 0.5f)) * h);
        return true;
    }

    private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < 1e-4f) return Vector2.Distance(p, a);
        float t = Math.Clamp(Vector2.Dot(p - a, ab) / len2, 0f, 1f);
        return Vector2.Distance(p, a + ab * t);
    }

    /// <summary>
    /// Cuanto avanzo el mouse a lo largo del eje: devuelve el punto del eje mas
    /// cercano al rayo del mouse.
    /// </summary>
    public bool AxisPoint(Camera cam, GizmoAxis axis, int mouseX, int mouseY, int w, int h, out float t)
    {
        t = 0;
        if (axis == GizmoAxis.None) return false;

        cam.ScreenRay(mouseX, mouseY, w, h, out var ro, out var rd);
        var d = Direction(axis);
        var w0 = Origin - ro;

        float b = Vector3.Dot(d, rd);
        float denom = 1f - b * b;
        if (MathF.Abs(denom) < 1e-5f) return false;      // el eje mira a la camara

        float dd = Vector3.Dot(d, w0);
        float e = Vector3.Dot(rd, w0);
        // punto del eje mas cercano al rayo del mouse (dos rectas que se cruzan)
        t = (b * e - dd) / denom;
        return true;
    }

    /// <summary>Angulo del mouse alrededor del centro del gizmo, en radianes.</summary>
    public float ScreenAngle(Camera cam, int mouseX, int mouseY, int w, int h)
    {
        if (!Project(cam, Origin, w, h, out var c)) return 0;
        return MathF.Atan2(mouseY - c.Y, mouseX - c.X);
    }

    /// <summary>Distancia del mouse al centro, en pixeles (para escalar).</summary>
    public float ScreenRadius(Camera cam, int mouseX, int mouseY, int w, int h)
    {
        if (!Project(cam, Origin, w, h, out var c)) return 1;
        return MathF.Max(4f, Vector2.Distance(new Vector2(mouseX, mouseY), c));
    }
}
