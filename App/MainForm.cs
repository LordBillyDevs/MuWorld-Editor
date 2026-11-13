using MuWorldEditor.Editing;
using MuWorldEditor.Formats;
using MuWorldEditor.Render;
using MuWorldEditor.World;

namespace MuWorldEditor.App;

public sealed class MainForm : Form
{
    private readonly MapView _view = new();
    private readonly ToolStrip _bar = new();
    private readonly StatusBar _statusBar = new();
    private readonly TitleBar _titleBar = new();
    private readonly StartScreen _start = new();
    private readonly Settings _cfg = Settings.Load();
    private readonly InfoSink _lblCoord, _lblInfo;
    private readonly bool _customChrome;

    /// <summary>
    /// Destino de los mensajes cortos: van a la barra de estado y, si son
    /// avisos, salen ademas como cartelito flotante.
    /// </summary>
    private sealed class InfoSink
    {
        private readonly Action<string> _set;
        public InfoSink(Action<string> set) { _set = set; }
        public string Text { set => _set(value); }
    }

    private readonly DarkTabControl _tabs = new();
    private readonly ToolStripComboBox _worldBox = new();
    private ToolStripButton _btnUndo, _btnRedo, _btnSave, _btnNavMove, _btnNavRotate;

    private Panel _terrainPanel;
    private ListBox _tileList, _objList;
    private Label _selInfo;
    private CheckedListBox _attList;
    private DarkSlider _radius, _hardness, _strength;
    private ComboBox _heightMode, _newObjectBox;
    private NumberBox _numTarget, _numMin, _numMax;
    private RadioButton _rbLayer1, _rbLayer2, _rbAlphaAdd, _rbAlphaDel, _rbAttAdd, _rbAttDel;
    private readonly Dictionary<EditMode, ToolStripButton> _modeButtons = new();
    private bool _building;

    /// <summary>Atajo para los textos traducibles de la ventana.</summary>
    private static string L(string key, string spanish) => Lang.T(key, spanish);

    public string ClientDataPath { get; private set; }

    public MainForm()
    {
        _customChrome = _cfg.CustomTitleBar;
        UiState.Collapsed = new HashSet<string>(_cfg.CollapsedSections ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        _lblCoord = new InfoSink(t => _statusBar.Apply(() => _statusBar.Cursor3D = t ?? ""));
        _lblInfo = new InfoSink(t => ShowInfo(t));

        Text = "MU World Editor";
        Font = DarkTheme.UiFont;
        Width = 1500; Height = 900;
        MinimumSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = DarkTheme.Back;
        ForeColor = DarkTheme.Text;
        Opacity = 1.0;
        Icon = Embedded.AppIcon();

        if (_customChrome)
        {
            FormBorderStyle = FormBorderStyle.None;
            Padding = new Padding(4);
        }

        // las preferencias de presentacion se aplican antes de crear la vista
        GlControl.RequestedSamples = _cfg.Antialias ? 4 : 0;
        _view.State.ShowMinimap = _cfg.ShowMinimap;
        _view.State.ShowSky = _cfg.ShowSky;
        _view.State.TerrainShading = Math.Clamp(_cfg.TerrainShading, 0, 3);

        _bar.Renderer = new DarkRenderer();
        _bar.BackColor = DarkTheme.Back;
        _bar.ForeColor = DarkTheme.Text;
        _bar.Font = DarkTheme.UiFont;

        StartLog.Write("barra de herramientas");
        BuildToolbar();
        StartLog.Write("panel lateral");
        BuildSidePanel();
        StartLog.Write("armando la ventana");

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            SplitterWidth = 5,
            BackColor = DarkTheme.Back
        };

        // divisor fino: una linea, y se marca dorado al pasar el mouse
        bool splitterHot = false;
        split.Paint += (_, e) =>
        {
            int x = split.SplitterDistance;
            var r = new Rectangle(x, 0, split.SplitterWidth, split.Height);
            using (var b = new SolidBrush(DarkTheme.Back)) e.Graphics.FillRectangle(b, r);
            using (var b = new SolidBrush(splitterHot ? DarkTheme.Accent : DarkTheme.BorderSoft))
                e.Graphics.FillRectangle(b, x + split.SplitterWidth / 2, 0, 1, split.Height);
        };
        split.MouseMove += (_, e) =>
        {
            bool hot = split.SplitterRectangle.Contains(e.Location);
            if (hot != splitterHot) { splitterHot = hot; split.Invalidate(); }
        };
        split.MouseLeave += (_, _) => { if (splitterHot) { splitterHot = false; split.Invalidate(); } };
        split.SplitterMoved += (_, _) =>
        {
            split.Invalidate();
            // solo guardamos el ancho cuando lo movio el usuario, no cuando lo
            // acomodamos nosotros: si no, se realimenta y el panel se dispara
            if (_applyingPanel) return;
            int w = split.Width - split.SplitterDistance - split.SplitterWidth;
            if (w >= 160 && w <= 900) _panelWidth = w;
        };
        split.DoubleClick += (_, _) => TogglePanel();
        _view.Dock = DockStyle.Fill;
        _tabs.Dock = DockStyle.Fill;
        split.Panel1.Controls.Add(_view);
        split.Panel1.Controls.Add(_start);
        _start.BringToFront();
        split.Panel2.Controls.Add(_tabs);

        Controls.Add(split);
        Controls.Add(_statusBar);
        Controls.Add(_bar);
        if (_customChrome) Controls.Add(_titleBar);

        _split = split;
        _panelWidth = _cfg.PanelWidth;
        if (_panelWidth < 160 || _panelWidth > 900) _panelWidth = 340;
        _panelHidden = _cfg.PanelHidden;
        RestoreWindowBounds();
        StartLog.Write("tamano de ventana restaurado");

        // el ancho real se aplica cuando la ventana ya tiene su tamano
        Shown += (_, _) => ApplyPanelWidth();
        Resize += (_, _) =>
        {
            if (WindowState != FormWindowState.Minimized) ApplyPanelWidth();
            if (_customChrome)
            {
                Padding = WindowState == FormWindowState.Maximized ? new Padding(0) : new Padding(4);
                _titleBar.Invalidate();
            }
        };

        _view.StatusChanged += () => { _lblCoord.Text = _view.StatusText; };
        _view.SelectionChanged += RefreshSelection;

        _view.AreaChanged += RefreshAreaInfo;
        _view.Sampled += texto => { if (texto != null) _lblInfo.Text = texto; };
        _view.ObjectPickedFromView += () =>
        {
            SetMode(EditMode.Objects);
            RefreshSelection();
            ScrollToSelection();
        };
        _view.History.Changed += RefreshHistoryButtons;

        // pantalla de inicio
        _start.ChooseClient += ChooseClient;
        _start.OpenLoose += OpenLooseWorld;
        _start.Back += () => ShowStart(false);
        _start.OpenRecent += OpenRecent;
        _start.RemoveRecent += r =>
        {
            _cfg.Recent.RemoveAll(x => string.Equals(x.Folder, r.Folder, StringComparison.OrdinalIgnoreCase));
            Settings.Save(_cfg);
            _start.SetRecent(_cfg.Recent);
        };
        _start.SetRecent(_cfg.Recent);

        var timer = new System.Windows.Forms.Timer { Interval = 400 };
        timer.Tick += (_, _) =>
        {
            bool dirty = _view.Ref.World.Dirty;
            int sel = _view.State.Selection.Count;
            int fps = _view.LastFrameMs > 0 ? Math.Min(999, 1000 / Math.Max(1, _view.LastFrameMs)) : 0;
            _statusBar.Apply(() =>
            {
                _statusBar.Dirty = dirty;
                _statusBar.Selection = sel > 0 ? $"{sel} {L("ui.sel_corto", "elegidos")}" : $"{_view.VisibleObjects} {L("ui.visibles_corto", "visibles")}";
                _statusBar.Perf = $"{_view.LastFrameMs} ms";
                _statusBar.Gpu = ShortGpu(_view.GlInfo) + (_view.Samples > 0 ? $"  ·  MSAA {_view.Samples}x" : "");
            });
            _titleBar.Dirty = dirty;
            if (_btnSave != null) _btnSave.Enabled = dirty;
        };
        timer.Start();

        DarkTheme.Apply(this);
        split.BackColor = DarkTheme.Back;
        split.Panel1.BackColor = DarkTheme.Back;

        Shown += (_, _) => AutoOpen();
        FormClosing += OnClosingCheck;
    }

    /// <summary>Nombre corto de la placa de video, sin el texto de OpenGL.</summary>
    private static string ShortGpu(string info)
    {
        if (string.IsNullOrEmpty(info)) return "";
        var s = info.Split('|')[0].Trim();
        s = s.Replace("NVIDIA GeForce ", "").Replace("AMD Radeon ", "Radeon ").Replace("/PCIe/SSE2", "");
        return s;
    }

    /// <summary>Mensaje corto: queda en la barra de estado y sale como aviso flotante.</summary>
    private void ShowInfo(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var kind = ToastKind.Info;
        string low = text.ToLowerInvariant();
        if (low.StartsWith("guardado") || low.StartsWith("saved") || low.Contains("guardad") || low.StartsWith("copiado") || low.StartsWith("pegado") || low.Contains("recalculada")) kind = ToastKind.Success;
        else if (low.StartsWith("no ") || low.Contains("no pude")) kind = ToastKind.Warning;
        _statusBar.Apply(() =>
        {
            _statusBar.Message = text;
            _statusBar.MessageColor = kind == ToastKind.Success ? DarkTheme.Success : kind == ToastKind.Warning ? DarkTheme.Warning : DarkTheme.TextDim;
        });
        Toast.Show(_view, text, kind);
    }

