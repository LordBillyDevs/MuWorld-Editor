using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MuWorldEditor.App;

/// <summary>
/// Paleta y tipografia del editor. Todo el programa saca los colores de aca:
/// tres niveles de fondo (ventana, panel, campo), un acento dorado estilo MU y
/// el texto en dos tonos. Cambiando estos valores cambia toda la interfaz.
/// </summary>
public static class DarkTheme
{
    // ---------------------------------------------------------- fondos
    /// <summary>Marco de la ventana, barras y vista vacia.</summary>
    public static readonly Color Back = Color.FromArgb(16, 18, 23);
    /// <summary>Paneles laterales.</summary>
    public static readonly Color Surface = Color.FromArgb(23, 26, 32);
    /// <summary>Campos, botones y tarjetas.</summary>
    public static readonly Color SurfaceAlt = Color.FromArgb(32, 36, 44);
    /// <summary>Lo que esta debajo del mouse.</summary>
    public static readonly Color Hover = Color.FromArgb(42, 47, 57);
    /// <summary>Fondo de listas y vistas previas, un poco mas hundido que el panel.</summary>
    public static readonly Color Sunken = Color.FromArgb(18, 20, 25);

    // ---------------------------------------------------------- bordes
    public static readonly Color Border = Color.FromArgb(47, 53, 64);
    public static readonly Color BorderSoft = Color.FromArgb(34, 38, 46);

    // ----------------------------------------------------------- texto
    public static readonly Color Text = Color.FromArgb(229, 231, 237);
    public static readonly Color TextDim = Color.FromArgb(139, 147, 162);
    public static readonly Color TextFaint = Color.FromArgb(94, 102, 117);

    // ---------------------------------------------------------- acento
    /// <summary>Dorado: el color de marca del editor.</summary>
    public static readonly Color Accent = Color.FromArgb(230, 174, 80);
    public static readonly Color AccentHover = Color.FromArgb(244, 196, 112);
    /// <summary>Fondo de lo elegido: el dorado apagado.</summary>
    public static readonly Color AccentDark = Color.FromArgb(78, 60, 31);
    /// <summary>Texto que va encima de un relleno dorado.</summary>
    public static readonly Color AccentText = Color.FromArgb(28, 22, 12);

    // -------------------------------------------------------- estados
    public static readonly Color Success = Color.FromArgb(92, 196, 124);
    public static readonly Color Warning = Color.FromArgb(240, 182, 72);
    public static readonly Color Danger = Color.FromArgb(232, 92, 92);
    public static readonly Color Info = Color.FromArgb(96, 164, 234);

    // ------------------------------------------------------ tipografia
    private static string _family;
    public static string FontFamilyName => _family ??= PickFamily("Segoe UI Variable Text", "Segoe UI", "Tahoma");

    public static readonly Font UiFont = new(FontFamilyName, 9.25f);
    public static readonly Font UiFontBold = new(FontFamilyName, 9.25f, FontStyle.Bold);
    public static readonly Font SmallFont = new(FontFamilyName, 8.25f);
    public static readonly Font SectionFont = new(FontFamilyName, 7.75f, FontStyle.Bold);
    public static readonly Font TitleFont = new(PickFamily("Segoe UI Variable Display Semib", "Segoe UI Semibold", "Segoe UI"), 10.5f);
    public static readonly Font HeroFont = new(PickFamily("Segoe UI Variable Display", "Segoe UI Light", "Segoe UI"), 26f, FontStyle.Regular);
    public static readonly Font MonoFont = new(PickFamily("Cascadia Mono", "Consolas", "Courier New"), 9f);

    private static string PickFamily(params string[] names)
    {
        try
        {
            var installed = new HashSet<string>(FontFamily.Families.Select(f => f.Name), StringComparer.OrdinalIgnoreCase);
            foreach (var n in names) if (installed.Contains(n)) return n;
        }
        catch { }
        return SystemFonts.DefaultFont.FontFamily.Name;
    }

    /// <summary>El color ya es uno de la paleta (se dejo a proposito).</summary>
    public static bool IsPalette(Color c)
    {
        int argb = c.ToArgb();
        return argb == Back.ToArgb() || argb == Surface.ToArgb() || argb == SurfaceAlt.ToArgb() ||
               argb == Sunken.ToArgb() || argb == Hover.ToArgb() || argb == Border.ToArgb();
    }

