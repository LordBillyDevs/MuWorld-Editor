using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace MuWorldEditor.App;

/// <summary>
/// Barra de titulo propia para la ventana sin marco: icono, titulo, subtitulo
/// (mapa abierto) y los botones minimizar, maximizar y cerrar. Se arrastra y
/// con doble clic se maximiza, igual que la de Windows.
/// </summary>
public sealed class TitleBar : Control
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

    private const int ButtonW = 46;
    private int _hot = -1;
    private string _subtitle = "";
    private bool _dirty;

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public string Subtitle { get => _subtitle; set { _subtitle = value ?? ""; Invalidate(); } }
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)] public bool Dirty { get => _dirty; set { if (_dirty != value) { _dirty = value; Invalidate(); } } }

    public TitleBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Top;
        Height = 36;
        BackColor = DarkTheme.Back;
    }

    private Form Owner => FindForm();

    private Rectangle ButtonRect(int i) => new(Width - ButtonW * (3 - i), 0, ButtonW, Height);

    private int HitButton(Point p)
    {
        for (int i = 0; i < 3; i++) if (ButtonRect(i).Contains(p)) return i;
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int h = HitButton(e.Location);
        if (h != _hot) { _hot = h; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hot = -1; Invalidate(); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left || HitButton(e.Location) >= 0) return;
        var f = Owner;
        if (f == null) return;
        if (e.Clicks == 2) { ToggleMax(); return; }
        ReleaseCapture();
        SendMessage(f.Handle, 0xA1, (IntPtr)2, IntPtr.Zero);   // WM_NCLBUTTONDOWN, HTCAPTION
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var f = Owner;
        if (f == null || e.Button != MouseButtons.Left) return;
        switch (HitButton(e.Location))
        {
            case 0: f.WindowState = FormWindowState.Minimized; break;
            case 1: ToggleMax(); break;
            case 2: f.Close(); break;
        }
    }

    private void ToggleMax()
    {
        var f = Owner;
        if (f == null) return;
        f.WindowState = f.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(DarkTheme.Back);
        var f = Owner;

        // emblema
        g.DrawImage(Icons.Get("logo", 20, DarkTheme.Accent), 12, (Height - 20) / 2);

        string title = "MU World Editor";
        var tf = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        int x = 40;
        TextRenderer.DrawText(g, title, DarkTheme.TitleFont, new Rectangle(x, 0, 400, Height), DarkTheme.Text, tf);
        x += TextRenderer.MeasureText(title, DarkTheme.TitleFont).Width + 4;

        if (_subtitle.Length > 0)
        {
            using (var b = new SolidBrush(DarkTheme.Border)) g.FillEllipse(b, x + 2, Height / 2 - 2, 4, 4);
            x += 12;
            int max = Width - ButtonW * 3 - x - 20;
            TextRenderer.DrawText(g, _subtitle, DarkTheme.UiFont, new Rectangle(x, 0, Math.Max(0, max), Height), DarkTheme.TextDim,
                                  tf | TextFormatFlags.EndEllipsis);
            x += Math.Min(max, TextRenderer.MeasureText(_subtitle, DarkTheme.UiFont).Width) + 8;
        }
        if (_dirty)
        {
            using var b = new SolidBrush(DarkTheme.Accent);
            g.FillEllipse(b, x, Height / 2 - 3, 6, 6);
        }

        bool max2 = f?.WindowState == FormWindowState.Maximized;
        string[] icons = { "minimize", max2 ? "restore" : "maximize", "close" };
        for (int i = 0; i < 3; i++)
        {
            var r = ButtonRect(i);
            if (_hot == i)
                using (var b = new SolidBrush(i == 2 ? Color.FromArgb(196, 43, 28) : DarkTheme.Hover))
                    g.FillRectangle(b, r);
            var col = _hot == i ? Color.White : DarkTheme.TextDim;
            g.DrawImage(Icons.Get(icons[i], 16, col), r.X + (r.Width - 16) / 2, (Height - 16) / 2);
        }

        using var pen = new Pen(DarkTheme.BorderSoft);
        g.DrawLine(pen, 0, Height - 1, Width, Height - 1);
    }
}

