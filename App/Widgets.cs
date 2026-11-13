using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace MuWorldEditor.App;

/// <summary>Boton redondeado con icono opcional, estados de hover y presionado.</summary>
public class ModernButton : Button
{
    private bool _hover, _down;

    /// <summary>Nombre del icono de <see cref="Icons"/> (o null).</summary>
    [DefaultValue(null)] public string IconName { get; set; }
    /// <summary>Boton principal: relleno dorado.</summary>
    [DefaultValue(false)] public bool Primary { get; set; }
    /// <summary>Boton peligroso (borrar): se tine de rojo al pasar el mouse.</summary>
    [DefaultValue(false)] public bool Danger { get; set; }
    /// <summary>Texto alineado a la izquierda, para botones anchos con icono.</summary>
    [DefaultValue(false)] public bool AlignLeft { get; set; }

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = Color.Transparent;
        ForeColor = DarkTheme.Text;
        Font = DarkTheme.UiFont;
        Cursor = Cursors.Hand;
        Height = 32;
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; _down = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); _down = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? DarkTheme.Surface);

        Color fill, border, text;
        if (!Enabled)
        {
            fill = DarkTheme.Mix(DarkTheme.SurfaceAlt, DarkTheme.Surface, 0.5f);
            border = DarkTheme.BorderSoft;
            text = DarkTheme.TextFaint;
        }
        else if (Primary)
        {
            fill = _down ? DarkTheme.Mix(DarkTheme.Accent, Color.Black, 0.15f) : _hover ? DarkTheme.AccentHover : DarkTheme.Accent;
            border = fill;
            text = DarkTheme.AccentText;
        }
        else
        {
            fill = _down ? DarkTheme.AccentDark : _hover ? (Danger ? Color.FromArgb(78, 36, 38) : DarkTheme.Hover) : DarkTheme.SurfaceAlt;
            border = _hover ? (Danger ? DarkTheme.Danger : DarkTheme.Border) : DarkTheme.BorderSoft;
            text = Danger && _hover ? Color.FromArgb(255, 200, 200) : DarkTheme.Text;
        }

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = DarkTheme.Round(r, 5))
        {
            using (var b = new SolidBrush(fill)) g.FillPath(b, path);
            using (var pen = new Pen(border)) g.DrawPath(pen, path);
        }
        if (Focused && ShowFocusCues && Enabled && !Primary)
            using (var path = DarkTheme.Round(new RectangleF(2, 2, Width - 5, Height - 5), 4))
            using (var pen = new Pen(Color.FromArgb(120, DarkTheme.Accent)))
                g.DrawPath(pen, path);

        int iconSize = Height >= 30 ? 16 : 14;
        var icon = IconName != null ? Icons.Get(IconName, iconSize, Primary && Enabled ? DarkTheme.AccentText : (Enabled ? (_hover ? DarkTheme.AccentHover : DarkTheme.TextDim) : DarkTheme.TextFaint)) : null;
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        string t = Text ?? "";

        if (icon != null && t.Length == 0)
        {
            g.DrawImage(icon, (Width - iconSize) / 2, (Height - iconSize) / 2);
            return;
        }

        int textW = TextRenderer.MeasureText(t, Font).Width;
        int contentW = textW + (icon != null ? iconSize + 6 : 0);
        int x = AlignLeft ? 10 : Math.Max(6, (Width - contentW) / 2);
        if (icon != null)
        {
            g.DrawImage(icon, x, (Height - iconSize) / 2);
            x += iconSize + 6;
        }
        TextRenderer.DrawText(g, t, Font, new Rectangle(x, 0, Width - x - 4, Height), text, flags);
    }
}

/// <summary>Casilla de verificacion moderna: cuadradito redondeado con tilde dorado.</summary>
public class ModernCheckBox : CheckBox
{
    private bool _hover;

