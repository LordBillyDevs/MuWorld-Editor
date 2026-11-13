using System.Drawing.Drawing2D;

namespace MuWorldEditor.App;

/// <summary>
/// Iconos de linea dibujados por codigo, sin archivos aparte. Cada uno se
/// arma en una grilla de 24x24 y se escala al tamano pedido, asi se ven
/// nitidos en cualquier DPI y de cualquier color.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<(string, int, int), Bitmap> Cache = new();

    public static Bitmap Get(string name, int size, Color color)
    {
        var key = (name, size, color.ToArgb());
        if (Cache.TryGetValue(key, out var bmp)) return bmp;

        bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.ScaleTransform(size / 24f, size / 24f);
            float w = size <= 16 ? 2.1f : 1.8f;
            using var pen = new Pen(color, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using var fill = new SolidBrush(color);
            Draw(g, name, pen, fill, color);
        }
        Cache[key] = bmp;
        return bmp;
    }

    private static void Draw(Graphics g, string name, Pen p, Brush f, Color c)
    {
        switch (name)
        {
            case "folder":
                using (var path = new GraphicsPath())
                {
                    path.AddLines(new PointF[] { new(3, 6.5f), new(9, 6.5f), new(11, 8.5f), new(21, 8.5f), new(21, 19), new(3, 19) });
                    path.CloseFigure();
                    g.DrawPath(p, path);
                }
                g.DrawLine(p, 3, 11.5f, 21, 11.5f);
                break;

            case "save":
                using (var path = DarkTheme.Round(new RectangleF(4, 4, 16, 16), 2.5f)) g.DrawPath(p, path);
                g.DrawRectangle(p, 8, 4, 8, 4.5f);
                g.DrawRectangle(p, 7.5f, 13, 9, 7);
                break;

            case "undo":
                g.DrawLines(p, new PointF[] { new(8.5f, 4.5f), new(4.5f, 8.5f), new(8.5f, 12.5f) });
                using (var path = new GraphicsPath())
                {
                    path.AddLine(4.5f, 8.5f, 14, 8.5f);
                    path.AddArc(9, 8.5f, 10, 10, -90, 180);
                    path.AddLine(14, 18.5f, 8, 18.5f);
                    g.DrawPath(p, path);
                }
                break;

            case "redo":
                g.TranslateTransform(24, 0); g.ScaleTransform(-1, 1);
                Draw(g, "undo", p, f, c);
                break;

            case "pan":
                g.DrawLine(p, 12, 3, 12, 21); g.DrawLine(p, 3, 12, 21, 12);
                g.DrawLines(p, new PointF[] { new(9.5f, 5.5f), new(12, 3), new(14.5f, 5.5f) });
                g.DrawLines(p, new PointF[] { new(9.5f, 18.5f), new(12, 21), new(14.5f, 18.5f) });
                g.DrawLines(p, new PointF[] { new(5.5f, 9.5f), new(3, 12), new(5.5f, 14.5f) });
                g.DrawLines(p, new PointF[] { new(18.5f, 9.5f), new(21, 12), new(18.5f, 14.5f) });
                break;

            case "orbit":
                g.DrawArc(p, 3, 7, 18, 10, 200, 300);
                g.DrawLines(p, new PointF[] { new(4.2f, 7.8f), new(3.6f, 11.2f), new(6.8f, 12.2f) });
                g.FillEllipse(f, 9.5f, 9.5f, 5, 5);
                break;

            case "rotate":
                g.DrawArc(p, 4.5f, 4.5f, 15, 15, -60, 300);
                g.DrawLines(p, new PointF[] { new(15.5f, 2.8f), new(16.3f, 6.6f), new(12.4f, 7.2f) });
                break;

            case "scale":
                g.DrawRectangle(p, 3.5f, 11.5f, 9, 9);
                g.DrawLine(p, 11, 13, 20, 4);
                g.DrawLines(p, new PointF[] { new(14.5f, 4), new(20, 4), new(20, 9.5f) });
                break;

            case "eye":
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(2.5f, 12, 6, 5.5f, 18, 5.5f, 21.5f, 12);
                    path.AddBezier(21.5f, 12, 18, 18.5f, 6, 18.5f, 2.5f, 12);
                    g.DrawPath(p, path);
                }
                g.DrawEllipse(p, 9, 9, 6, 6);
                break;

            case "mountain":
                using (var path = new GraphicsPath())
                {
                    path.AddLines(new PointF[] { new(2.5f, 19), new(9, 7.5f), new(13, 14), new(15.5f, 10.5f), new(21.5f, 19) });
                    path.CloseFigure();
                    g.DrawPath(p, path);
                }
                g.DrawLines(p, new PointF[] { new(7.2f, 10.7f), new(9, 12), new(10.6f, 10.4f) });
                break;

            case "flag":
                g.DrawLine(p, 6, 3.5f, 6, 21);
                using (var path = new GraphicsPath())
                {
                    path.AddLines(new PointF[] { new(6, 4.5f), new(18.5f, 4.5f), new(15.5f, 8.5f), new(18.5f, 12.5f), new(6, 12.5f) });
                    g.DrawPath(p, path);
                }
                break;

            case "tiles":
                foreach (var (x, y) in new[] { (4f, 4f), (13f, 4f), (4f, 13f), (13f, 13f) })
                    using (var path = DarkTheme.Round(new RectangleF(x, y, 7, 7), 1.5f)) g.DrawPath(p, path);
                break;

            case "blend":
                g.DrawEllipse(p, 4, 4, 16, 16);
                g.FillPie(f, 4, 4, 16, 16, 90, 180);
                break;

            case "cube":
                using (var path = new GraphicsPath())
                {
                    path.AddLines(new PointF[] { new(12, 3), new(20, 7.5f), new(20, 16.5f), new(12, 21), new(4, 16.5f), new(4, 7.5f) });
                    path.CloseFigure();
                    g.DrawPath(p, path);
                }
                g.DrawLines(p, new PointF[] { new(4, 7.5f), new(12, 12), new(20, 7.5f) });
                g.DrawLine(p, 12, 12, 12, 21);
                break;

            case "area":
                using (var dashed = (Pen)p.Clone())
                {
                    dashed.DashPattern = new[] { 1.6f, 1.6f };
                    g.DrawRectangle(dashed, 5, 5, 14, 14);
                }
                foreach (var (x, y) in new[] { (3f, 3f), (17f, 3f), (3f, 17f), (17f, 17f) })
                    g.FillRectangle(f, x, y, 4, 4);
                break;

            case "camera":
                using (var path = DarkTheme.Round(new RectangleF(3, 7, 18, 13), 2.5f)) g.DrawPath(p, path);
                g.DrawLines(p, new PointF[] { new(8, 7), new(9.5f, 4.5f), new(14.5f, 4.5f), new(16, 7) });
                g.DrawEllipse(p, 8.5f, 9.5f, 7, 7);
                break;

            case "map":
                g.DrawLines(p, new PointF[] { new(3, 6), new(9, 4), new(15, 6), new(21, 4), new(21, 18), new(15, 20), new(9, 18), new(3, 20), new(3, 6) });
                g.DrawLine(p, 9, 4, 9, 18); g.DrawLine(p, 15, 6, 15, 20);
                break;

            case "panel":
                using (var path = DarkTheme.Round(new RectangleF(3, 4, 18, 16), 2.5f)) g.DrawPath(p, path);
                g.DrawLine(p, 14.5f, 4, 14.5f, 20);
                g.DrawLine(p, 17, 8, 19, 8); g.DrawLine(p, 17, 11, 19, 11);
                break;

            case "globe":
                g.DrawEllipse(p, 3, 3, 18, 18);
                g.DrawEllipse(p, 8, 3, 8, 18);
                g.DrawLine(p, 3, 12, 21, 12);
                break;

            case "help":
                g.DrawEllipse(p, 3, 3, 18, 18);
                using (var path = new GraphicsPath())
                {
                    path.AddArc(9, 7, 6, 6, 180, 230);
                    path.AddLine(14.4f, 11.6f, 12, 13.4f);
                    path.AddLine(12, 13.4f, 12, 14.4f);
                    g.DrawPath(p, path);
                }
                g.FillEllipse(f, 10.9f, 16, 2.2f, 2.2f);
                break;

            case "home":
                g.DrawLines(p, new PointF[] { new(3, 11), new(12, 3.5f), new(21, 11) });
                g.DrawLines(p, new PointF[] { new(5.5f, 9.5f), new(5.5f, 20), new(18.5f, 20), new(18.5f, 9.5f) });
                g.DrawRectangle(p, 10, 14, 4, 6);
                break;

            case "plus":
                g.DrawLine(p, 12, 5, 12, 19); g.DrawLine(p, 5, 12, 19, 12);
                break;

            case "trash":
                g.DrawLine(p, 4, 6.5f, 20, 6.5f);
                g.DrawLines(p, new PointF[] { new(9, 6.5f), new(9.5f, 3.5f), new(14.5f, 3.5f), new(15, 6.5f) });
                g.DrawLines(p, new PointF[] { new(6, 6.5f), new(7, 20.5f), new(17, 20.5f), new(18, 6.5f) });
                g.DrawLine(p, 10, 10, 10, 17); g.DrawLine(p, 14, 10, 14, 17);
                break;

            case "copy":
                using (var path = DarkTheme.Round(new RectangleF(8, 8, 12, 12), 2)) g.DrawPath(p, path);
                g.DrawLines(p, new PointF[] { new(16, 8), new(16, 4), new(4, 4), new(4, 16), new(8, 16) });
                break;

            case "paste":
                using (var path = DarkTheme.Round(new RectangleF(5, 5, 14, 16), 2)) g.DrawPath(p, path);
                using (var path = DarkTheme.Round(new RectangleF(9, 3, 6, 4), 1)) g.DrawPath(p, path);
                g.DrawLine(p, 9, 12, 15, 12); g.DrawLine(p, 9, 16, 13, 16);
                break;

            case "duplicate":
                using (var path = DarkTheme.Round(new RectangleF(3.5f, 3.5f, 12, 12), 2)) g.DrawPath(p, path);
                using (var path = DarkTheme.Round(new RectangleF(8.5f, 8.5f, 12, 12), 2)) g.DrawPath(p, path);
                g.DrawLine(p, 14.5f, 12, 14.5f, 17); g.DrawLine(p, 12, 14.5f, 17, 14.5f);
                break;

            case "ground":
                g.DrawLine(p, 12, 3, 12, 14);
                g.DrawLines(p, new PointF[] { new(8, 10.5f), new(12, 14.5f), new(16, 10.5f) });
                g.DrawLine(p, 4, 19, 20, 19);
                break;

            case "focus":
                g.DrawLines(p, new PointF[] { new(4, 9), new(4, 4), new(9, 4) });
                g.DrawLines(p, new PointF[] { new(15, 4), new(20, 4), new(20, 9) });
                g.DrawLines(p, new PointF[] { new(20, 15), new(20, 20), new(15, 20) });
                g.DrawLines(p, new PointF[] { new(9, 20), new(4, 20), new(4, 15) });
                g.FillEllipse(f, 9.5f, 9.5f, 5, 5);
                break;

            case "search":
                g.DrawEllipse(p, 4, 4, 12, 12);
                g.DrawLine(p, 14.5f, 14.5f, 20, 20);
                break;

            case "chevron-down":
                g.DrawLines(p, new PointF[] { new(6, 9), new(12, 15), new(18, 9) });
                break;

            case "chevron-right":
                g.DrawLines(p, new PointF[] { new(9, 6), new(15, 12), new(9, 18) });
                break;

            case "close":
                g.DrawLine(p, 6, 6, 18, 18); g.DrawLine(p, 18, 6, 6, 18);
                break;

            case "minimize":
                g.DrawLine(p, 6, 12.5f, 18, 12.5f);
                break;

            case "maximize":
                using (var path = DarkTheme.Round(new RectangleF(6, 6, 12, 12), 1.5f)) g.DrawPath(p, path);
                break;

            case "restore":
                using (var path = DarkTheme.Round(new RectangleF(5, 8.5f, 10.5f, 10.5f), 1.5f)) g.DrawPath(p, path);
                g.DrawLines(p, new PointF[] { new(8.5f, 8.5f), new(8.5f, 5), new(19, 5), new(19, 15.5f), new(15.5f, 15.5f) });
                break;

            case "sun":
                g.DrawEllipse(p, 8, 8, 8, 8);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathF.PI / 4f;
                    g.DrawLine(p, 12 + MathF.Cos(a) * 6.5f, 12 + MathF.Sin(a) * 6.5f, 12 + MathF.Cos(a) * 9f, 12 + MathF.Sin(a) * 9f);
                }
                break;

            case "grid":
                g.DrawRectangle(p, 4, 4, 16, 16);
                g.DrawLine(p, 9.3f, 4, 9.3f, 20); g.DrawLine(p, 14.7f, 4, 14.7f, 20);
                g.DrawLine(p, 4, 9.3f, 20, 9.3f); g.DrawLine(p, 4, 14.7f, 20, 14.7f);
                break;

            case "layers":
                g.DrawPolygon(p, new PointF[] { new(12, 4), new(21, 8.5f), new(12, 13), new(3, 8.5f) });
                g.DrawLines(p, new PointF[] { new(3, 12.5f), new(12, 17), new(21, 12.5f) });
                g.DrawLines(p, new PointF[] { new(3, 16.5f), new(12, 21), new(21, 16.5f) });
                break;

            case "wire":
                g.DrawPolygon(p, new PointF[] { new(12, 3.5f), new(20.5f, 19.5f), new(3.5f, 19.5f) });
                g.DrawLine(p, 12, 3.5f, 12, 19.5f);
                g.DrawLine(p, 7.8f, 11.5f, 16.2f, 11.5f);
                break;

            case "keyboard":
                using (var path = DarkTheme.Round(new RectangleF(2.5f, 6, 19, 12), 2)) g.DrawPath(p, path);
                for (int i = 0; i < 4; i++) g.FillRectangle(f, 5.5f + i * 3.6f, 9, 1.8f, 1.8f);
                g.DrawLine(p, 8, 14.5f, 16, 14.5f);
                break;

            case "image":
                using (var path = DarkTheme.Round(new RectangleF(3, 4, 18, 16), 2)) g.DrawPath(p, path);
                g.DrawLines(p, new PointF[] { new(3.5f, 17), new(9, 11.5f), new(13, 15.5f), new(15.5f, 13), new(20.5f, 18) });
                g.FillEllipse(f, 14.5f, 7, 3, 3);
                break;

            case "refresh":
                g.DrawArc(p, 4.5f, 4.5f, 15, 15, 30, 300);
                g.DrawLines(p, new PointF[] { new(19.8f, 4.5f), new(19.3f, 8.8f), new(15, 8.4f) });
                break;

            case "picker":
                g.DrawLine(p, 5, 19, 13, 11);
                using (var path = DarkTheme.Round(new RectangleF(12.5f, 4.5f, 7, 7), 3.5f)) g.DrawPath(p, path);
                g.DrawLine(p, 11, 9, 15, 13);
                break;

            case "brush":
                g.DrawLine(p, 19.5f, 4.5f, 11, 13);
                using (var path = new GraphicsPath())
                {
                    path.AddBezier(11, 13, 7, 12, 5.5f, 15, 6, 19);
                    path.AddBezier(6, 19, 9, 18.5f, 12.5f, 17.5f, 11, 13);
                    g.DrawPath(p, path);
                }
                break;

            case "info":
                g.DrawEllipse(p, 3, 3, 18, 18);
                g.DrawLine(p, 12, 11, 12, 17);
                g.FillEllipse(f, 10.9f, 6.6f, 2.2f, 2.2f);
                break;

            case "warning":
                g.DrawPolygon(p, new PointF[] { new(12, 3.5f), new(21.5f, 20), new(2.5f, 20) });
                g.DrawLine(p, 12, 9.5f, 12, 14);
                g.FillEllipse(f, 10.9f, 16, 2.2f, 2.2f);
                break;

            case "error":
                g.DrawEllipse(p, 3, 3, 18, 18);
                g.DrawLine(p, 9, 9, 15, 15); g.DrawLine(p, 15, 9, 9, 15);
                break;

            case "check":
                g.DrawLines(p, new PointF[] { new(5, 12.5f), new(10, 17.5f), new(19.5f, 6.5f) });
                break;

            case "question":
                g.DrawEllipse(p, 3, 3, 18, 18);
                using (var path = new GraphicsPath())
                {
                    path.AddArc(9, 7, 6, 6, 180, 230);
                    path.AddLine(14.4f, 11.6f, 12, 13.4f);
                    path.AddLine(12, 13.4f, 12, 14.4f);
                    g.DrawPath(p, path);
                }
                g.FillEllipse(f, 10.9f, 16, 2.2f, 2.2f);
                break;

            case "logo":
                // emblema del editor: rombo dorado con un "mapa" adentro
                g.DrawPolygon(p, new PointF[] { new(12, 2), new(22, 12), new(12, 22), new(2, 12) });
                g.DrawLines(p, new PointF[] { new(7, 13.5f), new(10.5f, 9.5f), new(13, 12.5f), new(14.8f, 10.8f), new(17.5f, 13.5f) });
                g.FillEllipse(f, 11, 5.5f, 2, 2);
                break;

            default:
                g.DrawEllipse(p, 5, 5, 14, 14);
                break;
        }
    }
}