/// <summary>
/// Barra de estado dividida en secciones: mapa y formato, lo que hay bajo el
/// cursor, el ultimo aviso, la seleccion, el rendimiento y la placa de video.
/// </summary>
public sealed class StatusBar : Control
{
    public string MapName = "", Format = "", Cursor3D = "", Message = "", Selection = "", Perf = "", Gpu = "";
    public bool Dirty;
    public Color MessageColor = DarkTheme.TextDim;

    public StatusBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.ResizeRedraw, true);
        Dock = DockStyle.Bottom;
        Height = 26;
        BackColor = DarkTheme.Back;
    }

    public void Apply(Action change) { change(); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(DarkTheme.Back);
        using (var top = new Pen(DarkTheme.BorderSoft)) g.DrawLine(top, 0, 0, Width, 0);

        var font = DarkTheme.SmallFont;
        var tf = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
        int x = 10;

        void Sep()
        {
            using var p = new Pen(DarkTheme.BorderSoft);
            g.DrawLine(p, x, 6, x, Height - 6);
            x += 10;
        }

        int Seg(string text, Color color, string icon = null, int maxW = 360)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int start = x;
            if (icon != null)
            {
                g.DrawImage(Icons.Get(icon, 14, DarkTheme.TextFaint), x, (Height - 14) / 2);
                x += 18;
            }
            int w = Math.Min(maxW, TextRenderer.MeasureText(text, font).Width + 2);
            TextRenderer.DrawText(g, text, font, new Rectangle(x, 0, w, Height), color, tf);
            x += w + 10;
            return x - start;
        }

        // estado del archivo
        using (var b = new SolidBrush(Dirty ? DarkTheme.Accent : DarkTheme.Success))
            g.FillEllipse(b, x, Height / 2 - 4, 8, 8);
        x += 14;
        Seg(string.IsNullOrEmpty(MapName) ? Lang.T("ui.sin_mapa", "Sin mapa") : MapName, DarkTheme.Text, null, 260);
        if (!string.IsNullOrEmpty(Format)) Seg(Format, DarkTheme.TextFaint, null, 220);
        if (!string.IsNullOrEmpty(Cursor3D)) { Sep(); Seg(Cursor3D, DarkTheme.TextDim, "focus", 520); }

        // derecha a izquierda
        int right = Width - 10;
        void RightSeg(string text, Color color, string icon = null)
        {
            if (string.IsNullOrEmpty(text)) return;
            int w = TextRenderer.MeasureText(text, font).Width + 2;
            int total = w + (icon != null ? 18 : 0);
            right -= total;
            int rx = right;
            if (icon != null) { g.DrawImage(Icons.Get(icon, 14, DarkTheme.TextFaint), rx, (Height - 14) / 2); rx += 18; }
            TextRenderer.DrawText(g, text, font, new Rectangle(rx, 0, w, Height), color, tf);
            right -= 12;
            using var p = new Pen(DarkTheme.BorderSoft);
            g.DrawLine(p, right + 5, 6, right + 5, Height - 6);
        }
        RightSeg(Gpu, DarkTheme.TextFaint);
        RightSeg(Perf, DarkTheme.TextDim);
        RightSeg(Selection, DarkTheme.TextDim, "cube");

        if (!string.IsNullOrEmpty(Message) && right - x > 60)
        {
            Sep();
            TextRenderer.DrawText(g, Message, font, new Rectangle(x, 0, right - x - 4, Height), MessageColor, tf);
        }
    }
}

public enum ToastKind { Info, Success, Warning, Error }