    public ModernCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = DarkTheme.Text;
        Font = DarkTheme.UiFont;
        Cursor = Cursors.Hand;
        Padding = new Padding(0, 1, 0, 1);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var t = TextRenderer.MeasureText(Text ?? "", Font);
        return new Size(t.Width + 26, Math.Max(22, t.Height + 6));
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? DarkTheme.Surface);

        float y = (Height - 16) / 2f;
        var box = new RectangleF(1, y, 16, 16);
        using (var path = DarkTheme.Round(box, 4))
        {
            if (Checked)
            {
                using var b = new SolidBrush(Enabled ? (_hover ? DarkTheme.AccentHover : DarkTheme.Accent) : DarkTheme.TextFaint);
                g.FillPath(b, path);
            }
            else
            {
                using var b = new SolidBrush(DarkTheme.Sunken);
                g.FillPath(b, path);
                using var pen = new Pen(_hover && Enabled ? DarkTheme.Accent : DarkTheme.Border, 1.2f);
                g.DrawPath(pen, path);
            }
        }
        if (Checked)
        {
            using var pen = new Pen(DarkTheme.AccentText, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLines(pen, new[] { new PointF(5, y + 8.3f), new PointF(7.8f, y + 11.2f), new PointF(13, y + 5) });
        }

        TextRenderer.DrawText(g, Text, Font, new Rectangle(24, 0, Width - 24, Height),
                              Enabled ? DarkTheme.Text : DarkTheme.TextFaint,
                              TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Opcion de un grupo. Con Appearance=Button se dibuja como boton segmentado
/// (el elegido con fondo dorado apagado y raya dorada); si no, como circulo.
/// </summary>
public class ModernRadio : RadioButton
{
    private bool _hover;

    [DefaultValue(null)] public string IconName { get; set; }

    public ModernRadio()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = DarkTheme.Text;
        Font = DarkTheme.UiFont;
        Cursor = Cursors.Hand;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var t = TextRenderer.MeasureText(Text ?? "", Font);
        if (Appearance == Appearance.Button)
            return new Size(t.Width + 24 + (IconName != null ? 20 : 0), 30);
        return new Size(t.Width + 26, Math.Max(22, t.Height + 6));
    }

    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? DarkTheme.Surface);

        if (Appearance == Appearance.Button)
        {
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Color fill = Checked ? DarkTheme.AccentDark : _hover ? DarkTheme.Hover : DarkTheme.SurfaceAlt;
            using (var path = DarkTheme.Round(r, 5))
            {
                using (var b = new SolidBrush(fill)) g.FillPath(b, path);
                using (var pen = new Pen(Checked ? DarkTheme.Mix(DarkTheme.Accent, DarkTheme.AccentDark, 0.45f) : DarkTheme.BorderSoft))
                    g.DrawPath(pen, path);
            }
            var textColor = !Enabled ? DarkTheme.TextFaint : Checked ? DarkTheme.AccentHover : DarkTheme.Text;
            string t = Text ?? "";
            int tw = TextRenderer.MeasureText(t, Font).Width;
            int x = (Width - tw - (IconName != null ? 20 : 0)) / 2;
            if (IconName != null)
            {
                g.DrawImage(Icons.Get(IconName, 16, Checked ? DarkTheme.Accent : DarkTheme.TextDim), x, (Height - 16) / 2);
                x += 20;
            }
            TextRenderer.DrawText(g, t, Checked ? DarkTheme.UiFontBold : Font, new Rectangle(x, 0, Width - x, Height), textColor,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            return;
        }

        float y = (Height - 16) / 2f;
        using (var pen = new Pen(Checked || _hover ? DarkTheme.Accent : DarkTheme.Border, 1.4f))
        using (var b = new SolidBrush(DarkTheme.Sunken))
        {
            g.FillEllipse(b, 1, y, 16, 16);
            g.DrawEllipse(pen, 1, y, 16, 16);
        }
        if (Checked)
            using (var b = new SolidBrush(DarkTheme.Accent)) g.FillEllipse(b, 5, y + 4, 8, 8);
        TextRenderer.DrawText(g, Text, Font, new Rectangle(24, 0, Width - 24, Height),
                              Enabled ? DarkTheme.Text : DarkTheme.TextFaint,
                              TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>
/// Campo numerico propio: caja redondeada, flechitas finas, rueda del mouse y
/// teclas arriba/abajo. Reemplaza al NumericUpDown de Windows, que no se
/// puede oscurecer del todo.
/// </summary>
public class NumberBox : Control
{
    private readonly TextBox _box = new();
    private decimal _min = 0, _max = 100, _value, _inc = 1;
    private int _decimals;
    private bool _hoverUp, _hoverDown, _focused;

    public event EventHandler ValueChanged;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public decimal Minimum { get => _min; set { _min = value; if (_max < _min) _max = _min; Value = _value; } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public decimal Maximum { get => _max; set { _max = value; if (_min > _max) _min = _max; Value = _value; } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public decimal Increment { get => _inc; set => _inc = value; }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public int DecimalPlaces { get => _decimals; set { _decimals = Math.Max(0, value); ShowValue(); } }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public decimal Value
    {
        get => _value;
        set
        {
            var v = Math.Round(Math.Clamp(value, _min, _max), _decimals);
            if (v == _value) { ShowValue(); return; }
            _value = v;
            ShowValue();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public NumberBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        BackColor = DarkTheme.SurfaceAlt;
        Font = DarkTheme.UiFont;
        Height = 26;
        Width = 90;

        _box.BorderStyle = BorderStyle.None;
        _box.BackColor = DarkTheme.SurfaceAlt;
        _box.ForeColor = DarkTheme.Text;
        _box.Font = Font;
        _box.TextAlign = HorizontalAlignment.Left;
        _box.GotFocus += (_, _) => { _focused = true; Invalidate(); _box.SelectAll(); };
        _box.LostFocus += (_, _) => { _focused = false; Commit(); Invalidate(); };
        _box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { Commit(); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Up) { Step(1); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Down) { Step(-1); e.SuppressKeyPress = true; }
            else if (e.KeyCode == Keys.Escape) { ShowValue(); e.SuppressKeyPress = true; }
        };
        _box.MouseWheel += (_, e) => Step(e.Delta > 0 ? 1 : -1);
        Controls.Add(_box);
        ShowValue();
    }

    private void Step(int dir)
    {
        if (!Enabled) return;
        decimal k = ModifierKeys.HasFlag(Keys.Shift) ? 10 : 1;
        Value = _value + _inc * k * dir;
    }

    private void Commit()
    {
        var txt = _box.Text.Trim().Replace(',', '.');
        if (decimal.TryParse(txt, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            Value = v;
        else ShowValue();
    }

    private void ShowValue()
    {
        if (_box == null) return;
        string s = _value.ToString("F" + _decimals, System.Globalization.CultureInfo.CurrentCulture);
        if (_box.Text != s) _box.Text = s;
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        int h = _box.PreferredHeight;
        _box.SetBounds(8, Math.Max(1, (Height - h) / 2), Math.Max(10, Width - 8 - 20), h);
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        _box.Enabled = Enabled;
        _box.ForeColor = Enabled ? DarkTheme.Text : DarkTheme.TextFaint;
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); _box.Font = Font; PerformLayout(); }

    private Rectangle UpRect => new(Width - 18, 2, 16, Height / 2 - 2);
    private Rectangle DownRect => new(Width - 18, Height / 2, 16, Height / 2 - 2);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool u = UpRect.Contains(e.Location), d = DownRect.Contains(e.Location);
        if (u != _hoverUp || d != _hoverDown) { _hoverUp = u; _hoverDown = d; Invalidate(); }
        Cursor = u || d ? Cursors.Hand : Cursors.IBeam;
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hoverUp = _hoverDown = false; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (UpRect.Contains(e.Location)) Step(1);
        else if (DownRect.Contains(e.Location)) Step(-1);
        else _box.Focus();
    }

    protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Step(e.Delta > 0 ? 1 : -1); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? DarkTheme.Surface);

        var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (var path = DarkTheme.Round(r, 5))
        {
            using (var b = new SolidBrush(DarkTheme.SurfaceAlt)) g.FillPath(b, path);
            using (var pen = new Pen(_focused ? DarkTheme.Accent : DarkTheme.BorderSoft)) g.DrawPath(pen, path);
        }

        void Arrow(Rectangle rc, bool up, bool hot)
        {
            float cx = rc.X + rc.Width / 2f, cy = rc.Y + rc.Height / 2f + (up ? 1 : -1);
            using var pen = new Pen(!Enabled ? DarkTheme.TextFaint : hot ? DarkTheme.Accent : DarkTheme.TextDim, 1.5f)
            { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (up) g.DrawLines(pen, new[] { new PointF(cx - 3.5f, cy + 1.5f), new PointF(cx, cy - 2), new PointF(cx + 3.5f, cy + 1.5f) });
            else g.DrawLines(pen, new[] { new PointF(cx - 3.5f, cy - 1.5f), new PointF(cx, cy + 2), new PointF(cx + 3.5f, cy - 1.5f) });
        }
        Arrow(UpRect, true, _hoverUp);
        Arrow(DownRect, false, _hoverDown);
    }
}

/// <summary>
/// Cabecera de una seccion plegable del panel: flecha, titulo en versalitas
/// y una linea fina. Un clic pliega o despliega lo que sigue.
/// </summary>
public sealed class SectionHeader : Control
{
    private bool _hover;
    public bool Collapsed { get; private set; }
    public event Action<bool> Toggled;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string Key { get; init; }

    public SectionHeader(string title, bool collapsed)
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        Text = title;
        Collapsed = collapsed;
        Height = 30;
        Cursor = Cursors.Hand;
        Margin = new Padding(0, 8, 0, 2);
    }

    public void SetCollapsed(bool v)
    {
        if (Collapsed == v) return;
        Collapsed = v;
        Invalidate();
        Toggled?.Invoke(v);
    }

    protected override void OnClick(EventArgs e) { base.OnClick(e); SetCollapsed(!Collapsed); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = false; Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Parent?.BackColor ?? DarkTheme.Surface);

        if (_hover)
            using (var path = DarkTheme.Round(new RectangleF(0, 1, Width - 1, Height - 3), 4))
            using (var b = new SolidBrush(DarkTheme.Mix(DarkTheme.Surface, DarkTheme.Hover, 0.6f)))
                g.FillPath(b, path);

        var icon = Icons.Get(Collapsed ? "chevron-right" : "chevron-down", 14, _hover ? DarkTheme.Accent : DarkTheme.TextDim);
        g.DrawImage(icon, 4, (Height - 14) / 2);

        string title = (Text ?? "").ToUpperInvariant();
        var size = TextRenderer.MeasureText(title, DarkTheme.SectionFont);
        TextRenderer.DrawText(g, title, DarkTheme.SectionFont, new Point(22, (Height - size.Height) / 2),
                              _hover ? DarkTheme.Text : DarkTheme.Accent, TextFormatFlags.NoPrefix);

        int lx = 22 + size.Width + 8;
        if (lx < Width - 6)
            using (var pen = new Pen(DarkTheme.BorderSoft))
                g.DrawLine(pen, lx, Height / 2, Width - 6, Height / 2);
    }
}

/// <summary>
/// Convierte un panel armado con etiquetas de titulo en secciones plegables:
/// cada etiqueta marcada como cabecera se reemplaza por una
/// <see cref="SectionHeader"/> y todo lo que viene despues, hasta la proxima,
/// queda bajo ella. El estado plegado se recuerda entre sesiones.
/// </summary>
public static class Sections
{
    public const string HeaderTag = "section-header";

    /// <summary>Etiqueta de titulo de seccion (se convierte en cabecera plegable).</summary>
    public static Label Header(string text) => new()
    {
        Text = text,
        Tag = HeaderTag,
        Font = DarkTheme.UiFontBold,
        AutoSize = true,
        Margin = new Padding(0, 12, 0, 4)
    };

    public static void Build(FlowLayoutPanel p, string keyPrefix, int width = 270)
    {
        var all = p.Controls.Cast<Control>().ToList();
        if (!all.Any(c => c is Label { Tag: HeaderTag })) return;

        p.SuspendLayout();
        var current = new List<Control>();
        SectionHeader header = null;

        void Flush()
        {
            if (header == null) return;
            var h = header;
            var members = current.ToList();
            foreach (var c in members) c.Visible = !h.Collapsed;
            h.Toggled += collapsed =>
            {
                p.SuspendLayout();
                foreach (var c in members) c.Visible = !collapsed;
                p.ResumeLayout(true);
                UiState.SetCollapsed(h.Key, collapsed);
            };
        }

        foreach (var c in all)
        {
            if (c is Label { Tag: HeaderTag } lbl)
            {
                Flush();
                string key = keyPrefix + "/" + lbl.Text;
                header = new SectionHeader(lbl.Text, UiState.IsCollapsed(key)) { Key = key, Width = width };
                int i = p.Controls.GetChildIndex(lbl);
                p.Controls.Remove(lbl);
                p.Controls.Add(header);
                p.Controls.SetChildIndex(header, i);
                current.Clear();
            }
            else if (header != null) current.Add(c);
        }
        Flush();
        p.ResumeLayout(true);
    }
}

/// <summary>Estado de la interfaz que se recuerda: secciones plegadas, etc.</summary>
public static class UiState
{
    public static HashSet<string> Collapsed = new(StringComparer.OrdinalIgnoreCase);
    public static bool IsCollapsed(string key) => key != null && Collapsed.Contains(key);
    public static void SetCollapsed(string key, bool v)
    {
        if (key == null) return;
        if (v) Collapsed.Add(key); else Collapsed.Remove(key);
    }
}

/// <summary>
/// Pestanas pintadas a mano, sin los bordes blancos 3D del TabControl de
/// Windows: una barra tipo "segmentos" arriba y la pagina ocupa todo el resto.
/// Se usa igual que un TabControl (TabPages, SelectedIndex, eventos).
/// </summary>
public class DarkTabControl : TabControl
{
    private const int TCM_ADJUSTRECT = 0x1328, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONDBLCLK = 0x0203;
    private int _hot = -1;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>Alto de la barra de pestanas.</summary>
    public const int HeaderHeight = 46;

    public DarkTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        SizeMode = TabSizeMode.Fixed;
        ItemSize = new Size(70, 30);
        Font = DarkTheme.UiFont;
        BackColor = DarkTheme.Surface;
        Tag = "styled";
    }

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        if (e.Control is TabPage tp)
        {
            tp.UseVisualStyleBackColor = false;
            tp.BackColor = DarkTheme.Surface;
            tp.ForeColor = DarkTheme.Text;
            tp.Padding = new Padding(0);
            tp.BorderStyle = BorderStyle.None;
        }
        Invalidate();
    }

    private Rectangle Strip => new(10, 8, Math.Max(10, Width - 20), HeaderHeight - 14);

    private Rectangle TabBounds(int i)
    {
        var s = Strip;
        int n = Math.Max(1, TabCount);
        int w = (s.Width - 6) / n;
        int x = s.X + 3 + i * w;
        int last = i == n - 1 ? s.Right - 3 - x : w;
        return new Rectangle(x, s.Y + 3, last, s.Height - 6);
    }

    private int HitTab(Point p)
    {
        for (int i = 0; i < TabCount; i++) if (TabBounds(i).Contains(p)) return i;
        return -1;
    }

    protected override void WndProc(ref Message m)
    {
        if (!DesignMode)
        {
            if (m.Msg == TCM_ADJUSTRECT && m.LParam != IntPtr.Zero)
            {
                // la pagina ocupa todo menos la barra, sin marco
                var rc = System.Runtime.InteropServices.Marshal.PtrToStructure<RECT>(m.LParam);
                if (m.WParam == IntPtr.Zero) rc.Top += HeaderHeight;
                else rc.Top -= HeaderHeight;
                System.Runtime.InteropServices.Marshal.StructureToPtr(rc, m.LParam, false);
                m.Result = IntPtr.Zero;
                return;
            }
            if (m.Msg == WM_LBUTTONDOWN || m.Msg == WM_LBUTTONDBLCLK)
            {
                long lp = m.LParam.ToInt64();
                var p = new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
                int i = HitTab(p);
                if (i >= 0 && i != SelectedIndex) SelectedIndex = i;
                return;
            }
        }
        base.WndProc(ref m);
    }

    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitTab(e.Location);
        if (h != _hot) { _hot = h; Invalidate(new Rectangle(0, 0, Width, HeaderHeight)); }
        Cursor = h >= 0 && h != SelectedIndex ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hot != -1) { _hot = -1; Invalidate(new Rectangle(0, 0, Width, HeaderHeight)); }
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(DarkTheme.Surface)) g.FillRectangle(b, ClientRectangle);

        // carril de los segmentos
        var strip = Strip;
        using (var path = DarkTheme.Round(new RectangleF(strip.X + 0.5f, strip.Y + 0.5f, strip.Width - 1, strip.Height - 1), 9))
        {
            using (var b = new SolidBrush(DarkTheme.Sunken)) g.FillPath(b, path);
            using (var pen = new Pen(DarkTheme.BorderSoft)) g.DrawPath(pen, path);
        }

        for (int i = 0; i < TabCount; i++)
        {
            var page = TabPages[i];
            var r = TabBounds(i);
            bool active = i == SelectedIndex, hot = i == _hot && !active;

            if (active || hot)
            {
                using var path = DarkTheme.Round(new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1), 7);
                if (active)
                {
                    using (var lg = new LinearGradientBrush(r, DarkTheme.Mix(DarkTheme.SurfaceAlt, DarkTheme.Accent, 0.16f), DarkTheme.SurfaceAlt, 90f))
                        g.FillPath(lg, path);
                    using (var pen = new Pen(Color.FromArgb(120, DarkTheme.Accent))) g.DrawPath(pen, path);
                }
                else
                    using (var b = new SolidBrush(Color.FromArgb(150, DarkTheme.Hover))) g.FillPath(b, path);
            }

            var font = active ? DarkTheme.UiFontBold : DarkTheme.UiFont;
            var color = active ? DarkTheme.Text : hot ? DarkTheme.Text : DarkTheme.TextDim;
            string icon = page.Tag as string;
            int tw = TextRenderer.MeasureText(g, page.Text, font, Size.Empty, TextFormatFlags.NoPadding).Width;
            bool showIcon = icon != null && r.Width >= tw + 34;
            int total = tw + (showIcon ? 22 : 0);
            int x = r.X + Math.Max(2, (r.Width - total) / 2);
            if (showIcon)
            {
                g.DrawImage(Icons.Get(icon, 16, active ? DarkTheme.Accent : color), x, r.Y + (r.Height - 16) / 2);
                x += 22;
            }
            TextRenderer.DrawText(g, page.Text, font, new Rectangle(x, r.Y, r.Right - x, r.Height), color,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        }

        // linea fina que separa la barra del contenido
        using (var pen = new Pen(DarkTheme.BorderSoft))
            g.DrawLine(pen, 0, HeaderHeight - 1, Width, HeaderHeight - 1);
    }
}