    // ------------------------------------------------------- ventana propia
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (_cfg?.CustomTitleBar == true)
            {
                cp.Style |= 0x00020000 | 0x00080000 | 0x00010000;   // MINIMIZEBOX | SYSMENU | MAXIMIZEBOX: la barra de tareas puede minimizar
                cp.ClassStyle |= 0x00020000;                         // CS_DROPSHADOW
            }
            return cp;
        }
    }

    protected override void WndProc(ref Message m)
    {
        const int WM_NCHITTEST = 0x84;
        if (_customChrome && m.Msg == WM_NCHITTEST && WindowState != FormWindowState.Maximized)
        {
            base.WndProc(ref m);
            var p = PointToClient(new Point((short)(m.LParam.ToInt64() & 0xFFFF), (short)((m.LParam.ToInt64() >> 16) & 0xFFFF)));
            const int grip = 8;
            bool l = p.X <= grip, r = p.X >= ClientSize.Width - grip, t = p.Y <= grip, b = p.Y >= ClientSize.Height - grip;
            if (t && l) m.Result = (IntPtr)13; else if (t && r) m.Result = (IntPtr)14;
            else if (b && l) m.Result = (IntPtr)16; else if (b && r) m.Result = (IntPtr)17;
            else if (l) m.Result = (IntPtr)10; else if (r) m.Result = (IntPtr)11;
            else if (t) m.Result = (IntPtr)12; else if (b) m.Result = (IntPtr)15;
            return;
        }
        base.WndProc(ref m);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_customChrome && WindowState != FormWindowState.Maximized)
            using (var pen = new Pen(DarkTheme.Border))
                e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void OnResizeEnd(EventArgs e) { base.OnResizeEnd(e); Invalidate(); }

    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e);
        // maximizada sin marco no tiene que tapar la barra de tareas
        if (_customChrome && IsHandleCreated)
            MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
    }

    /// <summary>Vuelve a poner la ventana donde estaba y del tamano que tenia.</summary>
    private void RestoreWindowBounds()
    {
        var r = new Rectangle(_cfg.WindowX, _cfg.WindowY, Math.Max(960, _cfg.WindowW), Math.Max(600, _cfg.WindowH));
        bool visible = _cfg.WindowX != int.MinValue &&
                       Screen.AllScreens.Any(sc => sc.WorkingArea.IntersectsWith(new Rectangle(r.X + 40, r.Y + 10, 200, 40)));
        if (visible)
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = r;
        }
        else Size = new Size(r.Width, r.Height);
        if (_cfg.Maximized)
            Shown += (_, _) =>
            {
                if (_customChrome) MaximizedBounds = Screen.FromHandle(Handle).WorkingArea;
                WindowState = FormWindowState.Maximized;
            };
    }

    /// <summary>Guarda todo lo que se recuerda entre sesiones.</summary>
    private void SaveUiState()
    {
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        _cfg.WindowX = b.X; _cfg.WindowY = b.Y; _cfg.WindowW = b.Width; _cfg.WindowH = b.Height;
        _cfg.Maximized = WindowState == FormWindowState.Maximized;
        _cfg.PanelWidth = _panelWidth;
        _cfg.PanelHidden = _panelHidden;
        _cfg.ActiveTab = _tabs.SelectedIndex;
        _cfg.Language = Lang.Current;
        _cfg.ClientDataPath = ClientDataPath ?? _cfg.ClientDataPath ?? "";
        _cfg.ShowMinimap = _view.State.ShowMinimap;
        _cfg.ShowSky = _view.State.ShowSky;
        _cfg.TerrainShading = _view.State.TerrainShading;
        _cfg.CollapsedSections = UiState.Collapsed.ToList();
        Settings.Save(_cfg);
    }

    // ------------------------------------------------------ pantalla de inicio
    private void ShowStart(bool show)
    {
        _start.CanGoBack = _view.HasWorld;
        if (show) { _start.SetRecent(_cfg.Recent); _start.Visible = true; _start.BringToFront(); _start.Focus(); }
        else { _start.Visible = false; _view.Focus(); }
    }

    private void OpenRecent(RecentMap r)
    {
        if (!Directory.Exists(r.Folder))
        {
            Dialogs.Show(this, L("msg.reciente_no_existe", "Esa carpeta ya no existe:") + Environment.NewLine + r.Folder,
                         "MU World Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (r.FromClient && !string.IsNullOrEmpty(r.ClientDataPath) && Directory.Exists(r.ClientDataPath))
        {
            if (!string.Equals(ClientDataPath, r.ClientDataPath, StringComparison.OrdinalIgnoreCase))
                SetClientPath(r.ClientDataPath, autoSelect: false);
            for (int i = 0; i < _worldBox.Items.Count; i++)
                if (_worldBox.Items[i] is CatalogItem ci && ci.Id == r.World)
                {
                    if (_worldBox.SelectedIndex == i) OpenSelectedWorld(force: true);
                    else _worldBox.SelectedIndex = i;
                    return;
                }
        }
        OpenWorldFolder(r.Folder);
    }

    /// <summary>Anota el mapa recien abierto en recientes y le saca una miniatura.</summary>
    private void RememberOpened(bool fromClient)
    {
        var w = _view.Ref.World;
        if (w == null || string.IsNullOrEmpty(w.Directory)) return;
        string name = fromClient
            ? $"World{w.WorldNumber}{WorldNames.Suffix(w.WorldNumber).Replace("  -  ", " · ")}"
            : Path.GetFileName(w.Directory.TrimEnd(Path.DirectorySeparatorChar)) + $" (mapa {w.WorldNumber})";
        _cfg.Touch(new RecentMap
        {
            Folder = w.Directory, World = w.WorldNumber, ClientDataPath = ClientDataPath ?? "",
            Name = name, Format = w.FormatSummary, Opened = DateTime.Now, FromClient = fromClient
        });
        Settings.Save(_cfg);

        string dir = w.Directory;
        BeginInvoke(() =>
        {
            try
            {
                using var bmp = WorldMapExport.Render(_view.Ref.World, _view.Tiles, WorldMapExport.Layer.Tiles, 1);
                using var thumb = new Bitmap(220, 120);
                using (var g = Graphics.FromImage(thumb))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    // recorte centrado para que llene la tarjeta
                    g.DrawImage(bmp, new Rectangle(0, 0, 220, 120), new Rectangle(0, 58, 256, 140), GraphicsUnit.Pixel);
                }
                thumb.Save(Settings.ThumbPath(dir), System.Drawing.Imaging.ImageFormat.Png);
            }
            catch { }
        });
    }

    /// <summary>Despues de abrir un mapa: titulo, barra de estado y pantalla de inicio.</summary>
    private void AfterWorldOpened(string displayName, bool fromClient)
    {
        var w = _view.Ref.World;
        Text = "MU World Editor - " + displayName;
        _titleBar.Subtitle = displayName;
        _statusBar.Apply(() => { _statusBar.MapName = displayName; _statusBar.Format = w.FormatSummary; });
        ShowStart(false);
        RememberOpened(fromClient);
        _objThumbs.Clear();
    }

    private double _windowOpacity = 1.0;
    private SplitContainer _split;
    private int _panelWidth = 320;
    private bool _panelHidden;
    private bool _applyingPanel;

    /// <summary>Deja el panel de herramientas con el ancho elegido.</summary>
    private void ApplyPanelWidth()
    {
        if (_split == null || _split.Width < 520) return;

        // los minimos solo se pueden fijar cuando el control ya tiene ancho real
        if (_split.Panel2MinSize != 150)
        {
            _split.Panel1MinSize = 300;
            _split.Panel2MinSize = 150;
        }
        _applyingPanel = true;
        try
        {
        int target = _panelHidden ? 0 : Math.Clamp(_panelWidth, _split.Panel2MinSize, _split.Width - _split.Panel1MinSize - _split.SplitterWidth);
        if (_panelHidden)
        {
            _split.Panel2Collapsed = true;
            _applyingPanel = false;
            return;
        }
        _split.Panel2Collapsed = false;
        int dist = _split.Width - target - _split.SplitterWidth;
        dist = Math.Clamp(dist, _split.Panel1MinSize, _split.Width - _split.Panel2MinSize - _split.SplitterWidth);
        if (dist > 0 && dist != _split.SplitterDistance) _split.SplitterDistance = dist;
        }
        finally { _applyingPanel = false; }
    }

    /// <summary>Rearma la pestana Ver para que los tildes muestren el estado real.</summary>
    private void RebuildViewPanel()
    {
        if (_viewPage == null) return;
        bool antes = _building;
        _building = true;
        _viewPage.Controls.Clear();
        _viewPage.Controls.Add(BuildViewPanel());
        DarkTheme.Apply(_viewPage);
        _building = antes;
    }

    private void TogglePanel()
    {
        _panelHidden = !_panelHidden;
        ApplyPanelWidth();
        _view.Focus();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DarkTheme.UseDarkTitleBar(Handle);
    }

    // --------------------------------------------------------------- barra
    /// <summary>Boton de la barra con icono; el texto queda en el tooltip junto con el atajo.</summary>
    private ToolStripButton IconButton(string icon, string tip, string shortcut = null, bool showText = false, string text = null)
    {
        var b = new ToolStripButton(showText ? text ?? tip : "")
        {
            Image = Icons.Get(icon, 18, DarkTheme.Text),
            DisplayStyle = showText ? ToolStripItemDisplayStyle.ImageAndText : ToolStripItemDisplayStyle.Image,
            ToolTipText = shortcut != null ? $"{tip}   ({shortcut})" : tip,
            AutoSize = true,
            Padding = new Padding(showText ? 4 : 6, 2, showText ? 6 : 6, 2),
            ImageScaling = ToolStripItemImageScaling.None,
            TextImageRelation = TextImageRelation.ImageBeforeText
        };
        b.EnabledChanged += (_, _) => b.Image = Icons.Get(icon, 18, b.Enabled ? DarkTheme.Text : DarkTheme.TextFaint);
        b.CheckedChanged += (_, _) => b.Image = Icons.Get(icon, 18, b.Checked ? DarkTheme.Accent : DarkTheme.Text);
        return b;
    }

    /// <summary>Titulo chiquito de cada grupo de la barra.</summary>
    private static ToolStripLabel GroupLabel(string text) => new(text.ToUpperInvariant())
    {
        ForeColor = DarkTheme.TextFaint,
        Font = DarkTheme.SectionFont,
        Padding = new Padding(6, 0, 2, 0)
    };

    private void BuildToolbar()
    {
        _bar.GripStyle = ToolStripGripStyle.Hidden;
        _bar.ImageScalingSize = new Size(18, 18);
        _bar.Padding = new Padding(6, 3, 6, 3);
        _bar.AutoSize = false;
        _bar.Height = 42;
        _bar.ShowItemToolTips = true;

        // ---------------------------------------------------------- archivo
        var home = IconButton("home", L("ui.inicio", "Inicio"));
        home.Click += (_, _) => ShowStart(!_start.Visible);
        var open = IconButton("folder", L("ui.elegir_la_carpeta_data_del_cliente", "Elegir la carpeta Data del cliente"));
        open.Click += (_, _) => ChooseClient();

        _worldBox.DropDownStyle = ComboBoxStyle.DropDownList;
        _worldBox.AutoSize = false;
        _worldBox.Size = new Size(240, 26);
        _worldBox.FlatStyle = FlatStyle.Flat;
        _worldBox.BackColor = DarkTheme.SurfaceAlt;
        _worldBox.ForeColor = DarkTheme.Text;
        _worldBox.ToolTipText = L("ui.mapa_tip", "Mapa del cliente");
        DarkTheme.StyleCombo(_worldBox.ComboBox);
        _worldBox.ComboBox.DropDownHeight = 420;
        _worldBox.SelectedIndexChanged += (_, _) => OpenSelectedWorld();

        _btnSave = IconButton("save", L("ui.guardar", "Guardar"), "Ctrl+S");
        _btnSave.Enabled = false;
        _btnSave.Click += (_, _) => SaveWorld();

        _btnUndo = IconButton("undo", L("ui.deshacer", "Deshacer"), "Ctrl+Z");
        _btnUndo.Enabled = false;
        _btnUndo.Click += (_, _) => { _view.History.Undo(); _view.RebuildAll(); };

        _btnRedo = IconButton("redo", L("ui.rehacer", "Rehacer"), "Ctrl+Y");
        _btnRedo.Enabled = false;
        _btnRedo.Click += (_, _) => { _view.History.Redo(); _view.RebuildAll(); };

        _bar.Items.Add(home);
        _bar.Items.Add(open);
        _bar.Items.Add(_worldBox);
        _bar.Items.Add(new ToolStripSeparator());
        _bar.Items.Add(_btnSave);
        _bar.Items.Add(_btnUndo);
        _bar.Items.Add(_btnRedo);
        _bar.Items.Add(new ToolStripSeparator());

        // -------------------------------------------------------- navegacion
        _btnNavMove = IconButton("pan", L("ui.mover", "Mover") + " - " + L("ui.el_boton_izquierdo_arrastra_el_mapa", "El boton izquierdo arrastra el mapa"));
        _btnNavMove.Checked = true;
        _btnNavRotate = IconButton("orbit", L("ui.girar", "Girar") + " - " + L("ui.el_boton_izquierdo_gira_la_camara", "El boton izquierdo gira la camara"));
        _btnNavMove.Click += (_, _) => SetNavigation(NavMode.Move);
        _btnNavRotate.Click += (_, _) => SetNavigation(NavMode.Rotate);
        _bar.Items.Add(_btnNavMove);
        _bar.Items.Add(_btnNavRotate);
        _bar.Items.Add(new ToolStripSeparator());

        // ---------------------------------------------------- herramientas
        _bar.Items.Add(GroupLabel(L("ui.herramienta", "Herramienta")));
        AddModeButton(EditMode.View, L("ui.ver", "Ver"), "eye", "1");
        AddModeButton(EditMode.Height, L("ui.altura", "Altura"), "mountain", "2");
        AddModeButton(EditMode.Attribute, L("ui.atributos", "Atributos"), "flag", "3");
        AddModeButton(EditMode.Tile, L("ui.tiles", "Tiles"), "tiles", "4");
        AddModeButton(EditMode.Alpha, L("ui.transparencia", "Transparencia"), "blend", "5");
        AddModeButton(EditMode.Objects, L("ui.objetos", "Objetos"), "cube", "6");
        AddModeButton(EditMode.Area, L("ui.area", "Area"), "area", "7");
        _bar.Items.Add(new ToolStripSeparator());

        // ------------------------------------------------------------- vista
        var view3d = new ToolStripDropDownButton(L("ui.vista", "Vista"))
        {
            Image = Icons.Get("camera", 18, DarkTheme.Text),
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ImageScaling = ToolStripItemImageScaling.None,
            ToolTipText = L("ui.poner_la_camara_en_una_vista_fija", "Poner la camara en una vista fija")
        };
        void AddView(string text, string icon, string keys, Action a)
        {
            var it = new ToolStripMenuItem(text) { Image = icon != null ? Icons.Get(icon, 16, DarkTheme.TextDim) : null, ShortcutKeyDisplayString = keys };
            it.Click += (_, _) => { a(); _view.Focus(); };
            view3d.DropDownItems.Add(it);
        }
        AddView(L("ui.desde_arriba", "Desde arriba"), "map", null, () => _view.SetCameraView(CameraView.Top));
        AddView(L("ui.de_frente", "De frente"), "eye", null, () => _view.SetCameraView(CameraView.Front));
        AddView(L("ui.de_costado", "De costado"), "orbit", null, () => _view.SetCameraView(CameraView.Side));
        AddView(L("ui.encuadrar_la_seleccion", "Encuadrar la seleccion"), "focus", "F", _view.FocusOnSelection);
        AddView(L("ui.volver_al_inicio", "Volver al inicio"), "home", "Inicio", _view.ResetCamera);
        view3d.DropDownItems.Add(new ToolStripSeparator());

        ToolStripMenuItem Toggle(string text, string icon, Func<bool> get, Action<bool> set)
        {
            var it = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = get(), Image = Icons.Get(icon, 16, DarkTheme.TextDim) };
            it.CheckedChanged += (_, _) => { set(it.Checked); _view.State.RaiseChanged(); RebuildViewPanel(); };
            view3d.DropDownItems.Add(it);
            return it;
        }
        Toggle(L("ui.ver_en_alambre", "Ver en alambre"), "wire", () => _view.State.Wireframe, v => _view.State.Wireframe = v);
        Toggle(L("ui.minimapa", "Minimapa"), "map", () => _view.State.ShowMinimap, v => _view.State.ShowMinimap = v);
        Toggle(L("ui.cielo", "Cielo con degradado"), "sun", () => _view.State.ShowSky, v => _view.State.ShowSky = v);
        Toggle(L("ui.grilla_de_celdas", "Grilla de celdas"), "grid", () => _view.State.ShowGrid, v => _view.State.ShowGrid = v);
        DarkTheme.StyleMenu(view3d);
        _bar.Items.Add(view3d);

        var mapMenu = new ToolStripDropDownButton(L("ui.mapa_menu", "Mapa"))
        {
            Image = Icons.Get("layers", 18, DarkTheme.Text),
            DisplayStyle = ToolStripItemDisplayStyle.ImageAndText,
            ImageScaling = ToolStripItemImageScaling.None
        };
        void AddMap(string text, string icon, string tip, Action a)
        {
            var it = new ToolStripMenuItem(text) { ToolTipText = tip, Image = Icons.Get(icon, 16, DarkTheme.TextDim) };
            it.Click += (_, _) => { a(); _view.Focus(); };
            mapMenu.DropDownItems.Add(it);
        }
        AddMap(L("ui.abrir_un_mapa_suelto", "Abrir un mapa suelto..."), "folder", "Editar un mapa que este en cualquier carpeta, aunque no sea del cliente", OpenLooseWorld);
        mapMenu.DropDownItems.Add(new ToolStripSeparator());
        AddMap(L("ui.guardar_imagen_del_mapa", "Guardar imagen del mapa"), "image", "Una foto del mapa entero visto desde arriba", ExportWorldMap);
        AddMap(L("ui.calcular_la_luz_del_terreno", "Calcular la luz del terreno"), "sun", "Recalcula TerrainLight con las alturas de ahora", CalculateLightmap);
        mapMenu.DropDownItems.Add(new ToolStripSeparator());
        AddMap(L("ui.descartar_cambios", L("msg.descartar_cambios", "Descartar cambios")), "refresh", "Vuelve a leer el mapa del disco", RevertWorld);
        DarkTheme.StyleMenu(mapMenu);
        _bar.Items.Add(mapMenu);

        // ------------------------------------------------------------ derecha
        var about = IconButton("help", L("ui.acerca_de", "Acerca de"));
        about.Alignment = ToolStripItemAlignment.Right;
        about.Click += (_, _) => ShowAbout();
        _bar.Items.Add(about);

        var keys = IconButton("keyboard", L("ui.atajos_de_teclado", "Atajos de teclado"), "F1");
        keys.Alignment = ToolStripItemAlignment.Right;
        keys.Click += (_, _) => ShowShortcuts();
        _bar.Items.Add(keys);

        var langMenu = new ToolStripDropDownButton("")
        {
            Image = Icons.Get("globe", 18, DarkTheme.Text),
            DisplayStyle = ToolStripItemDisplayStyle.Image,
            ImageScaling = ToolStripItemImageScaling.None,
            Alignment = ToolStripItemAlignment.Right,
            ToolTipText = L("ui.idioma", "Idioma")
        };
        foreach (var (code, name) in Lang.Available)
        {
            var it = new ToolStripMenuItem(name) { Checked = Lang.Current == code, CheckOnClick = false, Tag = code };
            it.Click += (_, _) => ChangeLanguage(code);
            langMenu.DropDownItems.Add(it);
        }
        DarkTheme.StyleMenu(langMenu);
        _bar.Items.Add(langMenu);

        var panelBtn = IconButton("panel", L("ui.muestra_o_esconde_el_panel_de_herramient", "Muestra o esconde el panel de herramientas"), "Tab");
        panelBtn.Alignment = ToolStripItemAlignment.Right;
        panelBtn.Click += (_, _) => TogglePanel();
        _bar.Items.Add(panelBtn);
    }

    private void ShowShortcuts()
    {
        using var d = new ShortcutsDialog();
        d.ShowDialog(this);
    }

    /// <summary>
    /// Guarda el idioma elegido. La ventana ya esta armada con los textos
    /// viejos, asi que se rearma reiniciando el programa.
    /// </summary>
    /// <summary>
    /// Guarda el idioma elegido y avisa a Lang. Application.Restart() cierra la
    /// ventana antes de reiniciar, y ese cierre vuelve a guardar la configuracion
    /// usando Lang.Current: si aca no lo actualizamos, esa segunda escritura
    /// pisa el idioma recien elegido con el viejo.
    /// </summary>
    private void PersistLanguage(string code)
    {
        Lang.Use(code);
        SaveUiState();
    }

    private void ChangeLanguage(string code)
    {
        if (code == Lang.Current) return;

        _cfg.Language = code;
        Settings.Save(_cfg);

        var r = Dialogs.Show(this,
            L("ui.idioma_aviso", "El idioma cambia cuando se reinicia el editor.") +
            Environment.NewLine + Environment.NewLine +
            L("ui.reiniciar_ahora", "Reiniciar ahora?"),
            "MU World Editor", MessageBoxButtons.YesNo, MessageBoxIcon.Information);

        if (r != DialogResult.Yes) return;
        if (!ConfirmDiscard()) return;

        PersistLanguage(code);
        _view.Ref.World.Dirty = false;      // ya se pregunto arriba
        Application.Restart();
    }

    private void ShowAbout()
    {
        string texto = Embedded.ReadText("AVISO.txt")
                       ?? "MU World Editor - herramienta privada. Todos los derechos reservados.";
        string leeme = Embedded.ReadText("LEEME.txt");
        if (leeme != null) texto += Environment.NewLine + new string('-', 68) + Environment.NewLine + leeme;

        using var dlg = new Form
        {
            Text = L("ui.acerca_de", "Acerca de"),
            Size = new Size(680, 560),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.None,
            BackColor = DarkTheme.Surface,
            ForeColor = DarkTheme.Text,
            ShowInTaskbar = false,
            KeyPreview = true
        };
        string ver = "v" + (typeof(MainForm).Assembly.GetName().Version?.ToString(3) ?? "1.0");

        var header = new Panel { Dock = DockStyle.Top, Height = 96, BackColor = DarkTheme.Back };
        header.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.DrawImage(Icons.Get("logo", 48, DarkTheme.Accent), 24, 24);
            TextRenderer.DrawText(g, "MU World Editor", new Font(DarkTheme.FontFamilyName, 17f), new Point(86, 22), DarkTheme.Text, TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, ver + "  ·  Season 21  ·  .NET " + Environment.Version.ToString(2), DarkTheme.SmallFont, new Point(89, 56), DarkTheme.TextDim, TextFormatFlags.NoPrefix);
            using var pen = new Pen(DarkTheme.Border);
            g.DrawLine(pen, 0, header.Height - 1, header.Width, header.Height - 1);
        };
        header.MouseDown += (_, e) => { if (e.Button == MouseButtons.Left) NativeDrag.Begin(dlg.Handle); };

        var footer = new Panel { Dock = DockStyle.Bottom, Height = 58, BackColor = DarkTheme.Back };
        var close = new ModernButton { Text = L("ui.cerrar", "Cerrar"), Primary = true, Width = 104, Height = 32 };
        close.Location = new Point(dlg.Width - close.Width - 20, 13);
        close.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        close.Click += (_, _) => dlg.Close();
        footer.Controls.Add(close);

        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, Text = texto,
            ScrollBars = ScrollBars.Vertical, BackColor = DarkTheme.Surface, ForeColor = DarkTheme.TextDim,
            BorderStyle = BorderStyle.None, Font = DarkTheme.MonoFont
        };
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 16, 16, 12), BackColor = DarkTheme.Surface };
        body.Controls.Add(box);
        DarkTheme.DarkScrollbars(box);

        dlg.Controls.Add(body);
        dlg.Controls.Add(footer);
        dlg.Controls.Add(header);
        dlg.AcceptButton = close;
        dlg.CancelButton = close;
        dlg.Paint += (_, e) =>
        {
            using var pen = new Pen(DarkTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, dlg.Width - 1, dlg.Height - 1);
        };
        dlg.Padding = new Padding(1);
        dlg.ShowDialog(this);
    }

    private void AddModeButton(EditMode mode, string text, string icon, string key)
    {
        var b = IconButton(icon, text, key, showText: true, text: text);
        b.CheckOnClick = false;
        b.Checked = mode == EditMode.View;
        b.Click += (_, _) => SetMode(mode);
        _modeButtons[mode] = b;
        _bar.Items.Add(b);
    }

    private void SetNavigation(NavMode nav)
    {
        _view.State.Navigation = nav;
        _btnNavMove.Checked = nav == NavMode.Move;
        _btnNavRotate.Checked = nav == NavMode.Rotate;
        _view.Focus();
    }

    private EditMode _lastTerrainMode = EditMode.Height;
    private bool _attributesAutoShown;
    private TabPage _viewPage;

    private void SetMode(EditMode mode)
    {
        _view.State.Mode = mode;
        if (mode is EditMode.Height or EditMode.Attribute or EditMode.Tile or EditMode.Alpha)
            _lastTerrainMode = mode;

        // en Atributos las marcas se prenden solas, como en el editor original:
        // pintar banderas a ciegas no tiene sentido
        if (mode == EditMode.Attribute && !_view.State.ShowAttributes)
        {
            _view.State.ShowAttributes = true;
            _attributesAutoShown = true;
        }
        else if (mode != EditMode.Attribute && _attributesAutoShown)
        {
            _view.State.ShowAttributes = false;
            _attributesAutoShown = false;
        }
        foreach (var kv in _modeButtons) kv.Value.Checked = kv.Key == mode;

        bool antes = _building;
        _building = true;
        _tabs.SelectedIndex = mode switch
        {
            EditMode.Objects => 2,
            EditMode.Area => 3,
            EditMode.View => 0,
            _ => 1
        };
        _building = antes;

        RefreshTerrainPanel();
        _view.State.RaiseChanged();
        _view.Focus();
    }

    // ---------------------------------------------------------- panel lateral
    private void BuildSidePanel()
    {

        var tabView = new TabPage(L("ui.ver", "Ver")) { Tag = "eye" };
        _viewPage = tabView;
        tabView.Controls.Add(BuildViewPanel());

        var tabTerrain = new TabPage(L("ui.terreno", "Terreno")) { Tag = "mountain" };
        _terrainPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        tabTerrain.Controls.Add(_terrainPanel);

        var tabObjects = new TabPage(L("ui.objetos", "Objetos")) { Tag = "cube" };
        tabObjects.Controls.Add(BuildObjectPanel());

        _tabs.TabPages.Add(tabView);
        _tabs.TabPages.Add(tabTerrain);
        _tabs.TabPages.Add(tabObjects);

        var tabArea = new TabPage(L("ui.area", "Area")) { Tag = "area" };
        _areaPage = tabArea;
        tabArea.Controls.Add(BuildAreaPanel());
        _tabs.TabPages.Add(tabArea);

        // al cambiar de pestana se activa la herramienta de esa pestana: antes
        // habia que ir si o si a los botones de la barra y la pestana quedaba muerta
        _tabs.SelectedIndexChanged += (_, _) =>
        {
            if (_building) return;
            var modo = _view.State.Mode;
            switch (_tabs.SelectedIndex)
            {
                case 0:
                    if (modo != EditMode.View) SetMode(EditMode.View);
                    RebuildViewPanel();
                    break;
                case 1:
                    if (modo is EditMode.View or EditMode.Objects) SetMode(_lastTerrainMode);
                    break;
                case 2:
                    if (modo != EditMode.Objects) SetMode(EditMode.Objects);
                    break;
                case 3:
                    if (modo != EditMode.Area) SetMode(EditMode.Area);
                    break;
            }
        };
    }

    private Control BuildViewPanel()
    {
        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12, 6, 8, 12) };
        var s = _view.State;
        p.HandleCreated += (_, _) => Sections.Build(p, "ver");

        void Check(string text, Func<bool> get, Action<bool> set, string tip = null)
        {
            var c = new ModernCheckBox { Text = text, Checked = get(), AutoSize = true, Margin = new Padding(0, 3, 0, 3) };
            c.CheckedChanged += (_, _) => { set(c.Checked); s.RaiseChanged(); };
            if (tip != null) new ToolTip().SetToolTip(c, tip);
            p.Controls.Add(c);
        }

        p.Controls.Add(Header(L("ui.presentacion", "Presentacion")));
        Check(L("ui.cielo", "Cielo con degradado"), () => s.ShowSky, v => s.ShowSky = v, "El fondo pasa del horizonte al cielo, y la niebla toma ese color");
        Check(L("ui.minimapa", "Minimapa"), () => s.ShowMinimap, v => s.ShowMinimap = v, "Mapa chico arriba a la derecha: clic para viajar");
        Check(L("ui.ejes_en_la_esquina", "Ejes en la esquina"), () => s.ShowAxisHud, v => s.ShowAxisHud = v);
        Check(L("ui.contorno_al_pasar", "Contorno al pasar el mouse"), () => s.HoverHighlight, v => s.HoverHighlight = v,
              "Marca el objeto que esta debajo del mouse");
        p.Controls.Add(new Label { Text = L("ui.sombreado_del_terreno", "Sombreado del terreno"), AutoSize = true, ForeColor = DarkTheme.TextDim, Margin = new Padding(0, 6, 0, 2) });
        var shading = new ComboBox { Width = 266, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 4) };
        shading.Items.AddRange(new object[]
        {
            L("ui.luz_real", "Luz real del mapa (TerrainLight)"),
            L("ui.relieve", "Relieve calculado"),
            L("ui.combinada", "Combinada"),
            L("ui.plana", "Plana, sin sombras")
        });
        shading.SelectedIndex = Math.Clamp(s.TerrainShading, 0, 3);
        shading.SelectedIndexChanged += (_, _) => { if (_building) return; s.TerrainShading = shading.SelectedIndex; s.RaiseChanged(); };
        p.Controls.Add(shading);
        Check(L("ui.suavizado", "Bordes suavizados (MSAA)"), () => _cfg.Antialias, v =>
        {
            _cfg.Antialias = v;
            if (!_building) ShowInfo(L("ui.reinicio_aplica", "Se aplica la proxima vez que abras el editor"));
        });
        Check(L("ui.ventana_propia", "Barra de titulo propia"), () => _cfg.CustomTitleBar, v =>
        {
            _cfg.CustomTitleBar = v;
            if (!_building) ShowInfo(L("ui.reinicio_aplica", "Se aplica la proxima vez que abras el editor"));
        });

        p.Controls.Add(Header(L("ui.capas_del_terreno", "Capas del terreno")));
        Check(L("ui.capa_1_base", "Capa 1 (base)"), () => s.ShowLayer1, v => s.ShowLayer1 = v);
        Check(L("ui.capa_2_mezcla", "Capa 2 (mezcla)"), () => s.ShowLayer2, v => s.ShowLayer2 = v);
        Check(L("ui.grilla_de_celdas", "Grilla de celdas"), () => s.ShowGrid, v => s.ShowGrid = v, "Tambien con la tecla G");
        Check(L("ui.marcas_de_atributo", "Marcas de atributo"), () => s.ShowAttributes, v => s.ShowAttributes = v, "Tambien con la tecla T");
        p.Controls.Add(Slider(L("ui.opacidad_de_las_marcas", "Opacidad de las marcas"), 20, 220, s.AttributeOpacity, v => { s.AttributeOpacity = v; s.RaiseChanged(); }));

        p.Controls.Add(Header(L("ui.recuadro_de_trabajo", "Recuadro de trabajo")));
        p.Controls.Add(Slider(L("ui.tamano_en_celdas", "Tamano en celdas"), 2, 128, s.OverlayRange, v => { s.OverlayRange = v; s.RaiseChanged(); }));
        Check(L("ui.que_siga_al_mouse", "Que siga al mouse"), () => s.OverlayFollowsCursor, v => s.OverlayFollowsCursor = v,
              "Si lo destildas, el recuadro se queda en el centro de la vista");
        p.Controls.Add(new Label
        {
            AutoSize = false, Width = 265, Height = 32,
            ForeColor = DarkTheme.TextDim,
            Text = L("ui.atajo_alt_rueda_del_mouse", "Atajo: Alt + rueda del mouse")
        });

        Check(L("ui.caja_del_terreno", "Caja del terreno"), () => s.ShowTerrainBBox, v => s.ShowTerrainBBox = v);
        Check(L("ui.agua_animada", "Agua animada"), () => s.AnimateWater, v => s.AnimateWater = v);

        p.Controls.Add(Header(L("ui.objetos", "Objetos")));
        Check(L("ui.mostrar_objetos", "Mostrar objetos"), () => s.ShowObjects, v => s.ShowObjects = v);
        Check(L("ui.objetos_fijos", "Objetos fijos"), () => s.ShowStaticObjects, v => s.ShowStaticObjects = v);
        Check(L("ui.objetos_con_animacion", "Objetos con animacion"), () => s.ShowAnimatedObjects, v => s.ShowAnimatedObjects = v,
              "Pajaros, mariposas, banderas");
        Check(L("ui.reproducir_animaciones", "Reproducir animaciones"), () => s.PlayAnimations, v => s.PlayAnimations = v,
              "Los objetos animados se mueven como en el juego. Apagalo para verlos quietos en su pose inicial");
        p.Controls.Add(Slider(L("ui.velocidad_de_animacion", "Velocidad de animacion"), 10, 300, s.AnimationSpeed,
                              v => { s.AnimationSpeed = v; }));
        Check(L("ui.mostrar_sus_cajas", "Mostrar sus cajas"), () => s.ShowObjectBBox, v => s.ShowObjectBBox = v);
        Check(L("ui.contorno_del_seleccionado", "Contorno del seleccionado"), () => s.HighlightSelected, v => s.HighlightSelected = v,
              "Rodea el objeto elegido con un borde dorado, sin taparle la textura");
        Check(L("ui.guias_para_alinear", "Guias para alinear"), () => s.ShowObjectGuides, v => s.ShowObjectGuides = v,
              "Linea a plomo hasta el piso, cruz de centro y celda que ocupa");
        Check(L("ui.al_tocar_un_objeto_ir_a_objetos", "Al tocar un objeto, ir a Objetos"), () => s.JumpToObjectOnPick, v => s.JumpToObjectOnPick = v,
              "Un clic en la pestana Ver lo selecciona y abre la herramienta Objetos");

        p.Controls.Add(Header(L("ui.camara", "Camara")));
        p.Controls.Add(Slider(L("ui.velocidad_al_arrastrar_y_girar", "Velocidad al arrastrar y girar"), 20, 200, (int)(s.CameraSpeed * 100),
                              v => { s.CameraSpeed = v / 100f; }));
        p.Controls.Add(Slider(L("ui.velocidad_del_zoom", "Velocidad del zoom"), 20, 200, (int)(s.ZoomSpeed * 100),
                              v => { s.ZoomSpeed = v / 100f; }));
        p.Controls.Add(Slider(L("ui.apertura_grados", "Apertura (grados)"), 20, 100, (int)s.CameraAngle, v => { s.CameraAngle = v; s.RaiseChanged(); }));
        p.Controls.Add(Slider(L("ui.distancia_de_dibujo", "Distancia de dibujo"), 5000, 90000, (int)s.CameraFar, v => { s.CameraFar = v; s.RaiseChanged(); }));

        p.Controls.Add(Header(L("ui.ambiente", "Ambiente")));
        Check(L("ui.niebla", "Niebla"), () => s.ShowFog, v => s.ShowFog = v);

        var fogStart = Slider(L("ui.niebla_inicio", "Niebla: inicio"), 500, 30000, (int)s.FogStart, v => { s.FogStart = v; s.RaiseChanged(); });
        var fogEnd = Slider(L("ui.niebla_fin", "Niebla: fin"), 1000, 60000, (int)s.FogEnd, v => { s.FogEnd = v; s.RaiseChanged(); });
        var fogDens = Slider(L("ui.niebla_densidad", "Niebla: densidad"), 10, 400, (int)(s.FogDensity * 100), v => { s.FogDensity = v / 100f; s.RaiseChanged(); });
        p.Controls.Add(fogStart); p.Controls.Add(fogEnd); p.Controls.Add(fogDens);

        var btnAmbient = new ModernButton { Text = L("ui.color_de_ambiente", "Color de ambiente"), Width = 260, Height = 28 };
        btnAmbient.Click += (_, _) =>
        {
            using var d = new ColorDialog { Color = s.Ambient };
            if (d.ShowDialog(this) == DialogResult.OK) { s.Ambient = d.Color; s.RaiseChanged(); }
        };
        p.Controls.Add(btnAmbient);

        var btnDiffuse = new ModernButton { Text = L("ui.color_de_luz_difusa", "Color de luz (difusa)"), Width = 260, Height = 28 };
        btnDiffuse.Click += (_, _) =>
        {
            using var d = new ColorDialog { Color = s.Diffuse };
            if (d.ShowDialog(this) == DialogResult.OK) { s.Diffuse = d.Color; s.RaiseChanged(); }
        };
        p.Controls.Add(btnDiffuse);

        var btnFog = new ModernButton { Text = L("ui.color_de_niebla", "Color de niebla"), Width = 260, Height = 28 };
        btnFog.Click += (_, _) =>
        {
            using var d = new ColorDialog { Color = s.FogColor };
            if (d.ShowDialog(this) == DialogResult.OK) { s.FogColor = d.Color; s.RaiseChanged(); }
        };
        p.Controls.Add(btnFog);

        var btnBg = new ModernButton { Text = L("ui.color_de_fondo", "Color de fondo"), Width = 260, Height = 28 };
        btnBg.Click += (_, _) =>
        {
            using var d = new ColorDialog { Color = s.Background };
            if (d.ShowDialog(this) == DialogResult.OK) { s.Background = d.Color; s.RaiseChanged(); }
        };
        p.Controls.Add(btnBg);

        p.Controls.Add(Header(L("ui.luz_del_terreno", "Luz del terreno")));
        p.Controls.Add(new Label
        {
            AutoSize = false, Width = 265, Height = 46, ForeColor = DarkTheme.TextDim,
            Text = L("ui.recalcula_la_sombra_del_terreno_segun_su", "Recalcula la sombra del terreno segun sus alturas y la guarda en TerrainLight.")
        });
        var btnLightmap = new ModernButton { Text = L("ui.calcular_la_luz_del_mapa", "Calcular la luz del mapa"), IconName = "sun", Width = 266, Height = 32 };
        btnLightmap.Click += (_, _) => CalculateLightmap();
        p.Controls.Add(btnLightmap);

        p.Controls.Add(Header(L("ui.ventana", "Ventana")));
        // por debajo del 80% el texto de los paneles se hace ilegible: se mezcla
        // con lo que haya detras de la ventana. Verificado a mano en 60/80/95.
        p.Controls.Add(Slider(L("ui.opacidad_de_la_ventana", "Opacidad de la ventana"), 80, 100, (int)(_windowOpacity * 100), v =>
        {
            _windowOpacity = v / 100.0;
            Opacity = _windowOpacity;
        }));

        p.Controls.Add(Header(L("ui.atajos", "Atajos")));
        p.Controls.Add(new Label
        {
            AutoSize = false, Width = 270, Height = 190,
            Text = "Boton derecho: girar la camara\r\n" +
                   "Boton del medio: desplazar\r\n" +
                   "Rueda: acercar y alejar\r\n" +
                   "Ctrl + rueda: tamano del pincel\r\n" +
                   "Shift + pincel: efecto inverso\r\n\r\n" +
                   "Ctrl+Z / Ctrl+Y: deshacer y rehacer\r\n" +
                   "Ctrl+S: guardar\r\n" +
                   "Ctrl+C / Ctrl+V: copiar y pegar\r\n" +
                   "Ctrl+D: duplicar\r\n" +
                   "Supr: borrar seleccionados\r\n" +
                   "Ctrl+Shift+clic: nuevo objeto\r\n" +
                   "F: enfocar la seleccion"
        });

        return p;
    }

    /// <summary>
    /// Abre un mapa que esta fuera del cliente: por ejemplo uno descargado. Se
    /// deduce el numero de mundo de los archivos y, si no se puede, se pregunta.
    /// </summary>
    private void OpenLooseWorld()
    {
        using var d = new FolderBrowserDialog
        {
            Description = L("ui.elegi_la_carpeta_del_mapa_la_que_tiene_e", "Elegi la carpeta del mapa (la que tiene EncTerrainN.att, .map y .obj)"),
            UseDescriptionForTitle = true
        };
        if (!string.IsNullOrEmpty(ClientDataPath)) d.SelectedPath = ClientDataPath;
        if (d.ShowDialog(this) != DialogResult.OK) return;

        OpenWorldFolder(d.SelectedPath);
    }

    /// <summary>Abre la carpeta de un mapa, venga de donde venga.</summary>
    private void OpenWorldFolder(string folder)
    {
        if (!MuWorld.LooksLikeWorld(folder))
        {
            Dialogs.Show(this,
                L("msg.sin_archivos_mapa", L("msg.en_esa_carpeta_no_encontre_archivos_de_m", "En esa carpeta no encontre archivos de mapa (.map ni .att).")),
                "MU World Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int? detectado = MuWorld.DetectWorldNumber(folder);
        int numero = detectado ?? AskWorldNumber();
        if (numero < 0) return;

        if (!ConfirmDiscard()) return;

        try
        {
            _view.LoadWorldFolder(folder, numero, ClientDataPath);

            string etiqueta = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar));
            AfterWorldOpened($"{etiqueta}  (mapa {numero})", fromClient: false);

            // lo dejamos elegido en la lista sin volver a cargarlo
            _building = true;
            int idx = _worldBox.Items.Add(new CatalogItem { Id = -1, Text = etiqueta + "  (suelto)" });
            _worldBox.SelectedIndex = idx;
            _building = false;

            RefreshTileList();
            RefreshCatalogCombo();
            RefreshObjectList();
            RefreshTerrainPanel();
            RefreshHistoryButtons();
            AdjustHeightLimits();

            var partes = new List<string> { L("msg.mapa_abierto", "Mapa abierto") + " (" + _view.Ref.World.FormatSummary + ")" };
            if (_view.NewModelsFound > 0)
                partes.Add($"{_view.NewModelsFound} " + L("ui.modelos_nuevos", "modelos nuevos"));
            if (_view.NewTilesFound > 0)
                partes.Add($"{_view.NewTilesFound} " + L("ui.texturas_nuevas", "texturas de piso nuevas"));
            int sinModelo = _view.Catalog?.Entries.Values.Count(x => !x.Exists) ?? 0;
            if (sinModelo > 0)
                partes.Add($"{sinModelo} " + L("ui.sin_modelo_bmd", "tipos sin modelo .bmd"));
            _lblInfo.Text = string.Join("   |   ", partes);
        }
        catch (Exception ex)
        {
            Dialogs.Show(this, "No pude abrir ese mapa: " + ex.Message, "MU World Editor",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Pide el numero de mundo cuando no se puede deducir del archivo.</summary>
    private int AskWorldNumber()
    {
        using var dlg = new Form
        {
            Text = L("ui.numero_de_mapa", "Numero de mapa"),
            Width = 380,
            Height = 190,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            BackColor = DarkTheme.Back,
            ForeColor = DarkTheme.Text,
            Icon = Embedded.AppIcon()
        };

        var lbl = new Label
        {
            Text = L("ui.no_pude_darme_cuenta_de_que_mapa_es", "No pude darme cuenta de que mapa es.") + Environment.NewLine +
                   "Ponele el numero con el que lo va a usar el servidor:",
            Left = 14, Top = 14, Width = 340, Height = 44, ForeColor = DarkTheme.Text
        };
        var num = new NumberBox { Left = 14, Top = 66, Width = 90, Minimum = 0, Maximum = 999, Value = 1 };
        var ok = new ModernButton { Text = L("ui.abrir", "Abrir"), Left = 170, Top = 100, Width = 80, DialogResult = DialogResult.OK };
        var cancel = new ModernButton { Text = L("ui.cancelar", "Cancelar"), Left = 258, Top = 100, Width = 90, DialogResult = DialogResult.Cancel };

        dlg.Controls.AddRange(new Control[] { lbl, num, ok, cancel });
        dlg.AcceptButton = ok;
        dlg.CancelButton = cancel;
        dlg.HandleCreated += (_, _) => DarkTheme.UseDarkTitleBar(dlg.Handle);
        DarkTheme.Apply(dlg);

        return dlg.ShowDialog(this) == DialogResult.OK ? (int)num.Value : -1;
    }

    private void ExportWorldMap()
    {
        var w = _view.Ref.World;
        if (w.Objects.Count == 0 && w.MapPath == null)
        {
            Dialogs.Show(this, L("msg.primero_abri_un_mapa", "Primero abri un mapa."), "MU World Editor");
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = L("ui.guardar_la_imagen_del_mapa", "Guardar la imagen del mapa"),
            Filter = "Imagen PNG|*.png",
            FileName = $"World{w.WorldNumber}_mapa.png"
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        var capa = WorldMapExport.Layer.Tiles;
        if (_view.State.ShowAttributes) capa = WorldMapExport.Layer.Attributes;

        Cursor = Cursors.WaitCursor;
        try
        {
            using var bmp = WorldMapExport.Render(w, _view.Tiles, capa);
            bmp.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png);
            _lblInfo.Text = L("ui.imagen_guardada", "Imagen guardada: ") + Path.GetFileName(dlg.FileName);
        }
        catch (Exception ex)
        {
            Dialogs.Show(this, "No pude guardar la imagen: " + ex.Message, "MU World Editor",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Cursor = Cursors.Default; }
    }

    private void RevertWorld()
    {
        if (_worldBox.SelectedItem is not CatalogItem it) return;
        if (_view.Ref.World.Dirty &&
            Dialogs.Show(this, L("msg.se_pierden_los_cambios_que_no_guardaste_", "Se pierden los cambios que no guardaste. Seguro?"),
                            L("msg.descartar_cambios", "Descartar cambios"), MessageBoxButtons.OKCancel,
                            MessageBoxIcon.Warning) != DialogResult.OK) return;

        _view.Ref.World.Dirty = false;
        _view.LoadWorld(ClientDataPath, it.Id);
        RefreshTileList();
        RefreshCatalogCombo();
        RefreshObjectList();
        RefreshHistoryButtons();
        _lblInfo.Text = L("ui.mapa_recargado_del_disco", "Mapa recargado del disco");
    }

    /// <summary>El boton "Lightmap" del editor original.</summary>
    private void CalculateLightmap()
    {
        var w = _view.Ref.World;
        if (w.LightPath == null && string.IsNullOrEmpty(w.Directory))
        {
            Dialogs.Show(this, L("msg.primero_abri_un_mapa", "Primero abri un mapa."), "MU World Editor");
            return;
        }

        var r = Dialogs.Show(this,
            "Voy a recalcular la luz del terreno con la forma que tiene ahora y guardarla " +
            "en TerrainLight.OZJ.\r\n\r\nEl archivo actual queda copiado en la carpeta _backup.\r\n\r\nSigo?",
            L("msg.calcular_la_luz", "Calcular la luz"), MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (r != DialogResult.OK) return;

        Cursor = Cursors.WaitCursor;
        try
        {
            Lightmap.Calculate(w, new Lightmap.Options());
            Lightmap.Save(w);
            _view.RebuildAll();
            _lblInfo.Text = L("ui.luz_recalculada_y_guardada", "Luz recalculada y guardada");
        }
        catch (Exception ex)
        {
            Dialogs.Show(this, "No pude calcular la luz:\r\n" + ex.Message, "MU World Editor",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Cursor = Cursors.Default; }
    }

    private TabPage _areaPage;
    private Label _areaInfo, _clipInfo;

    /// <summary>Panel de la herramienta Area: copiar un pedazo de mapa y pegarlo.</summary>
    private Control BuildAreaPanel()
    {
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, AutoScroll = true, Padding = new Padding(12, 6, 8, 12)
        };
        p.HandleCreated += (_, _) => Sections.Build(p, "area");
        var s = _view.State;

        p.Controls.Add(Ayuda(L("ui.area_ayuda",
                     "Arrastra sobre el mapa para marcar un rectangulo. Se copia el relieve, los dos pisos, los atributos y los objetos que haya adentro.")));

        _areaInfo = new Label { AutoSize = true, MaximumSize = new Size(258, 0), ForeColor = DarkTheme.Text, Margin = new Padding(0, 2, 0, 6) };
        p.Controls.Add(_areaInfo);

        var fila = new FlowLayoutPanel { Width = 268, Height = 42, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        void B(Control host, string text, Action a, string tip, int ancho)
        {
            string icon = text == L("ui.copiar", "Copiar") ? "copy" : text == L("ui.pegar_aqui", "Pegar aca") ? "paste"
                        : text == L("ui.todo_el_mapa", "Todo") ? "map" : text == L("ui.limpiar", "Limpiar") ? "close" : null;
            var b = new ModernButton { Text = text, IconName = icon, Primary = icon == "paste", Width = ancho, Height = 34, Margin = new Padding(0, 0, 3, 3) };
            if (tip != null) new ToolTip().SetToolTip(b, tip);
            b.Click += (_, _) => { a(); _view.Focus(); };
            host.Controls.Add(b);
        }
        B(fila, L("ui.copiar", "Copiar"), CopyArea, L("ui.area_copiar_tip", "Guarda lo que hay dentro del rectangulo"), 86);
        B(fila, L("ui.todo_el_mapa", "Todo"), SelectAll, L("ui.todo_el_mapa_tip", "Elegir el mapa entero"), 80);
        B(fila, L("ui.limpiar", "Limpiar"), ClearArea, null, 90);
        p.Controls.Add(fila);

        p.Controls.Add(Header(L("ui.lo_copiado", "Lo copiado")));
        _clipInfo = new Label { AutoSize = true, MaximumSize = new Size(258, 0), ForeColor = DarkTheme.Text, Margin = new Padding(0, 2, 0, 6) };
        p.Controls.Add(_clipInfo);

        p.Controls.Add(Header(L("ui.que_se_pega", "Que se pega")));
        void Check(string text, Func<bool> get, Action<bool> set, string tip)
        {
            var c = new ModernCheckBox { Text = text, Checked = get(), AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            c.CheckedChanged += (_, _) => set(c.Checked);
            if (tip != null) new ToolTip().SetToolTip(c, tip);
            p.Controls.Add(c);
        }
        Check(L("ui.pegar_terreno", "El relieve"), () => s.Paste.Terrain, v => s.Paste.Terrain = v, null);
        Check(L("ui.pegar_tiles", "Los pisos"), () => s.Paste.Tiles, v => s.Paste.Tiles = v, null);
        Check(L("ui.pegar_att", "Los atributos"), () => s.Paste.Attributes, v => s.Paste.Attributes = v, null);
        Check(L("ui.pegar_objetos", "Los objetos"), () => s.Paste.Objects, v => s.Paste.Objects = v, null);
        Check(L("ui.pegar_luz", "La luz del terreno"), () => s.Paste.Light, v => s.Paste.Light = v,
              L("ui.pegar_luz_tip", "Normalmente conviene dejar la del mapa destino"));

        var filaPegar = new FlowLayoutPanel { Width = 268, Height = 46, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        B(filaPegar, L("ui.pegar_aqui", "Pegar aca"), PasteArea, L("ui.pegar_aqui_tip", "Pega donde tengas el mouse sobre el mapa"), 140);
        p.Controls.Add(filaPegar);

        p.Controls.Add(Ayuda(L("ui.area_otro_mapa",
                     "Podes abrir otro mapa y pegar ahi: lo copiado no se pierde al cambiar de mapa. Los modelos y las texturas que falten se copian solos a la carpeta del mapa destino. Si ya hay un archivo con ese nombre y es distinto, el nuevo se guarda como nombre2.")));

        RefreshAreaInfo();
        return p;
    }

    /// <summary>
    /// Texto de ayuda que crece solo hacia abajo. Con alto fijo se cortaba la
    /// mitad de la explicacion.
    /// </summary>
    private static Label Ayuda(string texto) => new()
    {
        Text = texto,
        AutoSize = true,
        MaximumSize = new Size(258, 0),
        ForeColor = DarkTheme.TextDim,
        Margin = new Padding(0, 2, 0, 6)
    };

    private void SelectAll()
    {
        _view.SelectWholeMap();
        RefreshAreaInfo();
    }

    private void ClearArea()
    {
        _view.State.ClearArea();
        _view.Invalidate();
        RefreshAreaInfo();
    }

    private void RefreshAreaInfo()
    {
        if (_areaInfo == null) return;
        var s = _view.State;

        if (!s.HasArea)
        {
            _areaInfo.Text = L("ui.area_sin_marcar", "Sin rectangulo marcado");
        }
        else
        {
            var (x0, y0, x1, y1) = s.AreaOrdered();
            string celdas = L("ui.celdas", "celdas");
            _areaInfo.Text = L("ui.area_marcada", "Rectangulo") + ": " +
                             (x1 - x0 + 1) + " x " + (y1 - y0 + 1) + " " + celdas +
                             "   [" + x0 + "," + y0 + "] - [" + x1 + "," + y1 + "]";
        }

        if (_clipInfo != null)
        {
            _clipInfo.Text = s.Clip == null || s.Clip.IsEmpty
                ? L("ui.nada_copiado", "Todavia no copiaste nada")
                : s.Clip.Describe() + Environment.NewLine +
                  L("ui.desde", "desde") + " " + Path.GetFileName(s.Clip.SourceFolder ?? "");
        }
    }

    private void CopyArea()
    {
        var clip = _view.CopyArea();
        if (clip == null)
        {
            Dialogs.Show(this, L("msg.marca_un_area", "Primero marca un rectangulo arrastrando sobre el mapa."),
                            "MU World Editor");
            return;
        }
        RefreshAreaInfo();
        _lblInfo.Text = L("ui.copiado", "Copiado") + ": " + clip.Describe();
    }

    private void PasteArea()
    {
        var s = _view.State;
        if (s.Clip == null || s.Clip.IsEmpty)
        {
            Dialogs.Show(this, L("msg.nada_para_pegar", "Todavia no copiaste ningun area."), "MU World Editor");
            return;
        }

        Cursor = Cursors.WaitCursor;
        try
        {
            var report = _view.PasteAreaAtCursor();
            if (report == null)
            {
                _lblInfo.Text = L("msg.nada_que_pegar", "No habia nada para pegar ahi");
                return;
            }

            RefreshObjectList();
            RefreshCatalogCombo();
            RefreshTileList();
            RefreshHistoryButtons();
            _lblInfo.Text = L("ui.pegado", "Pegado") + ": " + report.Describe();

            if (report.CopiedFiles.Count > 0 || report.Renamed.Count > 0)
            {
                var texto = new System.Text.StringBuilder();
                texto.AppendLine(L("msg.pegado_ok", "Se pego el area en este mapa."));
                texto.AppendLine();
                if (report.CopiedFiles.Count > 0)
                {
                    texto.AppendLine(L("msg.archivos_copiados", "Archivos copiados a la carpeta del mapa:"));
                    foreach (var f in report.CopiedFiles.Take(15)) texto.AppendLine("   " + f);
                    if (report.CopiedFiles.Count > 15)
                        texto.AppendLine("   ... y " + (report.CopiedFiles.Count - 15) + " mas");
                }
                if (report.Renamed.Count > 0)
                {
                    texto.AppendLine();
                    texto.AppendLine(L("msg.archivos_renombrados", "Ya existia uno con ese nombre, se guardaron como:"));
                    foreach (var r in report.Renamed.Take(10)) texto.AppendLine("   " + r);
                }
                Dialogs.Show(this, texto.ToString(), "MU World Editor",
                                MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            Dialogs.Show(this, "No pude pegar: " + ex.Message, "MU World Editor",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Cursor = Cursors.Default; }
    }

    private Control BuildObjectPanel()
    {
        // una sola columna con barra de desplazamiento: con alturas fijas los
        // controles de abajo quedan tapados en pantallas chicas
        var p = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(12, 6, 8, 12)
        };
        p.HandleCreated += (_, _) => Sections.Build(p, "obj");

        // ---------------------------------------------------- herramienta
        p.Controls.Add(Header(L("ui.herramienta", "Herramienta")));

        var gizmo = new FlowLayoutPanel { Width = 268, Height = 42, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        foreach (var (mode, text) in new[]
                 {
                     (GizmoMode.Move,   L("ui.mover", "Mover")),
                     (GizmoMode.Rotate, L("ui.rotar", "Rotar")),
                     (GizmoMode.Scale,  L("ui.escalar", "Escalar"))
                 })
        {
            var rb = new ModernRadio
            {
                Text = text, Appearance = Appearance.Button,
                IconName = mode switch { GizmoMode.Move => "pan", GizmoMode.Rotate => "rotate", _ => "scale" },
                Width = 86, Height = 34,
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 0, 3, 0),
                Checked = _view.State.Gizmo == mode
            };
            var destino = mode;
            rb.CheckedChanged += (_, _) => { if (rb.Checked) _view.State.Gizmo = destino; };
            gizmo.Controls.Add(rb);
        }
        p.Controls.Add(gizmo);

        // ---------------------------------------------------------- pegar
        p.Controls.Add(Header(L("ui.pegar_a", "Pegar a:")));

        var chkGround = new ModernCheckBox { Text = L("ui.el_piso", "El piso"), Checked = _view.State.SnapToGround, AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
        chkGround.CheckedChanged += (_, _) => _view.State.SnapToGround = chkGround.Checked;
        p.Controls.Add(chkGround);

        var gridRow = new FlowLayoutPanel { Width = 268, Height = 34, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        var chkGrid = new ModernCheckBox { Text = L("ui.la_grilla", "La grilla"), Checked = _view.State.SnapToGrid, AutoSize = true, Margin = new Padding(0, 5, 6, 0) };
        chkGrid.CheckedChanged += (_, _) => _view.State.SnapToGrid = chkGrid.Checked;
        var numGrid = new NumberBox { Width = 70, Minimum = 1, Maximum = 1000, Increment = 10, Value = (decimal)_view.State.GridSnapSize };
        numGrid.ValueChanged += (_, _) => _view.State.GridSnapSize = (float)numGrid.Value;
        gridRow.Controls.Add(chkGrid);
        gridRow.Controls.Add(new Label { Text = L("ui.paso", "Paso:"), AutoSize = true, Margin = new Padding(0, 7, 2, 0) });
        gridRow.Controls.Add(numGrid);
        p.Controls.Add(gridRow);

        var snapRow = new FlowLayoutPanel { Width = 268, Height = 34, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0, 0, 0, 4) };
        var chkRot = new ModernCheckBox { Text = L("ui.giro", "Giro"), Checked = _view.State.SnapRotation, AutoSize = true, Margin = new Padding(0, 5, 3, 0) };
        chkRot.CheckedChanged += (_, _) => _view.State.SnapRotation = chkRot.Checked;
        var numRot = new NumberBox { Width = 48, Minimum = 1, Maximum = 180, Value = (decimal)_view.State.RotateSnapDegrees };
        numRot.ValueChanged += (_, _) => _view.State.RotateSnapDegrees = (float)numRot.Value;
        var chkScale = new ModernCheckBox { Text = L("ui.escala", "Escala"), Checked = _view.State.SnapScale, AutoSize = true, Margin = new Padding(6, 5, 2, 0) };
        chkScale.CheckedChanged += (_, _) => _view.State.SnapScale = chkScale.Checked;
        var numScale = new NumberBox { Width = 52, Minimum = 0.01m, Maximum = 5m, DecimalPlaces = 2, Increment = 0.05m, Value = (decimal)_view.State.ScaleSnapStep };
        numScale.ValueChanged += (_, _) => _view.State.ScaleSnapStep = (float)numScale.Value;
        snapRow.Controls.Add(chkRot); snapRow.Controls.Add(numRot);
        snapRow.Controls.Add(chkScale); snapRow.Controls.Add(numScale);
        p.Controls.Add(snapRow);

        // ------------------------------------------- posicion, giro, escala
        p.Controls.Add(BuildTransformBox());

        // ----------------------------------------------------- objeto nuevo
        p.Controls.Add(Header(L("ui.objeto_nuevo", "Objeto nuevo")));

        _newObjectBox = new ComboBox { Width = 268, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 4) };
        _newObjectBox.SelectedIndexChanged += (_, _) =>
        {
            if (_newObjectBox.SelectedItem is not CatalogItem it) return;
            _view.State.NewObjectType = it.Id;
            // se muestra siempre: es el objeto que estas por poner
            ShowPreview(it.Id);
        };
        p.Controls.Add(_newObjectBox);

        p.Controls.Add(SampleButton(L("ui.tomar_un_objeto_del_mapa", "Tomar un objeto del mapa"), SampleMode.ObjectType,
                                    L("ui.tomar_objeto_tip", "Hace clic en un objeto y usa ese modelo para los nuevos")));

        var btnAdd = new ModernButton { Text = L("ui.agregar_en_el_centro_de_la_vista", "Agregar en el centro de la vista"), IconName = "plus", Primary = true, Width = 268, Height = 34, Margin = new Padding(0, 2, 0, 4) };
        btnAdd.Click += (_, _) => { _view.AddObjectAtCursor(); _view.Focus(); };
        p.Controls.Add(btnAdd);

        // ------------------------------------------------- acciones sobre lo elegido
        p.Controls.Add(Header(L("ui.sobre_lo_elegido", "Sobre lo elegido")));

        var buttons = new FlowLayoutPanel { Width = 268, Height = 120, FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Margin = new Padding(0, 0, 0, 4) };
        void B(string text, Action a, string tip = null)
        {
            string icon = text == L("ui.duplicar", "Duplicar") ? "duplicate" : text == L("ui.borrar", "Borrar") ? "trash"
                        : text == L("ui.al_piso", "Al piso") ? "ground" : text == L("ui.copiar", "Copiar") ? "copy"
                        : text == L("ui.pegar", "Pegar") ? "paste" : text == L("ui.enfocar", "Enfocar") ? "focus"
                        : text == L("ui.aspecto", "Aspecto") ? "image" : null;
            var b = new ModernButton { Text = text, IconName = icon, Danger = icon == "trash", Width = 87, Height = 34, Margin = new Padding(0, 0, 2, 3) };
            if (tip != null) new ToolTip().SetToolTip(b, tip);
            b.Click += (_, _) => { a(); _view.Focus(); };
            buttons.Controls.Add(b);
        }
        B(L("ui.duplicar", "Duplicar"), _view.DuplicateSelection);
        B(L("ui.borrar", "Borrar"), _view.DeleteSelection);
        B(L("ui.al_piso", "Al piso"), _view.DropSelectionToGround);
        B(L("ui.copiar", "Copiar"), _view.CopySelection);
        B(L("ui.pegar", "Pegar"), _view.PasteClipboard);
        B(L("ui.enfocar", "Enfocar"), _view.FocusOnSelection);
        B(L("ui.aspecto", "Aspecto"), ReplaceSelectedLook, L("ui.aspecto_tip", "Cambiar la imagen o el modelo del objeto"));
        B("< " + L("ui.apilado_corto", "Apilado"), () => _view.CycleDuplicate(-1), L("ui.apilado_tip", "Pasar al objeto anterior que este en el mismo lugar"));
        B(L("ui.apilado_corto", "Apilado") + " >", () => _view.CycleDuplicate(1), L("ui.apilado_tip", "Pasar al siguiente objeto que este en el mismo lugar"));
        p.Controls.Add(buttons);

        _preview = new PictureBox
        {
            Width = 268, Height = 150,
            SizeMode = PictureBoxSizeMode.CenterImage,
            BackColor = DarkTheme.Sunken,
            BorderStyle = BorderStyle.None,
            Margin = new Padding(0, 0, 0, 4)
        };
        new ToolTip().SetToolTip(_preview, L("ui.vista_previa_tip", "Como se ve el objeto elegido"));
        p.Controls.Add(_preview);

        _selInfo = new Label
        {
            Width = 268, Height = 104, AutoSize = false,
            Text = L("msg.nada_seleccionado", "Nada seleccionado"),
            Margin = new Padding(0, 0, 0, 6)
        };
        p.Controls.Add(_selInfo);

        // ----------------------------------------------------------- lista
        p.Controls.Add(Header(L("ui.lista_de_objetos", "Lista de objetos")));

        var buscar = new FlowLayoutPanel { Width = 268, Height = 36, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Margin = new Padding(0) };
        _objFilter = new TextBox { Width = 164, Margin = new Padding(0, 3, 4, 0), PlaceholderText = L("ui.buscar", "Buscar nombre o tipo...") };
        _objFilter.TextChanged += (_, _) => RefreshObjectList();
        var btnMismoTipo = new ModernButton { Text = L("ui.mismo_tipo", "Igual tipo"), IconName = "layers", Width = 98, Height = 28 };
        new ToolTip().SetToolTip(btnMismoTipo, L("ui.mismo_tipo_tip", "Selecciona todos los objetos del mismo tipo que el elegido"));
        btnMismoTipo.Click += (_, _) => SelectSameType();
        buscar.Controls.Add(_objFilter);
        buscar.Controls.Add(btnMismoTipo);
        p.Controls.Add(buscar);

        _objList = new ListBox
        {
            Width = 268, Height = 360,
            SelectionMode = SelectionMode.MultiExtended,
            IntegralHeight = false,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 40,
            BorderStyle = BorderStyle.None,
            BackColor = DarkTheme.Sunken
        };
        _objList.DrawItem += DrawObjectItem;
        _objList.SelectedIndexChanged += (_, _) =>
        {
            if (_building) return;
            _view.State.Selection.Clear();
            foreach (var item in _objList.SelectedItems)
                if (item is ObjectItem oi) _view.State.Selection.Add(oi.Obj);
            _view.Invalidate();
            UpdateSelectionLabel();
            RefreshTransformBox();
        };
        p.Controls.Add(_objList);

        return p;
    }

    private NumberBox _posX, _posY, _posZ, _rotX, _rotY, _rotZ, _numScale;

    /// <summary>Posicion, giro y escala del objeto seleccionado, escribibles a mano.</summary>
    private Control BuildTransformBox()
    {
        var host = new Panel { Width = 268, Height = 178, Margin = new Padding(0, 4, 0, 4) };

        NumberBox Num(int x, int y, string label, decimal min, decimal max, int decimals, Action<decimal> set)
        {
            host.Controls.Add(new Label { Text = label, Left = x, Top = y + 4, Width = 16, AutoSize = false });
            var n = new NumberBox
            {
                Left = x + 16, Top = y, Width = 92, Height = 26,
                Minimum = min, Maximum = max, DecimalPlaces = decimals,
                Increment = decimals == 0 ? 10 : 0.1m
            };
            n.ValueChanged += (_, _) => { if (!_building) set(n.Value); };
            host.Controls.Add(n);
            return n;
        }

        host.Controls.Add(new Label { Text = L("ui.posicion", "Posicion"), Left = 0, Top = 0, AutoSize = true, ForeColor = DarkTheme.TextDim, Font = DarkTheme.UiFontBold });
        host.Controls.Add(new Label { Text = L("ui.giro", "Giro"), Left = 140, Top = 0, AutoSize = true, ForeColor = DarkTheme.TextDim, Font = DarkTheme.UiFontBold });

        _posX = Num(0, 18, "X", -100000, 100000, 0, v => ApplyTransform(o => o.X = (float)v));
        _posY = Num(0, 48, "Y", -100000, 100000, 0, v => ApplyTransform(o => o.Y = (float)v));
        _posZ = Num(0, 78, "Z", -100000, 100000, 0, v => ApplyTransform(o => o.Z = (float)v));

        _rotX = Num(136, 18, "X", -3600, 3600, 0, v => ApplyTransform(o => o.AngleX = (float)v));
        _rotY = Num(136, 48, "Y", -3600, 3600, 0, v => ApplyTransform(o => o.AngleY = (float)v));
        _rotZ = Num(136, 78, "Z", -3600, 3600, 0, v => ApplyTransform(o => o.AngleZ = (float)v));

        host.Controls.Add(new Label { Text = L("ui.escala", "Escala"), Left = 0, Top = 112, AutoSize = true, ForeColor = DarkTheme.TextDim, Font = DarkTheme.UiFontBold });
        _numScale = Num(0, 134, " ", 0.05m, 20m, 2, v => ApplyTransform(o => o.Scale = (float)v));

        var btnGround = new ModernButton { Text = L("ui.al_piso", "Al piso"), IconName = "ground", Left = 136, Top = 134, Width = 130, Height = 28 };
        btnGround.Click += (_, _) => { _view.DropSelectionToGround(); RefreshSelection(); _view.Focus(); };
        host.Controls.Add(btnGround);

        return host;
    }

    /// <summary>
    /// Boton de cuentagotas: el proximo clic en el mapa toma el valor que haya
    /// ahi (altura, tile o tipo de objeto) en vez de pintar.
    /// </summary>
    private Button SampleButton(string text, SampleMode mode, string tip)
    {
        var b = new ModernButton { Text = text, IconName = "picker", Width = 268, Height = 34 };
        new ToolTip().SetToolTip(b, tip);
        b.Click += (_, _) =>
        {
            _view.State.Sample = mode;
            _view.Cursor = Cursors.Cross;
            _lblInfo.Text = L("ui.cuentagotas_hace_clic_en_el_mapa", "Cuentagotas: hace clic en el mapa");
            _view.Focus();
        };
        return b;
    }

    /// <summary>Aplica un cambio a todos los seleccionados y lo deja en el historial.</summary>
    private void ApplyTransform(Action<MapFiles.MapObject> change)
    {
        var sel = _view.State.Selection;
        if (sel.Count == 0) return;

        var items = new List<(MapFiles.MapObject, TransformObjects.State, TransformObjects.State)>();
        foreach (var o in sel)
        {
            var before = new TransformObjects.State(o);
            change(o);
            items.Add((o, before, new TransformObjects.State(o)));
        }
        _view.History.Push(new TransformObjects("Cambiar objeto", items));
        _view.Ref.World.Dirty = true;
        _view.Invalidate();
    }

    private void RefreshTransformBox()
    {
        var sel = _view.State.Selection;
        bool one = sel.Count > 0;
        foreach (var n in new[] { _posX, _posY, _posZ, _rotX, _rotY, _rotZ, _numScale })
            if (n != null) n.Enabled = one;

        if (!one)
        {
            // sin seleccion los dejamos en cero: si no quedan los numeros del
            // objeto anterior y parece que hay algo elegido cuando no lo hay
            _building = true;
            foreach (var n in new[] { _posX, _posY, _posZ, _rotX, _rotY, _rotZ })
                if (n != null) n.Value = 0;
            if (_numScale != null) _numScale.Value = _numScale.Minimum;
            _building = false;
            return;
        }
        if (_posX == null) return;

        var o = sel[0];
        _building = true;
        _posX.Value = Clamp(_posX, (decimal)o.X);
        _posY.Value = Clamp(_posY, (decimal)o.Y);
        _posZ.Value = Clamp(_posZ, (decimal)o.Z);
        _rotX.Value = Clamp(_rotX, (decimal)o.AngleX);
        _rotY.Value = Clamp(_rotY, (decimal)o.AngleY);
        _rotZ.Value = Clamp(_rotZ, (decimal)o.AngleZ);
        _numScale.Value = Clamp(_numScale, (decimal)o.Scale);
        _building = false;
    }

    private static decimal Clamp(NumberBox n, decimal v) => Math.Clamp(v, n.Minimum, n.Maximum);

    private sealed class CatalogItem
    {
        public int Id; public string Text;
        public override string ToString() => Text;
    }

    private sealed class ObjectItem
    {
        public MapFiles.MapObject Obj;
        public int Index;
        public string Text;
        public override string ToString() => Text;
    }

    private static Label Header(string text) => Sections.Header(text);

    private Control Slider(string label, int min, int max, int value, Action<int> set)
    {
        var panel = new Panel { Width = 275, Height = 46, Margin = new Padding(0, 2, 0, 2), BackColor = DarkTheme.Surface };
        var lbl = new Label { Text = label + ": " + value, AutoSize = true, Top = 0, Left = 0, ForeColor = DarkTheme.TextDim };
        var tb = new DarkSlider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 268, Top = 18, Left = 0 };
        tb.ValueChanged += v => { lbl.Text = label + ": " + v; set(v); };
        panel.Controls.Add(lbl);
        panel.Controls.Add(tb);
        return panel;
    }

    // --------------------------------------------------- panel de terreno
    private void RefreshTerrainPanel()
    {
        _terrainPanel.SuspendLayout();
        _terrainPanel.Controls.Clear();

        var p = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(12, 6, 8, 12) };
        var s = _view.State;
        string sectionKey = "terreno-" + s.Mode;
        p.HandleCreated += (_, _) => Sections.Build(p, sectionKey);

        // las cuatro herramientas, dentro de la propia pestana: asi se usa sola,
        // sin depender de los botones de la barra de arriba
        p.Controls.Add(Header(L("ui.herramienta", "Herramienta")));
        // se acomodan en dos filas si el panel esta angosto
        var fila = new FlowLayoutPanel
        {
            Width = 272, Height = 74, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, Margin = new Padding(0, 0, 0, 6)
        };
        foreach (var (modo, texto) in new[]
                 {
                     (EditMode.Height,    L("ui.altura", "Altura")),
                     (EditMode.Attribute, L("ui.atributos", "Atributos")),
                     (EditMode.Tile,      L("ui.tiles", "Tiles")),
                     (EditMode.Alpha,     L("ui.transparencia", "Transparencia"))
                 })
        {
            var rb = new ModernRadio
            {
                Text = texto,
                Appearance = Appearance.Button,
                IconName = modo switch { EditMode.Height => "mountain", EditMode.Attribute => "flag", EditMode.Tile => "tiles", _ => "blend" },
                AutoSize = false,
                Size = new Size(130, 32),
                TextAlign = ContentAlignment.MiddleCenter,
                Margin = new Padding(0, 0, 4, 4),
                Checked = s.Mode == modo
            };
            var destino = modo;
            rb.CheckedChanged += (_, _) => { if (rb.Checked && !_building) SetMode(destino); };
            fila.Controls.Add(rb);
        }
        p.Controls.Add(fila);

        if (s.Mode is EditMode.View or EditMode.Objects)
        {
            p.Controls.Add(new Label
            {
                Text = L("ui.elegi_herramienta", "Elegi una de las cuatro herramientas de arriba."),
                AutoSize = false, Width = 265, Height = 40, ForeColor = DarkTheme.TextDim
            });
            _terrainPanel.Controls.Add(p);
            DarkTheme.Apply(_terrainPanel);
            _terrainPanel.ResumeLayout();
            return;
        }

        p.Controls.Add(Header(L("ui.pincel", "Pincel")));
        _radius = AddBrushSizeSlider(p, s);
        _hardness = AddSlider(p, L("ui.dureza_del_borde", "Dureza del borde"), 0, 100, (int)(s.BrushHardness * 100), v => s.BrushHardness = v / 100f);
        _strength = AddSlider(p, L("ui.fuerza", "Fuerza"), 1, 100, (int)(s.BrushStrength * 100), v => s.BrushStrength = v / 100f);

        switch (s.Mode)
        {
            case EditMode.Height: BuildHeightTools(p, s); break;
            case EditMode.Attribute: BuildAttributeTools(p, s); break;
            case EditMode.Tile: BuildTileTools(p, s); break;
            case EditMode.Alpha: BuildAlphaTools(p, s); break;
        }

        _terrainPanel.Controls.Add(p);
        DarkTheme.Apply(_terrainPanel);
        _terrainPanel.ResumeLayout();
    }

    /// <summary>
    /// Deslizador del tamano del pincel. Va de a media celda (el minimo pinta
    /// una sola) y en vez del radio muestra cuantas celdas agarra, que es lo
    /// que uno mira. El valor del control es el radio por dos, porque el
    /// deslizador trabaja con enteros.
    /// </summary>
    private DarkSlider AddBrushSizeSlider(Control parent, EditorState s)
    {
        var panel = new Panel { Width = 275, Height = 46, BackColor = DarkTheme.Surface };
        var lbl = new Label { Text = "", AutoSize = true, ForeColor = DarkTheme.TextDim };
        var tb = new DarkSlider
        {
            Minimum = 1,                                   // 0,5 celdas de radio: una sola celda
            Maximum = 60,                                  // 30 celdas de radio
            Value = Math.Clamp((int)Math.Round(s.BrushRadius * 2), 1, 60),
            Width = 268, Top = 18
        };

        void Refresh(int valor)
        {
            float radio = valor / 2f;
            lbl.Text = L("ui.tamano", "Tamano") + ": " + BrushSizeText(radio);
        }

        tb.ValueChanged += v =>
        {
            s.BrushRadius = v / 2f;
            Refresh(v);
            _view.State.RaiseChanged();
        };
        Refresh(tb.Value);

        panel.Controls.Add(lbl);
        panel.Controls.Add(tb);
        parent.Controls.Add(panel);
        return tb;
    }

    /// <summary>Cuantas celdas agarra el pincel con ese radio, dicho en criollo.</summary>
    private string BrushSizeText(float radio)
    {
        int lado = 0, total = 0;
        int limite = (int)Math.Ceiling(radio) + 1;
        for (int y = -limite; y <= limite; y++)
            for (int x = -limite; x <= limite; x++)
            {
                double d = Math.Sqrt(x * x + y * y);
                if (d > radio) continue;
                total++;
                lado = Math.Max(lado, Math.Abs(x) * 2 + 1);
            }

        if (total <= 1) return L("ui.una_celda", "1 celda");
        return string.Format(L("ui.celdas_lado", "{0} celdas (unas {1}x{1})"), total, lado);
    }

    private DarkSlider AddSlider(Control parent, string label, int min, int max, int value, Action<int> set)
    {
        var panel = new Panel { Width = 275, Height = 46, BackColor = DarkTheme.Surface };
        var lbl = new Label { Text = label + ": " + value, AutoSize = true, ForeColor = DarkTheme.TextDim };
        var tb = new DarkSlider { Minimum = min, Maximum = max, Value = Math.Clamp(value, min, max), Width = 268, Top = 18 };
        tb.ValueChanged += v => { lbl.Text = label + ": " + v; set(v); _view.State.RaiseChanged(); };
        panel.Controls.Add(lbl);
        panel.Controls.Add(tb);
        parent.Controls.Add(panel);
        return tb;
    }

    private void BuildHeightTools(Control p, EditorState s)
    {
        p.Controls.Add(Header(L("ui.modo", "Modo")));
        _heightMode = new ComboBox { Width = 270, DropDownStyle = ComboBoxStyle.DropDownList };
        _heightMode.Items.AddRange(new object[] { L("ui.levantar_hundir", "Levantar / hundir"), L("ui.suavizar", "Suavizar"), L("ui.aplanar", "Aplanar hacia un valor"), L("ui.fijar_valor", "Fijar el valor exacto") });
        _heightMode.SelectedIndex = (int)s.Height;
        _heightMode.SelectedIndexChanged += (_, _) => { s.Height = (HeightMode)_heightMode.SelectedIndex; };
        p.Controls.Add(_heightMode);

        p.Controls.Add(new Label { Text = L("ui.con_shift_apretado_hunde_en_vez_de_levan", "Con Shift apretado hunde en vez de levantar."), AutoSize = true, Width = 270 });

        p.Controls.Add(Header(L("ui.altura_objetivo", "Altura objetivo")));
        _numTarget = new NumberBox { Minimum = -500, Maximum = 20000, Value = (decimal)s.FlattenTarget, Width = 120, DecimalPlaces = 0 };
        _numTarget.ValueChanged += (_, _) => s.FlattenTarget = (float)_numTarget.Value;
        p.Controls.Add(_numTarget);

        p.Controls.Add(SampleButton(L("ui.tomar_la_altura_del_mapa", "Tomar la altura del mapa"), SampleMode.Height,
                                    "Hace clic en el terreno y copia esa altura aca"));

        p.Controls.Add(Header(L("ui.limites", "Limites")));
        _numMin = new NumberBox { Minimum = -500, Maximum = 20000, Value = (decimal)s.HeightMin, Width = 120 };
        _numMin.ValueChanged += (_, _) => s.HeightMin = (float)_numMin.Value;
        _numMax = new NumberBox { Minimum = -500, Maximum = 20000, Value = (decimal)s.HeightMax, Width = 120 };
        _numMax.ValueChanged += (_, _) => s.HeightMax = (float)_numMax.Value;
        p.Controls.Add(new Label { Text = L("ui.minimo", "Minimo"), AutoSize = true });
        p.Controls.Add(_numMin);
        p.Controls.Add(new Label { Text = L("ui.maximo", "Maximo"), AutoSize = true });
        p.Controls.Add(_numMax);
    }

    private void BuildAttributeTools(Control p, EditorState s)
    {
        p.Controls.Add(Header(L("ui.que_hace_el_pincel", "Que hace el pincel")));
        var row = new FlowLayoutPanel { Width = 275, Height = 30 };
        _rbAttAdd = new ModernRadio { Text = L("ui.poner", "Poner"), Appearance = Appearance.Button, Width = 100, Height = 30, Checked = s.AttributeAction == AttributeMode.Add };
        _rbAttDel = new ModernRadio { Text = L("ui.sacar", "Sacar"), Appearance = Appearance.Button, Width = 100, Height = 30, Checked = s.AttributeAction == AttributeMode.Remove };
        _rbAttAdd.CheckedChanged += (_, _) => { if (_rbAttAdd.Checked) s.AttributeAction = AttributeMode.Add; };
        _rbAttDel.CheckedChanged += (_, _) => { if (_rbAttDel.Checked) s.AttributeAction = AttributeMode.Remove; };
        row.Controls.Add(_rbAttAdd); row.Controls.Add(_rbAttDel);
        p.Controls.Add(row);

        p.Controls.Add(Header(L("ui.banderas", "Banderas")));
        _attList = new CheckedListBox { Width = 270, Height = 340, CheckOnClick = true, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 22, BorderStyle = BorderStyle.None, BackColor = DarkTheme.Sunken };
        foreach (var a in AttributeInfo.All)
        {
            int idx = _attList.Items.Add(a.Label + "   (0x" + ((ushort)a.Flag).ToString("X4") + ")");
            _attList.SetItemChecked(idx, (s.AttributeFlags & a.Flag) != 0);
        }
        _attList.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? DarkTheme.Hover : DarkTheme.Sunken)) g.FillRectangle(bg, e.Bounds);
            var info = AttributeInfo.All[e.Index];
            bool ticked = _attList.GetItemChecked(e.Index);

            var box = new RectangleF(e.Bounds.Left + 4, e.Bounds.Top + 3, 16, 16);
            using (var path = DarkTheme.Round(box, 4))
            {
                if (ticked) using (var b = new SolidBrush(DarkTheme.Accent)) g.FillPath(b, path);
                else using (var pen = new Pen(DarkTheme.Border, 1.2f)) g.DrawPath(pen, path);
            }
            if (ticked)
                using (var pen = new Pen(DarkTheme.AccentText, 2f))
                    g.DrawLines(pen, new[] { new PointF(box.X + 4, box.Y + 8.3f), new PointF(box.X + 6.8f, box.Y + 11.2f), new PointF(box.X + 12, box.Y + 5) });

            var swatch = new RectangleF(e.Bounds.Left + 28, e.Bounds.Top + 5, 12, 12);
            using (var path = DarkTheme.Round(swatch, 3))
            using (var b = new SolidBrush(info.Color)) g.FillPath(b, path);

            var txt = _attList.Items[e.Index].ToString();
            TextRenderer.DrawText(g, txt, DarkTheme.UiFont, new Rectangle(e.Bounds.Left + 48, e.Bounds.Top, e.Bounds.Width - 50, e.Bounds.Height),
                                  ticked ? DarkTheme.Text : DarkTheme.TextDim,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        };
        _attList.MouseDown += (_, me) =>
        {
            int i = _attList.IndexFromPoint(me.Location);
            if (i >= 0 && me.X < 20) _attList.SetItemChecked(i, !_attList.GetItemChecked(i));
        };
        _attList.ItemCheck += (_, e) =>
        {
            BeginInvoke(() =>
            {
                TerrainFlag f = 0;
                for (int i = 0; i < _attList.Items.Count; i++)
                    if (_attList.GetItemChecked(i)) f |= AttributeInfo.All[i].Flag;
                s.AttributeFlags = f;
            });
        };
        p.Controls.Add(_attList);

        var legend = new Label
        {
            AutoSize = false, Width = 275, Height = 90,
            Text = L("ui.el_color_de_cada_bandera_es_el_que_se_pi", "El color de cada bandera es el que se pinta sobre el mapa.") + Environment.NewLine +
                   "Activa \"Marcas de atributo\" en la pestana Ver (o tecla T)." + Environment.NewLine +
                   "Si una celda tiene varias, se ve la primera de la lista."
        };
        p.Controls.Add(legend);
    }

    private void BuildTileTools(Control p, EditorState s)
    {
        p.Controls.Add(Header(L("ui.capa", "Capa")));
        var row = new FlowLayoutPanel { Width = 275, Height = 30 };
        _rbLayer1 = new ModernRadio { Text = L("ui.capa_1", "Capa 1"), Appearance = Appearance.Button, Width = 100, Height = 30, Checked = s.TileLayer == 1 };
        _rbLayer2 = new ModernRadio { Text = L("ui.capa_2", "Capa 2"), Appearance = Appearance.Button, Width = 100, Height = 30, Checked = s.TileLayer == 2 };
        _rbLayer1.CheckedChanged += (_, _) => { if (_rbLayer1.Checked) s.TileLayer = 1; };
        _rbLayer2.CheckedChanged += (_, _) => { if (_rbLayer2.Checked) s.TileLayer = 2; };
        row.Controls.Add(_rbLayer1); row.Controls.Add(_rbLayer2);
        p.Controls.Add(row);

        p.Controls.Add(Header(L("ui.textura", "Textura")));
        p.Controls.Add(SampleButton(L("ui.tomar_el_tile_del_mapa", "Tomar el tile del mapa"), SampleMode.Tile,
                                    "Hace clic en el piso y copia esa textura aca"));
        _tilePreview = new PictureBox
        {
            Width = 268, Height = 130,
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = DarkTheme.Sunken,
            BorderStyle = BorderStyle.None,
            Margin = new Padding(0, 0, 0, 4)
        };
        new ToolTip().SetToolTip(_tilePreview, L("ui.vista_previa_tile", "Como se ve la textura elegida"));
        p.Controls.Add(_tilePreview);

        _tileList = new ListBox
        {
            Width = 268, Height = 320, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 36,
            BorderStyle = BorderStyle.None, BackColor = DarkTheme.Sunken, IntegralHeight = false
        };
        _tileList.DrawItem += DrawTileItem;
        RefreshTileList();
        _tileList.SelectedIndexChanged += (_, _) =>
        {
            if (_tileList.SelectedItem is not CatalogItem it) return;
            s.TileId = it.Id;
            ShowTilePreview(it.Id);
        };
        p.Controls.Add(_tileList);
        ShowTilePreview(s.TileId);
    }

    private void BuildAlphaTools(Control p, EditorState s)
    {
        p.Controls.Add(Header(L("ui.que_hace_el_pincel", "Que hace el pincel")));
        var row = new FlowLayoutPanel { Width = 275, Height = 30 };
        _rbAlphaAdd = new ModernRadio { Text = L("ui.mostrar_capa_2", "Mostrar capa 2"), Appearance = Appearance.Button, Width = 130, Height = 30, Checked = s.AlphaAdd };
        _rbAlphaDel = new ModernRadio { Text = L("ui.borrar_capa_2", "Borrar capa 2"), Appearance = Appearance.Button, Width = 130, Height = 30, Checked = !s.AlphaAdd };
        _rbAlphaAdd.CheckedChanged += (_, _) => { if (_rbAlphaAdd.Checked) s.AlphaAdd = true; };
        _rbAlphaDel.CheckedChanged += (_, _) => { if (_rbAlphaDel.Checked) s.AlphaAdd = false; };
        row.Controls.Add(_rbAlphaAdd); row.Controls.Add(_rbAlphaDel);
        p.Controls.Add(row);

        var chkRellena = new CheckBox
        {
            Text = L("ui.poner_tile_si_falta", "Poner la textura si la capa 2 esta vacia"),
            Checked = s.AlphaFillsLayer2, AutoSize = true, Margin = new Padding(0, 8, 0, 4)
        };
        new ToolTip().SetToolTip(chkRellena,
            L("ui.poner_tile_si_falta_tip",
              "Si la celda no tiene textura en la capa 2 no hay nada para mostrar. Con esto se le pone la que elegiste en la herramienta Tiles."));
        chkRellena.CheckedChanged += (_, _) => s.AlphaFillsLayer2 = chkRellena.Checked;
        p.Controls.Add(chkRellena);

        var irTiles = new ModernButton { Text = L("ui.elegir_textura", "Elegir esa textura..."), IconName = "tiles", Width = 268, Height = 32 };
        irTiles.Click += (_, _) => { _view.State.TileLayer = 2; SetMode(EditMode.Tile); };
        p.Controls.Add(irTiles);

        p.Controls.Add(new Label
        {
            AutoSize = false, Width = 268, Height = 96, ForeColor = DarkTheme.TextDim,
            Text = L("ui.alfa_explicacion",
                     "Decide cuanto se ve la capa 2 sobre la 1. Con Shift, al reves. " +
                     "Abajo se ve que tiene la celda que apuntas.")
        });
    }

    private void RefreshTileList()
    {
        if (_tileList == null || _view.Tiles == null) return;
        foreach (var b in _tileThumbs.Values) b?.Dispose();
        _tileThumbs.Clear();
        _tileList.Items.Clear();
        foreach (var t in _view.Tiles.Available)
            _tileList.Items.Add(new CatalogItem
            {
                Id = t.Id,
                // el asterisco marca las que encontro el editor solo: el numero
                // puede no ser el que usa tu cliente
                Text = t.Name + (t.Guessed ? "  *" : "")
            });
        if (_tileList.Items.Count > 0) _tileList.SelectedIndex = 0;
    }

    // ------------------------------------------------------------- mapas
    /// <summary>
    /// Al arrancar se carga la lista de mapas del cliente que se uso la ultima
    /// vez, pero no se abre ninguno: se muestra la pantalla de inicio con los
    /// recientes para elegir.
    /// </summary>
    private void AutoOpen()
    {
        if (_tabs.TabCount > 0) _tabs.SelectedIndex = Math.Clamp(_cfg.ActiveTab, 0, _tabs.TabCount - 1);

        string guess = _cfg.ClientDataPath;
        if (string.IsNullOrEmpty(guess) || !Directory.Exists(guess))
        {
            foreach (var c in new[] { @"D:\MuServer\Client\Data", @"C:\MuServer\Client\Data" })
                if (Directory.Exists(c)) { guess = c; break; }
        }

        try
        {
            if (!string.IsNullOrEmpty(guess) && Directory.Exists(guess) && HasWorlds(guess))
                SetClientPath(guess, autoSelect: false);
        }
        catch { /* la carpeta guardada ya no sirve: se elige desde el inicio */ }

        ShowStart(true);
    }

    private void ChooseClient()
    {
        using var d = new FolderBrowserDialog
        {
            Description = L("ui.elegi_carpeta", "Elegi la carpeta Data del cliente, o directamente la carpeta de un mapa"),
            UseDescriptionForTitle = true
        };
        if (!string.IsNullOrEmpty(ClientDataPath)) d.SelectedPath = ClientDataPath;
        if (d.ShowDialog(this) == DialogResult.OK) OpenFolder(d.SelectedPath);
    }

    /// <summary>
    /// Acepta cualquier carpeta y se da cuenta de que es: la Data del cliente,
    /// una carpeta que contiene la Data, o directamente un mapa suelto.
    /// </summary>
    private void OpenFolder(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

        // 1) tiene subcarpetas World* : es un contenedor de mapas
        if (HasWorlds(path)) { SetClientPath(path); return; }

        // 2) es directamente la carpeta de un mapa canonico
        if (MuWorld.LooksLikeWorld(path)) { OpenWorldFolder(path); return; }

        // 3) adentro tiene una Data (o Client\Data) con los mundos
        foreach (var candidata in new[] { Path.Combine(path, "Data"), Path.Combine(path, "Client", "Data") })
            if (Directory.Exists(candidata) && HasWorlds(candidata)) { SetClientPath(candidata); return; }

        Dialogs.Show(this,
            L("msg.carpeta_no_sirve",
              "En esa carpeta no encontre ni mapas ni las carpetas World1, World2, ..." +
              Environment.NewLine + Environment.NewLine +
              "Elegi la carpeta Data del cliente, o la carpeta de un mapa " +
              "(la que tiene los archivos EncTerrainN.att, .map y .obj)."),
            "MU World Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private static bool HasWorlds(string path)
    {
        foreach (var dir in Directory.GetDirectories(path, "World*"))
        {
            string name = Path.GetFileName(dir);
            if (name.Length > 5 &&
                int.TryParse(name.AsSpan(5), out int worldNumber) &&
                MuWorld.HasMapForWorld(dir, worldNumber))
                return true;
        }
        return false;
    }

    private void SetClientPath(string path, bool autoSelect = true)
    {
        ClientDataPath = path;
        _cfg.ClientDataPath = path;
        Settings.Save(_cfg);

        _worldBox.Items.Clear();
        var worlds = new List<int>();
        foreach (var dir in Directory.GetDirectories(path, "World*"))
        {
            string name = Path.GetFileName(dir);
            if (int.TryParse(name.AsSpan(5), out int n) && MuWorld.HasMapForWorld(dir, n))
                worlds.Add(n);
        }
        worlds.Sort();

        foreach (int n in worlds)
            _worldBox.Items.Add(new CatalogItem { Id = n, Text = L("ui.world", "World") + n + WorldNames.Suffix(n) });

        if (_worldBox.Items.Count > 0 && autoSelect)
        {
            // arrancamos en Lorencia si esta, que es lo que uno espera ver
            int start = 0;
            for (int i = 0; i < _worldBox.Items.Count; i++)
                if (_worldBox.Items[i] is CatalogItem ci && ci.Id == 1) { start = i; break; }
            _worldBox.SelectedIndex = start;
        }
        else if (_worldBox.Items.Count == 0) Dialogs.Show(this, L("msg.sin_mundos", "En esa carpeta no hay mapas."), "MU World Editor");
        else ShowInfo(string.Format(L("ui.mapas_encontrados", "{0} mapas en el cliente: elegi uno arriba"), _worldBox.Items.Count));
    }

    /// <summary>
    /// Ajusta los limites del pincel de altura al formato del mapa abierto:
    /// 0..382 en los clasicos (8 bits x 1.5) y -500..5000 en los de 24 bits.
    /// </summary>
    private void AdjustHeightLimits()
    {
        var w = _view.Ref.World;
        var s = _view.State;
        s.HeightMin = w.MinHeight;
        s.HeightMax = w.MaxHeight;
        s.FlattenTarget = Math.Clamp(s.FlattenTarget, w.MinHeight, w.MaxHeight);
        if (_numMin != null) _numMin.Value = (decimal)s.HeightMin;
        if (_numMax != null) _numMax.Value = (decimal)s.HeightMax;
        if (_numTarget != null) _numTarget.Value = (decimal)MathF.Round(s.FlattenTarget);
    }

    private void OpenSelectedWorld() => OpenSelectedWorld(false);

    private void OpenSelectedWorld(bool force)
    {
        if (_building && !force) return;
        if (_worldBox.SelectedItem is not CatalogItem it) return;
        if (it.Id < 0) return;      // es un mapa suelto, ya esta cargado
        if (!ConfirmDiscard()) return;

        Cursor = Cursors.WaitCursor;
        try
        {
            _view.LoadWorld(ClientDataPath, it.Id);
            AfterWorldOpened($"World{it.Id}{WorldNames.Suffix(it.Id).Replace("  -  ", " · ")}", fromClient: true);
            RefreshTileList();
            RefreshCatalogCombo();
            RefreshObjectList();
            RefreshTerrainPanel();
            RefreshHistoryButtons();
            AdjustHeightLimits();

            if (_view.Ref.World.ObjectsReadOnly)
                Dialogs.Show(this,
                    "El archivo de objetos de este mapa esta danado (dice tener mas objetos de los que trae).\r\n" +
                    "Lo podes mirar y editar el terreno, pero el editor NO va a reescribir el .obj para no romperlo.",
                    "MU World Editor", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            Dialogs.Show(this, "No pude abrir el mapa:\r\n" + ex.Message, "MU World Editor",
                         MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { Cursor = Cursors.Default; }
    }

    private void SaveWorld()
    {
        try
        {
            _view.Ref.World.Save();
            _btnSave.Enabled = false;
            _lblInfo.Text = L("ui.guardado", "Guardado ") + " " + DateTime.Now.ToString("HH:mm:ss");
            _titleBar.Dirty = false;
        }
        catch (Exception ex)
        {
            Dialogs.Show(this, "No pude guardar:\r\n" + ex.Message, "MU World Editor",
                            MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private bool ConfirmDiscard()
    {
        if (!_view.Ref.World.Dirty) return true;
        var r = Dialogs.Show(this, L("msg.hay_cambios_sin_guardar_guardar_antes_de", "Hay cambios sin guardar. Guardar antes de seguir?"),
                                "MU World Editor", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (r == DialogResult.Cancel) return false;
        if (r == DialogResult.Yes) SaveWorld();
        return true;
    }

    private void OnClosingCheck(object sender, FormClosingEventArgs e)
    {
        if (!ConfirmDiscard()) { e.Cancel = true; return; }
        SaveUiState();
    }

    // ------------------------------------------------------------- listas
    private void RefreshCatalogCombo()
    {
        if (_view.Catalog == null) return;
        _newObjectBox.Items.Clear();
        foreach (var e in _view.Catalog.Entries.Values.OrderBy(x => x.Id))
            _newObjectBox.Items.Add(new CatalogItem
            {
                Id = e.Id,
                Text = e.Id + " - " + (string.IsNullOrEmpty(e.Name) ? e.FileName : e.Name)
                       + (e.Exists ? "" : "  (sin modelo)") + (e.Guessed ? "  *" : "")
            });
        if (_newObjectBox.Items.Count > 0) _newObjectBox.SelectedIndex = 0;
    }

    // ---------------------------------------------- listas con miniaturas
    private readonly Dictionary<int, Bitmap> _objThumbs = new();
    private readonly Queue<int> _thumbQueue = new();
    private readonly HashSet<int> _thumbQueued = new();
    private readonly Dictionary<int, Bitmap> _tileThumbs = new();
    private System.Windows.Forms.Timer _thumbTimer;

    private static (string name, Color color, string icon) Category(string modelName)
    {
        string n = (modelName ?? "").ToLowerInvariant();
        bool Any(params string[] k) => k.Any(n.Contains);
        if (Any("tree", "arbol", "leaf", "grass", "pasto", "bush", "plant", "flower", "straw")) return ("Vegetacion", Color.FromArgb(104, 196, 112), "sun");
        if (Any("house", "wall", "stair", "bridge", "gate", "door", "castle", "tower", "fence", "roof", "furniture", "tent", "sign")) return ("Construccion", Color.FromArgb(224, 168, 88), "home");
        if (Any("stone", "rock", "piedra", "cliff", "statue", "tomb", "grave")) return ("Roca", Color.FromArgb(150, 160, 176), "mountain");
        if (Any("light", "fire", "torch", "candle", "lamp", "bonfire")) return ("Luz", Color.FromArgb(250, 204, 92), "sun");
        if (Any("water", "fountain", "spout", "well", "pool")) return ("Agua", Color.FromArgb(92, 168, 240), "blend");
        if (Any("bird", "butterfly", "animal", "monster", "fish", "wolf", "merchant", "npc")) return ("Criatura", Color.FromArgb(232, 120, 170), "eye");
        return ("Otros", Color.FromArgb(150, 130, 220), "cube");
    }

    private Bitmap ObjectThumb(int type)
    {
        if (_objThumbs.TryGetValue(type, out var bmp)) return bmp;
        if (_thumbQueued.Add(type)) _thumbQueue.Enqueue(type);
        if (_thumbTimer == null)
        {
            // las miniaturas se arman de a poco, sin trabar la lista
            _thumbTimer = new System.Windows.Forms.Timer { Interval = 40 };
            _thumbTimer.Tick += (_, _) =>
            {
                int n = 0;
                while (_thumbQueue.Count > 0 && n++ < 3)
                {
                    int t = _thumbQueue.Dequeue();
                    _thumbQueued.Remove(t);
                    Bitmap img = null;
                    try { img = _view.RenderModelPreview(t, 64, 64); } catch { }
                    _objThumbs[t] = img;
                }
                if (n > 0) _objList?.Invalidate();
                if (_thumbQueue.Count == 0) _thumbTimer.Stop();
            };
        }
        if (!_thumbTimer.Enabled) _thumbTimer.Start();
        return null;
    }

    private void DrawObjectItem(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _objList.Items.Count) return;
        if (_objList.Items[e.Index] is not ObjectItem it) return;
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        var r = e.Bounds;

        using (var bg = new SolidBrush(sel ? DarkTheme.AccentDark : (e.Index % 2 == 0 ? DarkTheme.Sunken : DarkTheme.Mix(DarkTheme.Sunken, DarkTheme.Surface, 0.5f))))
            g.FillRectangle(bg, r);

        var o = it.Obj;
        string name = _view.Catalog?.DisplayName(o.Type) ?? ("Tipo " + o.Type);
        var cat = Category(name);
        using (var b = new SolidBrush(sel ? DarkTheme.Accent : cat.color)) g.FillRectangle(b, r.X, r.Y + 4, 3, r.Height - 8);

        var thumbRect = new Rectangle(r.X + 9, r.Y + 4, 32, 32);
        var thumb = ObjectThumb(o.Type);
        using (var path = DarkTheme.Round(thumbRect, 4))
        using (var b = new SolidBrush(DarkTheme.Mix(DarkTheme.Back, cat.color, 0.14f)))
            g.FillPath(b, path);
        if (thumb != null) g.DrawImage(thumb, thumbRect);
        else g.DrawImage(Icons.Get(cat.icon, 16, cat.color), thumbRect.X + 8, thumbRect.Y + 8);

        int tx = thumbRect.Right + 8;
        var tf = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        string idx = "#" + it.Index.ToString("0000");
        int idxW = TextRenderer.MeasureText(idx, DarkTheme.SmallFont).Width;
        TextRenderer.DrawText(g, name, DarkTheme.UiFont, new Rectangle(tx, r.Y + 3, r.Right - tx - idxW - 8, 18),
                              sel ? DarkTheme.AccentHover : DarkTheme.Text, tf);
        TextRenderer.DrawText(g, idx, DarkTheme.SmallFont, new Rectangle(r.Right - idxW - 6, r.Y + 4, idxW + 4, 16), DarkTheme.TextFaint, tf);
        string sub = $"{cat.name}  ·  {L("ui.tipo", "tipo")} {o.Type}  ·  [{(int)(o.X / 100)}, {(int)(o.Y / 100)}]";
        TextRenderer.DrawText(g, sub, DarkTheme.SmallFont, new Rectangle(tx, r.Y + 21, r.Right - tx - 6, 16), DarkTheme.TextDim, tf);
    }

    private Bitmap TileThumb(int id)
    {
        if (_tileThumbs.TryGetValue(id, out var bmp)) return bmp;
        Bitmap thumb = null;
        try
        {
            var tile = _view.Tiles?.Get(id);
            if (tile?.File != null)
            {
                using var full = Formats.MuTexture.Load(tile.File).ToBitmap();
                thumb = new Bitmap(28, 28);
                using var g = Graphics.FromImage(thumb);
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
                g.DrawImage(full, new Rectangle(0, 0, 28, 28));
            }
        }
        catch { }
        _tileThumbs[id] = thumb;
        return thumb;
    }

    private void DrawTileItem(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _tileList.Items.Count) return;
        if (_tileList.Items[e.Index] is not CatalogItem it) return;
        var g = e.Graphics;
        bool sel = (e.State & DrawItemState.Selected) != 0;
        var r = e.Bounds;
        using (var bg = new SolidBrush(sel ? DarkTheme.AccentDark : DarkTheme.Sunken)) g.FillRectangle(bg, r);
        if (sel) using (var b = new SolidBrush(DarkTheme.Accent)) g.FillRectangle(b, r.X, r.Y + 4, 3, r.Height - 8);

        var thumbRect = new Rectangle(r.X + 9, r.Y + 4, 28, 28);
        var thumb = TileThumb(it.Id);
        if (thumb != null) g.DrawImage(thumb, thumbRect);
        else using (var b = new SolidBrush(DarkTheme.SurfaceAlt)) g.FillRectangle(b, thumbRect);
        using (var p = new Pen(DarkTheme.BorderSoft)) g.DrawRectangle(p, thumbRect);

        var tf = TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter;
        string num = it.Id.ToString();
        TextRenderer.DrawText(g, it.Text, DarkTheme.UiFont, new Rectangle(thumbRect.Right + 10, r.Y, r.Width - 90, r.Height),
                              sel ? DarkTheme.AccentHover : DarkTheme.Text, tf);
        TextRenderer.DrawText(g, num, DarkTheme.SmallFont, new Rectangle(r.Right - 40, r.Y, 34, r.Height), DarkTheme.TextFaint,
                              tf | TextFormatFlags.Right);
    }

    private TextBox _objFilter;
    private PictureBox _preview, _tilePreview;
    private int _previewType = -1;

    private void RefreshObjectList()
    {
        _building = true;
        _objList.BeginUpdate();
        _objList.Items.Clear();

        string filtro = (_objFilter?.Text ?? "").Trim();
        var w = _view.Ref.World;

        for (int i = 0; i < w.Objects.Count; i++)
        {
            var o = w.Objects[i];
            string name = _view.Catalog?.DisplayName(o.Type) ?? ("Tipo " + o.Type);
            string texto = $"{i:0000}  {name}   [{(int)(o.X / 100)},{(int)(o.Y / 100)}]";

            // el filtro busca por nombre o por numero de tipo
            if (filtro.Length > 0)
            {
                bool coincide = name.Contains(filtro, StringComparison.OrdinalIgnoreCase)
                                || o.Type.ToString() == filtro;
                if (!coincide) continue;
            }

            _objList.Items.Add(new ObjectItem { Obj = o, Index = i, Text = texto });
        }

        _objList.EndUpdate();
        _building = false;
    }

    /// <summary>Abre la ventana para cambiarle el aspecto al objeto elegido.</summary>
    private void ReplaceSelectedLook()
    {
        var sel = _view.State.Selection;
        if (sel.Count == 0)
        {
            Dialogs.Show(this, L("msg.elegi_uno_primero", "Primero elegi un objeto."), "MU World Editor");
            return;
        }

        using var dlg = new ReplaceDialog(_view, sel[0]);
        dlg.ShowDialog(this);

        if (dlg.ModelChanged)
        {
            RefreshCatalogCombo();
            RefreshObjectList();
            RefreshSelection();
            _view.Invalidate();
        }
    }

    /// <summary>Selecciona de una todos los objetos del mismo tipo que el elegido.</summary>
    private void SelectSameType()
    {
        var sel = _view.State.Selection;
        if (sel.Count == 0)
        {
            Dialogs.Show(this, L("msg.elegi_uno_primero", "Primero elegí un objeto."), "MU World Editor");
            return;
        }

        short tipo = sel[0].Type;
        var todos = _view.Ref.World.Objects.Where(o => o.Type == tipo).ToList();
        sel.Clear();
        sel.AddRange(todos);

        RefreshSelection();
        _view.Invalidate();
        _lblInfo.Text = todos.Count + " " + L("ui.objetos_del_mismo_tipo", "objetos de ese tipo");
    }

    private void RefreshSelection()
    {
        _building = true;
        _objList.ClearSelected();
        for (int i = 0; i < _objList.Items.Count; i++)
            if (_objList.Items[i] is ObjectItem oi && _view.State.Selection.Contains(oi.Obj))
                _objList.SetSelected(i, true);
        _building = false;
        UpdateSelectionLabel();
        RefreshTransformBox();
        ScrollToSelection();
    }

    /// <summary>Deja a la vista el objeto seleccionado dentro de la lista.</summary>
    private void ScrollToSelection()
    {
        if (_objList.SelectedIndex < 0) return;
        int visibles = Math.Max(1, _objList.ClientSize.Height / Math.Max(1, _objList.ItemHeight));
        _objList.TopIndex = Math.Max(0, _objList.SelectedIndex - visibles / 2);
    }

    /// <summary>Muestra la imagen de la textura elegida.</summary>
    private void ShowTilePreview(int tileId)
    {
        if (_tilePreview == null) return;

        var vieja = _tilePreview.Image;
        _tilePreview.Image = null;

        var tile = _view.Tiles?.Get(tileId);
        if (tile?.File != null)
        {
            try { _tilePreview.Image = Formats.MuTexture.Load(tile.File).ToBitmap(); }
            catch { /* si la textura esta rota, el recuadro queda vacio */ }
        }
        vieja?.Dispose();
    }

    /// <summary>Dibuja el modelo de ese tipo en el recuadro de vista previa.</summary>
    private void ShowPreview(int type)
    {
        if (_preview == null) return;
        if (type == _previewType) return;          // ya esta mostrando ese
        _previewType = type;

        var vieja = _preview.Image;
        _preview.Image = type < 0 ? null : _view.RenderModelPreview(type, _preview.Width - 2, _preview.Height - 2);
        vieja?.Dispose();
    }

    private void UpdateSelectionLabel()
    {
        var sel = _view.State.Selection;
        if (sel.Count == 0)
        {
            _selInfo.Text = L("ui.nada_seleccionado", L("msg.nada_seleccionado", "Nada seleccionado"));
            // sin nada elegido mostramos el modelo que se va a agregar
            ShowPreview(_view.State.NewObjectType);
            return;
        }
        ShowPreview(sel[0].Type);
        if (sel.Count > 1) { _selInfo.Text = sel.Count + " " + L("ui.objetos_seleccionados", "objetos seleccionados"); return; }

        var o = sel[0];
        string name = _view.Catalog?.DisplayName(o.Type) ?? ("Tipo " + o.Type);
        _selInfo.Text = $"{name}  ({L("ui.tipo", "tipo")} {o.Type})" + Environment.NewLine + Environment.NewLine +
                        $"{L("ui.posicion", "Posicion")}: {o.X:0} , {o.Y:0} , {o.Z:0}" + Environment.NewLine +
                        $"{L("ui.celda", "Celda")}: {(int)(o.X / 100)} , {(int)(o.Y / 100)}" + Environment.NewLine +
                        $"{L("ui.giro", "Giro")}: {o.AngleX:0} , {o.AngleY:0} , {o.AngleZ:0}" + Environment.NewLine +
                        $"{L("ui.escala", "Escala")}: {o.Scale:0.00}";
    }

    private void RefreshHistoryButtons()
    {
        _btnUndo.Enabled = _view.History.CanUndo;
        _btnRedo.Enabled = _view.History.CanRedo;
        _btnUndo.ToolTipText = _view.History.UndoName ?? "";
        _btnRedo.ToolTipText = _view.History.RedoName ?? "";
        if (_objList.Items.Count != _view.Ref.World.Objects.Count) RefreshObjectList();
    }

    // ------------------------------------------------------------ teclado
    /// <summary>Se esta escribiendo en un campo: las letras sueltas no son atajos.</summary>
    private bool Typing()
    {
        Control c = ActiveControl;
        while (c is ContainerControl cc && cc.ActiveControl != null) c = cc.ActiveControl;
        return c is TextBoxBase || c is NumberBox || c?.Parent is NumberBox || c is ComboBox;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F1) { ShowShortcuts(); return true; }
        if (keyData == Keys.Escape && _start.Visible && _view.HasWorld) { ShowStart(false); return true; }

        // con modificadores siempre son atajos; sin ellos, solo si no se esta escribiendo
        bool plain = (keyData & (Keys.Control | Keys.Alt)) == 0;
        if (plain && Typing() && keyData != Keys.Tab) return base.ProcessCmdKey(ref msg, keyData);

        switch (keyData)
        {
            case Keys.D1: SetMode(EditMode.View); return true;
            case Keys.D2: SetMode(EditMode.Height); return true;
            case Keys.D3: SetMode(EditMode.Attribute); return true;
            case Keys.D4: SetMode(EditMode.Tile); return true;
            case Keys.D5: SetMode(EditMode.Alpha); return true;
            case Keys.D6: SetMode(EditMode.Objects); return true;
            case Keys.D7: SetMode(EditMode.Area); return true;
            case Keys.Control | Keys.Z: _view.History.Undo(); _view.RebuildAll(); return true;
            case Keys.Control | Keys.Y:
            case Keys.Control | Keys.Shift | Keys.Z: _view.History.Redo(); _view.RebuildAll(); return true;
            case Keys.Control | Keys.S: SaveWorld(); return true;
            case Keys.Control | Keys.C: _view.CopySelection(); return true;
            case Keys.Control | Keys.V: _view.PasteClipboard(); return true;
            case Keys.Control | Keys.D: _view.DuplicateSelection(); return true;
            case Keys.Delete: _view.DeleteSelection(); return true;
            case Keys.F: _view.FocusOnSelection(); return true;
            case Keys.Tab:
                TogglePanel();
                return true;
            case Keys.G | Keys.Shift:
                return true;
            case Keys.R:
                _view.State.Gizmo = GizmoMode.Rotate; _view.Invalidate(); return true;
            case Keys.Y:
                _view.State.Gizmo = GizmoMode.Scale; _view.Invalidate(); return true;
            case Keys.M:
                _view.State.Gizmo = GizmoMode.Move; _view.Invalidate(); return true;
            case Keys.PageUp:
                _view.CycleDuplicate(1); return true;
            case Keys.PageDown:
                _view.CycleDuplicate(-1); return true;
            case Keys.G:
                _view.State.ShowGrid = !_view.State.ShowGrid;
                _view.State.RaiseChanged();
                return true;
            case Keys.T:
                _view.State.ShowAttributes = !_view.State.ShowAttributes;
                _view.State.RaiseChanged();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}