/// <summary>
/// Aviso flotante que aparece abajo a la derecha de la vista y se va solo.
/// No roba el foco ni corta el trabajo como un cartel de Windows.
/// </summary>
public sealed class Toast : Form
{
    private static readonly List<Toast> Open = new();
    private readonly string _text;
    private readonly ToastKind _kind;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private float _life;
    private readonly float _duration;
    private Control _anchor;

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= 0x08000000 | 0x00000080;     // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
            cp.ClassStyle |= 0x00020000;               // CS_DROPSHADOW
            return cp;
        }
    }

    private Toast(string text, ToastKind kind, float seconds)
    {
        _text = text;
        _kind = kind;
        _duration = seconds;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = DarkTheme.SurfaceAlt;
        Opacity = 0;
        DoubleBuffered = true;
        Font = DarkTheme.UiFont;

        var size = TextRenderer.MeasureText(text, Font, new Size(360, 0), TextFormatFlags.WordBreak);
        Size = new Size(Math.Max(220, size.Width + 66), Math.Max(44, size.Height + 24));
        _timer.Tick += (_, _) => Tick();
        Click += (_, _) => _life = Math.Max(_life, _duration);
    }

    public static void Show(Control anchor, string text, ToastKind kind = ToastKind.Info, float seconds = 2.8f)
    {
        if (anchor == null || anchor.IsDisposed || string.IsNullOrWhiteSpace(text)) return;
        var owner = anchor.FindForm();
        if (owner == null || !owner.Visible || owner.WindowState == FormWindowState.Minimized) return;

        // el mismo aviso dos veces seguidas no se apila
        foreach (var t in Open) if (t._text == text && t._life < t._duration) { t._life = 0.3f; return; }

        var toast = new Toast(text, kind, seconds) { _anchor = anchor };
        Open.Add(toast);
        toast.Reflow();
        toast.Show(owner);
        toast._timer.Start();
    }

    private static void Restack(Control anchor)
    {
        if (anchor == null || anchor.IsDisposed) return;
        var screen = anchor.RectangleToScreen(anchor.ClientRectangle);
        int y = screen.Bottom - 16;
        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var t = Open[i];
            y -= t.Height;
            t.Location = new Point(screen.Right - t.Width - 16, y);
            y -= 8;
        }
    }

    private void Reflow() => Restack(_anchor);

    private void Tick()
    {
        _life += 0.016f;
        float fadeIn = Math.Clamp(_life / 0.18f, 0f, 1f);
        float fadeOut = Math.Clamp((_duration + 0.35f - _life) / 0.35f, 0f, 1f);
        double o = Math.Min(fadeIn, fadeOut) * 0.97;
        if (Math.Abs(Opacity - o) > 0.01) Opacity = o;

        if (_life > _duration + 0.35f)
        {
            _timer.Stop();
            Open.Remove(this);
            var a = _anchor;
            Close();
            Dispose();
            Restack(a);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(DarkTheme.SurfaceAlt);

        var color = _kind switch
        {
            ToastKind.Success => DarkTheme.Success,
            ToastKind.Warning => DarkTheme.Warning,
            ToastKind.Error => DarkTheme.Danger,
            _ => DarkTheme.Accent
        };
        string icon = _kind switch
        {
            ToastKind.Success => "check",
            ToastKind.Warning => "warning",
            ToastKind.Error => "error",
            _ => "info"
        };

        using (var b = new SolidBrush(color)) g.FillRectangle(b, 0, 0, 3, Height);
        g.DrawImage(Icons.Get(icon, 18, color), 16, (Height - 18) / 2);
        TextRenderer.DrawText(g, _text, Font, new Rectangle(44, 0, Width - 54, Height), DarkTheme.Text,
                              TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        using var pen = new Pen(DarkTheme.Border);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>
/// Cuadros de dialogo con el tema del editor, en lugar de los carteles
/// grises de Windows. Misma forma de uso que MessageBox.Show.
/// </summary>
public static class Dialogs
{
    public static DialogResult Show(IWin32Window owner, string text)
        => Show(owner, text, "MU World Editor", MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DialogResult Show(IWin32Window owner, string text, string caption)
        => Show(owner, text, caption, MessageBoxButtons.OK, MessageBoxIcon.None);

    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons)
        => Show(owner, text, caption, buttons, MessageBoxIcon.None);

    public static DialogResult Show(IWin32Window owner, string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
    {
        using var dlg = new ThemedDialog(text, caption, buttons, icon);
        return owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
    }

    private sealed class ThemedDialog : Form
    {
        private readonly string _caption;

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; }
        }

        public ThemedDialog(string text, string caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            _caption = string.IsNullOrEmpty(caption) ? "MU World Editor" : caption;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            BackColor = DarkTheme.Surface;
            ForeColor = DarkTheme.Text;
            Font = DarkTheme.UiFont;
            KeyPreview = true;
            DoubleBuffered = true;

            string iconName = icon switch
            {
                MessageBoxIcon.Error => "error",
                MessageBoxIcon.Warning => "warning",
                MessageBoxIcon.Question => "question",
                MessageBoxIcon.Information => "info",
                _ => "logo"
            };
            Color iconColor = icon switch
            {
                MessageBoxIcon.Error => DarkTheme.Danger,
                MessageBoxIcon.Warning => DarkTheme.Warning,
                MessageBoxIcon.Information => DarkTheme.Info,
                _ => DarkTheme.Accent
            };

            var textSize = TextRenderer.MeasureText(text ?? "", Font, new Size(440, 0), TextFormatFlags.WordBreak);
            int w = Math.Max(380, Math.Min(560, textSize.Width + 110));
            int textH = TextRenderer.MeasureText(text ?? "", Font, new Size(w - 96, 0), TextFormatFlags.WordBreak).Height;
            int h = 44 + Math.Max(48, textH + 16) + 60;
            ClientSize = new Size(w, h);

            var pic = new PictureBox { Image = Icons.Get(iconName, 32, iconColor), Left = 24, Top = 56, Size = new Size(32, 32), BackColor = Color.Transparent };
            var lbl = new Label
            {
                Text = text, Left = 72, Top = 56, Width = w - 96, Height = textH + 8,
                ForeColor = DarkTheme.Text, BackColor = Color.Transparent, Font = Font
            };
            Controls.Add(pic);
            Controls.Add(lbl);

            var list = buttons switch
            {
                MessageBoxButtons.OKCancel => new[] { (Lang.T("ui.aceptar", "Aceptar"), DialogResult.OK), (Lang.T("ui.cancelar", "Cancelar"), DialogResult.Cancel) },
                MessageBoxButtons.YesNo => new[] { (Lang.T("ui.si", "Si"), DialogResult.Yes), (Lang.T("ui.no", "No"), DialogResult.No) },
                MessageBoxButtons.YesNoCancel => new[] { (Lang.T("ui.si", "Si"), DialogResult.Yes), (Lang.T("ui.no", "No"), DialogResult.No), (Lang.T("ui.cancelar", "Cancelar"), DialogResult.Cancel) },
                MessageBoxButtons.RetryCancel => new[] { (Lang.T("ui.reintentar", "Reintentar"), DialogResult.Retry), (Lang.T("ui.cancelar", "Cancelar"), DialogResult.Cancel) },
                _ => new[] { (Lang.T("ui.aceptar", "Aceptar"), DialogResult.OK) }
            };

            int bx = w - 20;
            for (int i = list.Length - 1; i >= 0; i--)
            {
                var (label, result) = list[i];
                var b = new ModernButton { Text = label, Width = 104, Height = 32, Primary = i == 0, DialogResult = result };
                bx -= b.Width;
                b.Location = new Point(bx, h - 48);
                bx -= 8;
                Controls.Add(b);
                if (i == 0) AcceptButton = b;
                if (result is DialogResult.Cancel or DialogResult.No || list.Length == 1) CancelButton = b;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(DarkTheme.Surface);
            using (var b = new SolidBrush(DarkTheme.Back)) g.FillRectangle(b, 0, 0, Width, 40);
            using (var b = new SolidBrush(DarkTheme.Back)) g.FillRectangle(b, 0, Height - 64, Width, 64);
            g.DrawImage(Icons.Get("logo", 16, DarkTheme.Accent), 14, 12);
            TextRenderer.DrawText(g, _caption, DarkTheme.TitleFont, new Rectangle(38, 0, Width - 50, 40), DarkTheme.Text,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            using var pen = new Pen(DarkTheme.Border);
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            g.DrawLine(pen, 0, 40, Width, 40);
            g.DrawLine(pen, 0, Height - 64, Width, Height - 64);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Y < 40 && e.Button == MouseButtons.Left)
            {
                NativeDrag.Begin(Handle);
            }
        }
    }
}

internal static class NativeDrag
{
    [DllImport("user32.dll")] private static extern bool ReleaseCapture();
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);

    public static void Begin(IntPtr handle)
    {
        ReleaseCapture();
        SendMessage(handle, 0xA1, (IntPtr)2, IntPtr.Zero);
    }
}

/// <summary>Pantalla breve de presentacion mientras se arma la ventana principal.</summary>
public sealed class SplashForm : Form
{
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private float _t;
    public string Status = "Cargando...";

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; }
    }

    public SplashForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        Size = new Size(560, 320);
        BackColor = DarkTheme.Back;
        DoubleBuffered = true;
        TopMost = true;
        _timer.Tick += (_, _) => { _t += 0.016f; Invalidate(); };
        _timer.Start();
        HandleCreated += (_, _) => DarkTheme.UseDarkTitleBar(Handle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var bg = new LinearGradientBrush(ClientRectangle, Color.FromArgb(28, 24, 20), DarkTheme.Back, 60f))
            g.FillRectangle(bg, ClientRectangle);

        // rayos dorados suaves detras del emblema
        var c = new PointF(120, 150);
        using (var glow = new GraphicsPath())
        {
            glow.AddEllipse(c.X - 110, c.Y - 110, 220, 220);
            using var pgb = new PathGradientBrush(glow) { CenterColor = Color.FromArgb(70, DarkTheme.Accent), SurroundColors = new[] { Color.FromArgb(0, DarkTheme.Accent) } };
            g.FillPath(pgb, glow);
        }
        g.DrawImage(Icons.Get("logo", 96, DarkTheme.Accent), c.X - 48, c.Y - 48);

        TextRenderer.DrawText(g, "MU World Editor", DarkTheme.HeroFont, new Point(220, 104), DarkTheme.Text, TextFormatFlags.NoPrefix);
        TextRenderer.DrawText(g, "Editor de mapas para MU Online  ·  Season 21", DarkTheme.UiFont, new Point(224, 154), DarkTheme.TextDim, TextFormatFlags.NoPrefix);
        string ver = "v" + (typeof(SplashForm).Assembly.GetName().Version?.ToString(3) ?? "1.0");
        TextRenderer.DrawText(g, ver, DarkTheme.SmallFont, new Point(224, 178), DarkTheme.Accent, TextFormatFlags.NoPrefix);

        // barra de carga que va y viene
        var bar = new Rectangle(224, 236, 300, 3);
        using (var b = new SolidBrush(DarkTheme.SurfaceAlt)) g.FillRectangle(b, bar);
        float pos = (MathF.Sin(_t * 2.4f) * 0.5f + 0.5f) * (bar.Width - 90);
        using (var lg = new LinearGradientBrush(new RectangleF(bar.X + pos, bar.Y, 90, 3), Color.FromArgb(0, DarkTheme.Accent), DarkTheme.Accent, 0f))
        {
            var blend = new ColorBlend { Colors = new[] { Color.FromArgb(0, DarkTheme.Accent), DarkTheme.Accent, Color.FromArgb(0, DarkTheme.Accent) }, Positions = new[] { 0f, 0.5f, 1f } };
            lg.InterpolationColors = blend;
            g.FillRectangle(lg, bar.X + pos, bar.Y, 90, 3);
        }
        TextRenderer.DrawText(g, Status, DarkTheme.SmallFont, new Point(224, 248), DarkTheme.TextFaint, TextFormatFlags.NoPrefix);

        using var pen = new Pen(DarkTheme.Border);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Ventana de atajos de teclado (F1).</summary>
public sealed class ShortcutsDialog : Form
{
    private static readonly (string group, (string keys, string what)[] items)[] Groups =
    {
        ("Archivo", new[] { ("Ctrl+S", "Guardar el mapa"), ("Ctrl+Z", "Deshacer"), ("Ctrl+Y", "Rehacer"), ("F1", "Esta ayuda") }),
        ("Herramientas", new[] { ("1", "Ver"), ("2", "Altura"), ("3", "Atributos"), ("4", "Tiles"), ("5", "Transparencia"), ("6", "Objetos"), ("7", "Area"), ("Tab", "Mostrar u ocultar el panel") }),
        ("Camara", new[] { ("Clic derecho", "Girar la camara"), ("Clic del medio / Espacio", "Desplazar"), ("Rueda", "Acercar y alejar"), ("W A S D / flechas", "Mover la vista"), ("F", "Encuadrar la seleccion"), ("Home", "Volver al inicio") }),
        ("Pincel", new[] { ("Ctrl + rueda", "Tamano del pincel"), ("Shift + pincel", "Efecto inverso") }),
        ("Objetos", new[] { ("M / R / Y", "Mover / rotar / escalar"), ("Ctrl+C / Ctrl+V", "Copiar y pegar"), ("Ctrl+D", "Duplicar"), ("Supr", "Borrar"), ("Ctrl + clic", "Objeto nuevo"), ("Re Pag / Av Pag", "Objeto apilado") }),
        ("Vista", new[] { ("G", "Grilla de celdas"), ("T", "Marcas de atributo") })
    };

    protected override CreateParams CreateParams
    {
        get { var cp = base.CreateParams; cp.ClassStyle |= 0x00020000; return cp; }
    }

    public ShortcutsDialog()
    {
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        Size = new Size(720, 520);
        BackColor = DarkTheme.Surface;
        DoubleBuffered = true;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode is Keys.Escape or Keys.F1 or Keys.Enter) Close(); };
        var close = new ModernButton { Text = Lang.T("ui.cerrar", "Cerrar"), Primary = true, Width = 104, Height = 32 };
        close.Location = new Point(Width - close.Width - 20, Height - 48);
        close.Click += (_, _) => Close();
        Controls.Add(close);
        AcceptButton = close;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Y < 48 && e.Button == MouseButtons.Left) NativeDrag.Begin(Handle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(DarkTheme.Surface);
        using (var b = new SolidBrush(DarkTheme.Back)) g.FillRectangle(b, 0, 0, Width, 48);
        g.DrawImage(Icons.Get("keyboard", 20, DarkTheme.Accent), 18, 14);
        TextRenderer.DrawText(g, Lang.T("ui.atajos_de_teclado", "Atajos de teclado"), DarkTheme.TitleFont, new Rectangle(48, 0, 400, 48),
                              DarkTheme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

        int colW = (Width - 60) / 2;
        int[] y = { 64, 64 };
        int col = 0;
        foreach (var (group, items) in Groups)
        {
            int x = 24 + col * (colW + 12);
            TextRenderer.DrawText(g, group.ToUpperInvariant(), DarkTheme.SectionFont, new Point(x, y[col]), DarkTheme.Accent, TextFormatFlags.NoPrefix);
            y[col] += 20;
            foreach (var (keys, what) in items)
            {
                var ks = TextRenderer.MeasureText(keys, DarkTheme.SmallFont);
                var chip = new RectangleF(x, y[col], ks.Width + 12, 20);
                using (var path = DarkTheme.Round(chip, 4))
                {
                    using (var b = new SolidBrush(DarkTheme.SurfaceAlt)) g.FillPath(b, path);
                    using (var p = new Pen(DarkTheme.Border)) g.DrawPath(p, path);
                }
                TextRenderer.DrawText(g, keys, DarkTheme.SmallFont, Rectangle.Round(chip), DarkTheme.Text,
                                      TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, what, DarkTheme.UiFont, new Rectangle((int)chip.Right + 10, y[col], colW - (int)chip.Width - 10, 20),
                                      DarkTheme.TextDim, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
                y[col] += 25;
            }
            y[col] += 12;
            col = y[0] <= y[1] ? 0 : 1;
        }

        using var pen = new Pen(DarkTheme.Border);
        g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        g.DrawLine(pen, 0, 48, Width, 48);
    }
}