    /// <summary>Un color a medio camino entre dos.</summary>
    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    /// <summary>Rectangulo con esquinas redondeadas.</summary>
    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Max(0.5f, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        if (radius <= 0.1f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    // ------------------------------------------------------- ventana
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

    // modo oscuro de Windows para las barras de desplazamiento y menus nativos
    // (funciones sin nombre de uxtheme, existen desde Windows 10 1903)
    [DllImport("uxtheme.dll", EntryPoint = "#135")] private static extern int SetPreferredAppMode(int mode);
    [DllImport("uxtheme.dll", EntryPoint = "#133")] private static extern bool AllowDarkModeForWindow(IntPtr hwnd, bool allow);
    [DllImport("uxtheme.dll", EntryPoint = "#136")] private static extern void FlushMenuThemes();
    private static bool _darkApp;

    /// <summary>Pide a Windows que pinte oscuro lo nativo de toda la aplicacion. Se llama al arrancar.</summary>
    public static void EnableDarkApp()
    {
        if (_darkApp) return;
        _darkApp = true;
        try
        {
            if (Environment.OSVersion.Version.Build >= 18362)
            {
                SetPreferredAppMode(2);   // ForceDark
                FlushMenuThemes();
            }
        }
        catch { }
    }

    /// <summary>Pinta de oscuro tambien la barra de titulo (Windows 10 1809 en adelante).</summary>
    public static void UseDarkTitleBar(IntPtr handle)
    {
        try
        {
            int on = 1;
            if (DwmSetWindowAttribute(handle, 20, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref on, sizeof(int));   // builds viejas

            // Windows 11: esquinas redondeadas y borde del color del tema
            int round = 2;
            DwmSetWindowAttribute(handle, 33, ref round, sizeof(int));
            int border = Border.R | (Border.G << 8) | (Border.B << 16);
            DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
        }
        catch { }
    }

    /// <summary>Barras de desplazamiento oscuras en listas y paneles (Windows 10/11).</summary>
    public static void DarkScrollbars(Control c)
    {
        if (!c.IsHandleCreated) { c.HandleCreated += (_, _) => DarkScrollbars(c); return; }
        EnableDarkApp();
        try { if (Environment.OSVersion.Version.Build >= 18362) AllowDarkModeForWindow(c.Handle, true); } catch { }
        try { SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { }
    }

    /// <summary>
    /// Tine los menus desplegables. Sin esto usan los colores de Windows y el
    /// texto queda gris sobre gris, ilegible.
    /// </summary>
    public static void StyleMenu(ToolStripDropDownItem menu)
    {
        menu.DropDown.BackColor = Surface;
        menu.DropDown.ForeColor = Text;
        menu.DropDown.Renderer = new DarkRenderer();
        menu.DropDown.Opacity = 1.0;
        menu.DropDown.Font = UiFont;
        menu.DropDown.Padding = new Padding(2, 4, 2, 4);

        foreach (ToolStripItem it in menu.DropDownItems)
        {
            it.BackColor = Surface;
            it.ForeColor = Text;
            if (it is ToolStripMenuItem mi) mi.Padding = new Padding(4, 3, 4, 3);
        }
    }

    public static void Apply(Control root)
    {
        foreach (Control c in root.Controls) ApplyOne(c);
    }

    private static void ApplyOne(Control c)
    {
        switch (c)
        {
            case ModernButton or ModernCheckBox or ModernRadio or NumberBox or DarkSlider or SectionHeader:
                // se pintan solos
                break;

            case Button b:
                b.FlatStyle = FlatStyle.Flat;
                b.BackColor = SurfaceAlt;
                b.ForeColor = Text;
                b.FlatAppearance.BorderColor = Border;
                b.FlatAppearance.MouseOverBackColor = Hover;
                b.FlatAppearance.MouseDownBackColor = AccentDark;
                break;

            case RadioButton r:
                r.ForeColor = Text;
                r.BackColor = r.Appearance == Appearance.Button ? SurfaceAlt : Color.Transparent;
                if (r.Appearance == Appearance.Button)
                {
                    r.FlatStyle = FlatStyle.Flat;
                    r.FlatAppearance.BorderColor = Border;
                    r.FlatAppearance.CheckedBackColor = AccentDark;
                    r.FlatAppearance.MouseOverBackColor = Hover;
                }
                break;

            case CheckBox ck:
                ck.ForeColor = Text;
                ck.BackColor = Color.Transparent;
                break;

            case CheckedListBox clb:
                clb.BackColor = Sunken;
                clb.ForeColor = Text;
                clb.BorderStyle = BorderStyle.None;
                DarkScrollbars(clb);
                break;

            case ListBox lb:
                lb.BackColor = Sunken;
                lb.ForeColor = Text;
                lb.BorderStyle = BorderStyle.None;
                DarkScrollbars(lb);
                break;

            case ComboBox cb:
                StyleCombo(cb);
                break;

            case NumericUpDown n:
                n.BackColor = SurfaceAlt;
                n.ForeColor = Text;
                n.BorderStyle = BorderStyle.FixedSingle;
                break;

            case TextBox tb:
                tb.BackColor = SurfaceAlt;
                tb.ForeColor = Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
                break;

            case Label l:
                if (l.ForeColor == SystemColors.ControlText || l.ForeColor == Control.DefaultForeColor)
                    l.ForeColor = Text;
                l.BackColor = Color.Transparent;
                break;

            case DarkTabControl:
                break;

            case TabControl tc:
                StyleTabControl(tc);
                break;

            case System.Windows.Forms.Panel or SplitContainer or TabPage:
                if (!IsPalette(c.BackColor)) c.BackColor = Surface;
                c.ForeColor = Text;
                if (c is ScrollableControl { AutoScroll: true }) DarkScrollbars(c);
                break;
        }

        foreach (Control child in c.Controls) ApplyOne(child);
    }

    /// <summary>Combos con la lista desplegable pintada a mano, oscura.</summary>
    public static void StyleCombo(ComboBox cb)
    {
        cb.BackColor = SurfaceAlt;
        cb.ForeColor = Text;
        cb.FlatStyle = FlatStyle.Flat;
        if (cb.DrawMode != DrawMode.Normal) return;

        cb.DrawMode = DrawMode.OwnerDrawFixed;
        cb.ItemHeight = Math.Max(cb.ItemHeight, 20);
        cb.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
            using var bg = new SolidBrush(sel && !edit ? AccentDark : SurfaceAlt);
            e.Graphics.FillRectangle(bg, e.Bounds);
            var r = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, cb.GetItemText(cb.Items[e.Index]), cb.Font, r,
                                  sel && !edit ? AccentHover : Text,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        DarkScrollbars(cb);
    }

    private static void StyleTabControl(TabControl tc)
    {
        if (tc.Tag as string == "styled") return;
        tc.Tag = "styled";
        tc.DrawMode = TabDrawMode.OwnerDrawFixed;
        tc.SizeMode = TabSizeMode.Fixed;
        tc.ItemSize = new Size(78, 34);
        tc.BackColor = Surface;
        tc.Padding = new Point(0, 0);

        tc.DrawItem += (_, e) =>
        {
            var page = tc.TabPages[e.Index];
            bool active = tc.SelectedIndex == e.Index;
            var g = e.Graphics;
            using (var bg = new SolidBrush(active ? Surface : Back)) g.FillRectangle(bg, e.Bounds);

            if (active)
                using (var accent = new SolidBrush(Accent))
                    g.FillRectangle(accent, e.Bounds.Left + 10, e.Bounds.Bottom - 3, e.Bounds.Width - 20, 3);

            var icon = page.Tag as string;
            var textRect = e.Bounds;
            if (icon != null)
            {
                var bmp = Icons.Get(icon, 16, active ? Accent : TextDim);
                int total = 16 + 6 + TextRenderer.MeasureText(page.Text, UiFont).Width;
                int x = e.Bounds.X + Math.Max(4, (e.Bounds.Width - total) / 2);
                g.DrawImage(bmp, x, e.Bounds.Y + (e.Bounds.Height - 16) / 2 - 1);
                textRect = new Rectangle(x + 22, e.Bounds.Y, e.Bounds.Right - x - 22, e.Bounds.Height - 2);
                TextRenderer.DrawText(g, page.Text, active ? UiFontBold : UiFont, textRect,
                                      active ? Text : TextDim,
                                      TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            else
            {
                TextRenderer.DrawText(g, page.Text, active ? UiFontBold : UiFont, textRect,
                                      active ? Text : TextDim,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                                      TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        };

        tc.Paint += (_, e) =>
        {
            using var b = new SolidBrush(Back);
            e.Graphics.FillRectangle(b, new Rectangle(0, 0, tc.Width, tc.ItemSize.Height + 4));
        };

        // reparte el ancho entre las pestanas, para que llenen la barra
        void Fit()
        {
            if (tc.TabCount == 0) return;
            int w = Math.Max(56, (tc.Width - 6) / tc.TabCount);
            if (tc.ItemSize.Width != w) tc.ItemSize = new Size(w, tc.ItemSize.Height);
        }
        tc.Resize += (_, _) => Fit();
        tc.ControlAdded += (_, _) => Fit();
        Fit();
    }
}

/// <summary>Colores del ToolStrip y de los menus.</summary>
public sealed class DarkColorTable : ProfessionalColorTable
{
    public override Color ToolStripGradientBegin => DarkTheme.Back;
    public override Color ToolStripGradientMiddle => DarkTheme.Back;
    public override Color ToolStripGradientEnd => DarkTheme.Back;
    public override Color ToolStripContentPanelGradientBegin => DarkTheme.Back;
    public override Color ToolStripContentPanelGradientEnd => DarkTheme.Back;
    public override Color ToolStripBorder => DarkTheme.Border;
    public override Color ToolStripDropDownBackground => DarkTheme.Surface;
    public override Color ButtonSelectedGradientBegin => DarkTheme.Hover;
    public override Color ButtonSelectedGradientMiddle => DarkTheme.Hover;
    public override Color ButtonSelectedGradientEnd => DarkTheme.Hover;
    public override Color ButtonSelectedBorder => DarkTheme.Hover;
    public override Color ButtonPressedGradientBegin => DarkTheme.AccentDark;
    public override Color ButtonPressedGradientMiddle => DarkTheme.AccentDark;
    public override Color ButtonPressedGradientEnd => DarkTheme.AccentDark;
    public override Color ButtonCheckedGradientBegin => DarkTheme.AccentDark;
    public override Color ButtonCheckedGradientMiddle => DarkTheme.AccentDark;
    public override Color ButtonCheckedGradientEnd => DarkTheme.AccentDark;
    public override Color SeparatorDark => DarkTheme.Border;
    public override Color SeparatorLight => DarkTheme.Border;
    public override Color StatusStripGradientBegin => DarkTheme.Back;
    public override Color StatusStripGradientEnd => DarkTheme.Back;
    public override Color MenuBorder => DarkTheme.Border;
    public override Color MenuItemBorder => DarkTheme.AccentDark;
    public override Color MenuItemSelected => DarkTheme.AccentDark;
    public override Color MenuItemSelectedGradientBegin => DarkTheme.AccentDark;
    public override Color MenuItemSelectedGradientEnd => DarkTheme.AccentDark;
    public override Color MenuItemPressedGradientBegin => DarkTheme.AccentDark;
    public override Color MenuItemPressedGradientEnd => DarkTheme.AccentDark;
    public override Color ImageMarginGradientBegin => DarkTheme.Surface;
    public override Color ImageMarginGradientMiddle => DarkTheme.Surface;
    public override Color ImageMarginGradientEnd => DarkTheme.Surface;
    public override Color CheckBackground => DarkTheme.AccentDark;
    public override Color CheckSelectedBackground => DarkTheme.AccentDark;
    public override Color CheckPressedBackground => DarkTheme.AccentDark;
}

/// <summary>
/// Dibujo de la barra de herramientas: fondo redondeado al pasar el mouse,
/// relleno dorado apagado y una raya dorada abajo en la herramienta activa.
/// </summary>
public sealed class DarkRenderer : ToolStripProfessionalRenderer
{
    public DarkRenderer() : base(new DarkColorTable()) { RoundedEdges = false; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool on = e.Item is ToolStripButton { Checked: true };
        e.TextColor = !e.Item.Enabled ? DarkTheme.TextFaint : on ? DarkTheme.AccentHover : DarkTheme.Text;
        e.TextFont = DarkTheme.UiFont;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        if (e.Item.Owner is ToolStripDropDown) { base.OnRenderButtonBackground(e); return; }
        DrawItemBack(e.Graphics, e.Item, e.Item is ToolStripButton { Checked: true });
    }

    protected override void OnRenderDropDownButtonBackground(ToolStripItemRenderEventArgs e)
        => DrawItemBack(e.Graphics, e.Item, false);

    private static void DrawItemBack(Graphics g, ToolStripItem item, bool isChecked)
    {
        var r = new RectangleF(1, 2, item.Width - 2, item.Height - 4);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color? fill = null;
        if (item.Pressed) fill = DarkTheme.AccentDark;
        else if (isChecked) fill = DarkTheme.Mix(DarkTheme.AccentDark, DarkTheme.Back, 0.25f);
        else if (item.Selected && item.Enabled) fill = DarkTheme.Hover;

        if (fill != null)
        {
            using var path = DarkTheme.Round(r, 5);
            using var b = new SolidBrush(fill.Value);
            g.FillPath(b, path);
        }
        if (isChecked)
        {
            using var acc = new SolidBrush(DarkTheme.Accent);
            g.FillRectangle(acc, r.X + 6, r.Bottom - 2, r.Width - 12, 2);
        }
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        if (e.Item.Owner is ToolStripDropDown)
        {
            using var p0 = new Pen(DarkTheme.Border);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(p0, 8, y, e.Item.Width - 8, y);
            return;
        }
        using var p = new Pen(DarkTheme.Border);
        int x = e.Item.Width / 2;
        e.Graphics.DrawLine(p, x, 7, x, e.Item.Height - 7);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (e.ToolStrip is ToolStripDropDown)
        {
            using var p0 = new Pen(DarkTheme.Border);
            e.Graphics.DrawRectangle(p0, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
            return;
        }
        using var p = new Pen(DarkTheme.BorderSoft);
        e.Graphics.DrawLine(p, 0, e.AffectedBounds.Bottom - 1, e.ToolStrip.Width, e.AffectedBounds.Bottom - 1);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = DarkTheme.TextDim;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(e.ImageRectangle.X + 2, e.ImageRectangle.Y + 2, 12, 12);
        using var path = DarkTheme.Round(r, 3);
        using var b = new SolidBrush(DarkTheme.Accent);
        g.FillPath(b, path);
        using var pen = new Pen(DarkTheme.AccentText, 1.8f);
        g.DrawLines(pen, new[] { new PointF(r.X + 3, r.Y + 6.5f), new PointF(r.X + 5.2f, r.Y + 9), new PointF(r.X + 9.5f, r.Y + 3.5f) });
    }
}

/// <summary>
/// Barra deslizante dibujada a mano: el TrackBar de Windows no admite
/// colores propios y no se puede integrar al tema oscuro.
/// </summary>
public sealed class DarkSlider : Control
{
    private int _min = 0, _max = 100, _value = 50;
    private bool _drag, _hover;

    public event Action<int> ValueChanged;

    [DefaultValue(0)]
    public int Minimum { get => _min; set { _min = value; Clamp(); Invalidate(); } }
    [DefaultValue(100)]
    public int Maximum { get => _max; set { _max = value; Clamp(); Invalidate(); } }

    [DefaultValue(50)]
    public int Value
    {
        get => _value;
        set
        {
            int v = _max < _min ? value : Math.Clamp(value, _min, _max);
            if (v == _value) return;
            _value = v;
            Invalidate();
            ValueChanged?.Invoke(_value);
        }
    }

    public DarkSlider()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Height = 22;
        BackColor = DarkTheme.Surface;
        Cursor = Cursors.Hand;
    }

    private void Clamp()
    {
        // durante la construccion pueden llegar min y max en cualquier orden
        if (_max < _min) return;
        _value = Math.Clamp(_value, _min, _max);
    }

    private float Ratio => _max <= _min ? 0 : (_value - _min) / (float)(_max - _min);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? BackColor);

        float cy = Height / 2f;
        var track = new RectangleF(7, cy - 2, Width - 14, 4);
        using (var path = DarkTheme.Round(track, 2))
        using (var b = new SolidBrush(DarkTheme.Sunken))
            g.FillPath(b, path);

        float fill = track.Width * Ratio;
        if (fill > 0.5f)
            using (var path = DarkTheme.Round(new RectangleF(track.X, track.Y, fill, track.Height), 2))
            using (var b = new SolidBrush(Enabled ? DarkTheme.Accent : DarkTheme.TextFaint))
                g.FillPath(b, path);

        float tx = track.X + fill;
        float rad = _drag ? 7.5f : _hover ? 7f : 6.5f;
        if (_hover || _drag)
            using (var halo = new SolidBrush(Color.FromArgb(46, DarkTheme.Accent)))
                g.FillEllipse(halo, tx - rad - 4, cy - rad - 4, (rad + 4) * 2, (rad + 4) * 2);
        using (var b = new SolidBrush(_drag ? DarkTheme.AccentHover : Color.FromArgb(236, 238, 243)))
            g.FillEllipse(b, tx - rad, cy - rad, rad * 2, rad * 2);
        using (var p = new Pen(Color.FromArgb(90, 0, 0, 0)))
            g.DrawEllipse(p, tx - rad, cy - rad, rad * 2, rad * 2);
    }

    private void SetFromMouse(int x)
    {
        float r = Math.Clamp((x - 7) / (float)Math.Max(1, Width - 14), 0f, 1f);
        Value = (int)Math.Round(_min + r * (_max - _min));
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        _drag = true;
        SetFromMouse(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag) SetFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _drag = false;
        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        int step = Math.Max(1, (_max - _min) / 100);
        Value += e.Delta > 0 ? step : -step;
    }
}
