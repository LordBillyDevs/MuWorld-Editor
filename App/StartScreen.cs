using System.Drawing.Drawing2D;

namespace MuWorldEditor.App;

/// <summary>
/// Pantalla de inicio: se ve cuando no hay mapa abierto (o con el boton
/// Inicio). Muestra los mapas recientes con su miniatura y botones grandes
/// para elegir el cliente o abrir un mapa suelto.
/// </summary>
public sealed class StartScreen : Control
{
    private List<RecentMap> _recent = new();
    private readonly Dictionary<string, Image> _thumbs = new(StringComparer.OrdinalIgnoreCase);
    private int _hot = -1, _hotRemove = -1;
    private readonly ModernButton _btnClient, _btnLoose, _btnBack;
    private int _scroll;

    public event Action ChooseClient, OpenLoose, Back;
    public event Action<RecentMap> OpenRecent;
    public event Action<RecentMap> RemoveRecent;

    private const int CardW = 236, CardH = 196, Gap = 18;

    public StartScreen()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = DarkTheme.Back;
        Dock = DockStyle.Fill;

        _btnClient = new ModernButton { Text = Lang.T("ui.elegir_cliente", "Elegir carpeta del cliente"), IconName = "folder", Primary = true, Width = 250, Height = 40 };
        _btnLoose = new ModernButton { Text = Lang.T("ui.abrir_mapa_suelto_corto", "Abrir un mapa suelto"), IconName = "map", Width = 220, Height = 40 };
        _btnBack = new ModernButton { Text = Lang.T("ui.volver_al_mapa", "Volver al mapa"), IconName = "eye", Width = 170, Height = 40, Visible = false };
        _btnClient.Click += (_, _) => ChooseClient?.Invoke();
        _btnLoose.Click += (_, _) => OpenLoose?.Invoke();
        _btnBack.Click += (_, _) => Back?.Invoke();
        Controls.Add(_btnClient);
        Controls.Add(_btnLoose);
        Controls.Add(_btnBack);
    }

    /// <summary>Hay un mapa abierto detras: se muestra el boton para volver.</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool CanGoBack { get => _btnBack.Visible; set { _btnBack.Visible = value; PerformLayout(); } }

    public void SetRecent(IEnumerable<RecentMap> list)
    {
        _recent = list.ToList();
        foreach (var t in _thumbs.Values) t.Dispose();
        _thumbs.Clear();
        foreach (var r in _recent)
        {
            string p = Settings.ThumbPath(r.Folder);
            try
            {
                if (File.Exists(p))
                    using (var fs = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        _thumbs[r.Folder] = new Bitmap(Image.FromStream(fs));
            }
            catch { }
        }
        _scroll = 0;
        Invalidate();
    }

    private int Left0 => Math.Max(48, (Width - Columns * (CardW + Gap) + Gap) / 2);
    private int Columns => Math.Max(1, Math.Min(5, (Width - 96 + Gap) / (CardW + Gap)));
    private int CardsTop => 250;

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int x = Left0;
        _btnClient.Location = new Point(x, 150);
        _btnLoose.Location = new Point(x + _btnClient.Width + 12, 150);
        _btnBack.Location = new Point(_btnLoose.Right + 12, 150);
    }

    private Rectangle CardRect(int i)
    {
        int col = i % Columns, row = i / Columns;
        return new Rectangle(Left0 + col * (CardW + Gap), CardsTop + 40 + row * (CardH + Gap) - _scroll, CardW, CardH);
    }

    private Rectangle RemoveRect(Rectangle card) => new(card.Right - 30, card.Y + 8, 22, 22);

    private int HitCard(Point p)
    {
        for (int i = 0; i < _recent.Count; i++) if (CardRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitCard(e.Location);
        int hr = h >= 0 && RemoveRect(CardRect(h)).Contains(e.Location) ? h : -1;
        if (h != _hot || hr != _hotRemove) { _hot = h; _hotRemove = hr; Invalidate(); }
        Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = _hotRemove = -1; Invalidate(); }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        int h = HitCard(e.Location);
        if (h < 0 || e.Button != MouseButtons.Left) return;
        var r = _recent[h];
        if (RemoveRect(CardRect(h)).Contains(e.Location)) { RemoveRecent?.Invoke(r); return; }
        OpenRecent?.Invoke(r);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int rows = (int)Math.Ceiling(_recent.Count / (float)Columns);
        int content = CardsTop + 40 + rows * (CardH + Gap);
        int maxScroll = Math.Max(0, content - Height + 24);
        _scroll = Math.Clamp(_scroll - e.Delta / 2, 0, maxScroll);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(24, 22, 20), DarkTheme.Back, 75f))
            g.FillRectangle(bg, ClientRectangle);

        // resplandor dorado arriba a la izquierda
        using (var glow = new GraphicsPath())
        {
            glow.AddEllipse(-200, -260, 760, 520);
            using var pgb = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(34, DarkTheme.Accent), SurroundColors = new[] { Color.FromArgb(0, DarkTheme.Accent) } };
            g.FillPath(pgb, glow);
        }

        int x = Left0;
        g.DrawImage(Icons.Get("logo", 52, DarkTheme.Accent), x, 58);
        TextRenderer.DrawText(g, "MU World Editor", DarkTheme.HeroFont, new Point(x + 66, 50), DarkTheme.Text, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, Lang.T("ui.inicio_sub", "Abri un mapa reciente o elegi la carpeta Data del cliente para empezar."),
                              DarkTheme.UiFont, new Point(x + 70, 100), DarkTheme.TextDim, TextFormatFlags.NoPrefix);

        var clip = new Rectangle(0, CardsTop, Width, Height - CardsTop);
        TextRenderer.DrawText(g, Lang.T("ui.recientes", "Recientes").ToUpperInvariant(), DarkTheme.SectionFont,
                              new Point(x, CardsTop + 8), DarkTheme.Accent, TextFormatFlags.NoPrefix);
        using (var pen = new Pen(DarkTheme.BorderSoft))
            g.DrawLine(pen, x + 90, CardsTop + 15, Math.Min(Width - 48, x + Columns * (CardW + Gap) - Gap), CardsTop + 15);

        if (_recent.Count == 0)
        {
            TextRenderer.DrawText(g, Lang.T("ui.sin_recientes", "Todavia no abriste ningun mapa. Los que abras van a aparecer aca."),
                                  DarkTheme.UiFont, new Point(x, CardsTop + 44), DarkTheme.TextFaint, TextFormatFlags.NoPrefix);
            return;
        }

        g.SetClip(new Rectangle(0, CardsTop + 30, Width, Height - CardsTop - 30));
        for (int i = 0; i < _recent.Count; i++)
        {
            var r = CardRect(i);
            if (r.Bottom < CardsTop || r.Top > Height) continue;
            DrawCard(g, _recent[i], r, i == _hot, i == _hotRemove);
        }
        g.ResetClip();
    }

    private void DrawCard(Graphics g, RecentMap m, Rectangle r, bool hot, bool hotRemove)
    {
        var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
        using (var path = DarkTheme.Round(rf, 8))
        {
            using (var b = new SolidBrush(hot ? DarkTheme.Hover : DarkTheme.SurfaceAlt)) g.FillPath(b, path);
            using (var pen = new Pen(hot ? DarkTheme.Accent : DarkTheme.BorderSoft, hot ? 1.5f : 1f)) g.DrawPath(pen, path);
        }

        var thumbRect = new Rectangle(r.X + 8, r.Y + 8, r.Width - 16, 120);
        using (var tpath = DarkTheme.Round(thumbRect, 6))
        {
            g.SetClip(tpath, CombineMode.Intersect);
            if (_thumbs.TryGetValue(m.Folder, out var img))
                g.DrawImage(img, thumbRect);
            else
            {
                using var b = new SolidBrush(DarkTheme.Sunken);
                g.FillRectangle(b, thumbRect);
                g.DrawImage(Icons.Get("map", 32, DarkTheme.TextFaint), thumbRect.X + (thumbRect.Width - 32) / 2, thumbRect.Y + 44);
            }
            g.SetClip(new Rectangle(0, CardsTop + 30, Width, Height - CardsTop - 30));
        }

        string title = string.IsNullOrEmpty(m.Name) ? "World" + m.World : m.Name;
        TextRenderer.DrawText(g, title, DarkTheme.UiFontBold, new Rectangle(r.X + 12, r.Y + 134, r.Width - 24, 20), DarkTheme.Text,
                              TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        string sub = (string.IsNullOrEmpty(m.Format) ? "" : m.Format + "  ·  ") + Ago(m.Opened);
        TextRenderer.DrawText(g, sub, DarkTheme.SmallFont, new Rectangle(r.X + 12, r.Y + 154, r.Width - 24, 18), DarkTheme.TextDim,
                              TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, m.Folder, DarkTheme.SmallFont, new Rectangle(r.X + 12, r.Y + 172, r.Width - 24, 18), DarkTheme.TextFaint,
                              TextFormatFlags.PathEllipsis | TextFormatFlags.NoPrefix);

        if (hot)
        {
            var rr = RemoveRect(r);
            using (var b = new SolidBrush(hotRemove ? DarkTheme.Danger : Color.FromArgb(200, 16, 18, 23))) g.FillEllipse(b, rr);
            g.DrawImage(Icons.Get("close", 14, Color.White), rr.X + 4, rr.Y + 4);
        }
    }

    private static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        if (d.TotalMinutes < 1) return Lang.T("ui.recien", "recien");
        if (d.TotalHours < 1) return $"hace {(int)d.TotalMinutes} min";
        if (d.TotalDays < 1) return $"hace {(int)d.TotalHours} h";
        if (d.TotalDays < 30) return $"hace {(int)d.TotalDays} dias";
        return t.ToString("dd/MM/yyyy");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var t in _thumbs.Values) t.Dispose();
        base.Dispose(disposing);
    }
}
