using System.Numerics;
using MuWorldEditor.Editing;
using MuWorldEditor.Formats;
using MuWorldEditor.Render;
using MuWorldEditor.World;

namespace MuWorldEditor.App;

/// <summary>La vista 3D del mapa: dibuja y recibe el mouse.</summary>
public sealed class MapView : GlControl
{
    public readonly WorldRef Ref = new();
    public readonly EditorState State = new();
    public readonly History History = new();
    public readonly Camera Camera = new();

    public TileCatalog Tiles;
    public ObjectCatalog Catalog;
    public string ClientDataPath = "";

    /// <summary>Cuantos modelos y texturas nuevos aparecieron al abrir el mapa.</summary>

    public int NewModelsFound, NewTilesFound;

    private ShaderProgram _shader;
    private TerrainRenderer _terrain;
    private ModelRenderer _models;
    private LineBatch _lines;
    private QuadBatch _quads;
    private TexturedQuad _texQuad;

    // minimapa
    private GpuTexture _minimapTex;
    private bool _minimapDirty = true;
    private DateTime _minimapBuilt = DateTime.MinValue;
    private RectangleF _minimapRect;
    private bool _minimapDrag;

    /// <summary>Objeto que esta debajo del mouse (se marca con un contorno suave).</summary>
    public MapFiles.MapObject HoverObject { get; private set; }

    // transicion suave de camara
    private bool _camAnim;
    private DateTime _camAnimStart;
    private Vector3 _fromTarget, _toTarget;
    private float _fromDist, _toDist, _fromYaw, _toYaw, _fromPitch, _toPitch;
    private const float CamAnimSeconds = 0.38f;

    private bool _dragCamera, _panCamera, _painting, _dragObjects, _markingArea;
    private Point _lastMouse;
    private Vector3 _cursorWorld;
    private int _cursorCellX = -1, _cursorCellY = -1;
    private bool _cursorValid;

    private readonly Gizmo _gizmo = new();
    public Gizmo Gizmo => _gizmo;
    /// <summary>Cache de modelos, para poder recargarlos tras cambiar archivos.</summary>
    public ModelRenderer Models => _models;
    private bool _gizmoDrag;
    private float _gizmoStartT, _gizmoStartAngle, _gizmoStartRadius;
    private Vector3 _gizmoStartPlane;
    private List<(MapFiles.MapObject obj, TransformObjects.State before)> _gizmoStart;

    private ArrayStroke<byte> _strokeByte;
    private ArrayStroke<float> _strokeHeight;
    private ArrayStroke<ushort> _strokeShort;
    private List<ArrayStroke<byte>> _strokeGroup;
    private List<(MapFiles.MapObject obj, TransformObjects.State before)> _dragStart;
    private Vector3 _dragAnchor;

    public event Action StatusChanged;
    public event Action SelectionChanged;

    /// <summary>Cambio el rectangulo elegido con la herramienta Area.</summary>

    public event Action AreaChanged;

    public string StatusText { get; private set; } = "";
    public int LastFrameMs { get; private set; }
    public int VisibleObjects { get; private set; }

    private MuWorld W => Ref.World;

    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();
    private int _waterTick;
    private bool _animatedVisible;
    private double _animClock, _lastRenderSec;
    private System.Windows.Forms.Timer _animTimer;

    public MapView()
    {
        State.Changed += () => Invalidate();
        History.Changed += () => Ref.World.Dirty = true;

        // el agua se mueve, asi que hay que repintar seguido
        _animTimer = new System.Windows.Forms.Timer { Interval = 16 };
        _animTimer.Tick += (_, _) =>
        {
            if (_camAnim) { Invalidate(); return; }
            ++_waterTick;
            if (_waterTick % 2 == 0 && (State.AnimateWater || (State.PlayAnimations && _animatedVisible && State.ShowObjects))) Invalidate();
            // el minimapa se rehace tranquilo, no en cada pincelada
            if (_minimapDirty && State.ShowMinimap && (DateTime.Now - _minimapBuilt).TotalSeconds > 1.2) Invalidate();
        };
        _animTimer.Start();
        History.Changed += () => _minimapDirty = true;
    }

    protected override void OnGlInit()
    {
        _shader = ShaderProgram.Create();
        _terrain = new TerrainRenderer(Ref);
        _models = new ModelRenderer();
        _lines = new LineBatch();
        _quads = new QuadBatch();
        _texQuad = new TexturedQuad();
        Gl.ClearStencil(0);

        Gl.Enable(Gl.DEPTH_TEST);
        Gl.DepthFunc(Gl.LEQUAL);
        Gl.Disable(Gl.CULL_FACE);
    }

    // ----------------------------------------------------------- carga
    public void LoadWorld(string clientDataPath, int worldNumber)
        => LoadWorldFolder(Path.Combine(clientDataPath, "World" + worldNumber), worldNumber, clientDataPath);

    /// <summary>
    /// Abre un mapa desde la carpeta que sea, este o no dentro del cliente.
    /// Los modelos se buscan primero ahi mismo y despues en el cliente.
    /// </summary>
    public void LoadWorldFolder(string dir, int worldNumber, string clientDataPath = null)
    {
        string nextClientDataPath = clientDataPath ?? ClientDataPath;
        string catalogRoot = string.IsNullOrWhiteSpace(nextClientDataPath) ? dir : nextClientDataPath;
        var nextWorld = MuWorld.Load(dir, worldNumber);
        var nextTiles = new TileCatalog(dir);
        var nextCatalog = ObjectCatalog.Load(catalogRoot, worldNumber,
                                             nextWorld.Objects.Select(o => (int)o.Type), dir);

        // los .bmd y las texturas que agregues a la carpeta aparecen solos
        NewModelsFound = nextCatalog.AddModelsFromDisk();
        NewTilesFound = nextTiles.Unassigned.Count;

        MakeCurrent();
        Tiles?.Dispose();
        _models?.ForgetAll();

        ClientDataPath = nextClientDataPath;
        Ref.World = nextWorld;
        Tiles = nextTiles;
        Catalog = nextCatalog;

        State.Selection.Clear();
        History.Clear();
        Ref.World.Dirty = false;

        Camera.Target = new Vector3(12800, 12800, W.HeightAtWorld(12800, 12800));
        Camera.Distance = 4000;
        _camAnim = false;
        HoverObject = null;
        _minimapDirty = true;
        _minimapBuilt = DateTime.MinValue;

        _terrain?.InvalidateAll();
        Invalidate();
        SelectionChanged?.Invoke();
    }

    public void RebuildAll()
    {
        _terrain?.InvalidateAll();
        _minimapDirty = true;
        Invalidate();
    }

    /// <summary>Hay un mapa cargado (no es el mundo vacio del arranque).</summary>
    public bool HasWorld => W != null && W.MapPath != null;

    // ---------------------------------------------------------- dibujo
    protected override void OnGlRender()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Gl.ClearColor(State.Background.R / 255f, State.Background.G / 255f, State.Background.B / 255f, 1f);
        Gl.Clear(Gl.COLOR_BUFFER_BIT | Gl.DEPTH_BUFFER_BIT | Gl.STENCIL_BUFFER_BIT);
        if (_shader == null || W == null) return;

        StepCameraAnimation();
        Camera.Fov = State.CameraAngle * MathF.PI / 180f;
        Camera.Far = State.CameraFar;
        Camera.Update(Width, Height);
        var frustum = Frustum.FromViewProjection(Camera.ViewProjection);

        _shader.Use();
        if (State.ShowSky) DrawSky();

        _shader.SetMvp(Camera.ViewProjection);
        _shader.SetTime((float)_clock.Elapsed.TotalSeconds);
        {
            // reloj propio de las animaciones: asi la velocidad se puede cambiar sin saltos
            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Clamp(now - _lastRenderSec, 0, 0.25);
            _lastRenderSec = now;
            if (State.PlayAnimations) _animClock += dt * State.AnimationSpeed / 100.0;
            ModelRenderer.AnimTime = _animClock;
            ModelRenderer.PlayAnimations = State.PlayAnimations;
        }
        var fog = State.FogFromSky && State.ShowSky ? State.SkyHorizon : State.FogColor;
        _shader.SetFog(State.ShowFog, new Vector3(fog.R / 255f, fog.G / 255f, fog.B / 255f),
                       State.FogStart, State.FogEnd, State.FogDensity);
        _shader.SetLighting(State.Ambient, State.Diffuse);
        _shader.SetSky(State.ShowSky ? State.SkyHorizon : State.Background);
        if (_terrain != null) _terrain.Shading = State.TerrainShading;

        // modo alambre: se ve la malla del terreno y de los modelos
        Gl.PolygonMode(Gl.FRONT_AND_BACK, State.Wireframe ? Gl.LINE : Gl.FILL);

        if (Tiles != null)
            _terrain.Render(_shader, Tiles, State.ShowLayer1, State.ShowLayer2, frustum, State.AnimateWater);

        if (State.ShowObjects) DrawObjects(frustum);

        Gl.PolygonMode(Gl.FRONT_AND_BACK, Gl.FILL);
        DrawOverlays();
        DrawOutlines();
        DrawGizmo();
        DrawHud();

        LastFrameMs = (int)sw.ElapsedMilliseconds;
    }

    private readonly QuadBatch _skyQuads = new();

    /// <summary>Degradado de cielo detras de todo, en franjas para que quede parejo.</summary>
    private void DrawSky()
    {
        _shader.SetMvp(Matrix4x4.Identity);
        _shader.SetFog(false, Vector3.Zero, 0f, -1f, 1f);
        _shader.SetLighting(Color.Black, Color.White);
        _shader.SetWater(false);

        // el horizonte cae mas abajo o mas arriba segun la inclinacion de la camara
        float horizon = Math.Clamp(-0.15f + (Camera.Pitch / (MathF.PI / 2f)) * 0.9f, -0.6f, 0.95f);
        var top = State.SkyTop;
        var hor = State.SkyHorizon;
        var low = DarkTheme.Mix(State.SkyHorizon, Color.Black, 0.55f);

        _skyQuads.Clear();
        const int bands = 24;
        for (int i = 0; i < bands; i++)
        {
            float t0 = i / (float)bands, t1 = (i + 1) / (float)bands;
            float y0 = horizon + (1f - horizon) * t0, y1 = horizon + (1f - horizon) * t1;
            var c = DarkTheme.Mix(hor, top, MathF.Pow((t0 + t1) * 0.5f, 0.55f));
            _skyQuads.AddQuad(new Vector3(-1, y0, 0.999f), new Vector3(1, y0, 0.999f), new Vector3(1, y1, 0.999f), new Vector3(-1, y1, 0.999f), c);

            float z0 = horizon - (horizon + 1f) * t1, z1 = horizon - (horizon + 1f) * t0;
            var d = DarkTheme.Mix(hor, low, (t0 + t1) * 0.5f);
            _skyQuads.AddQuad(new Vector3(-1, z0, 0.999f), new Vector3(1, z0, 0.999f), new Vector3(1, z1, 0.999f), new Vector3(-1, z1, 0.999f), d);
        }
        Gl.Disable(Gl.DEPTH_TEST);
        _skyQuads.Draw(_shader);
        Gl.Enable(Gl.DEPTH_TEST);
    }

    private readonly List<(ModelRenderer.GpuModel model, Matrix4x4 m)> _outlineSel = new(), _outlineHover = new();

    /// <summary>Contornos de lo elegido (dorado) y de lo que esta bajo el mouse (blanco suave).</summary>
    private void DrawOutlines()
    {
        foreach (var (model, m) in _outlineHover)
            _models.DrawOutline(_shader, model, m, Color.FromArgb(150, 255, 255, 255), 3f);
        foreach (var (model, m) in _outlineSel)
            _models.DrawOutline(_shader, model, m, Color.FromArgb(255, DarkTheme.Accent), 5f);
        _outlineHover.Clear();
        _outlineSel.Clear();
    }

    private void DrawObjects(Frustum frustum)
    {
        VisibleObjects = 0;
        _animatedVisible = false;
        if (Catalog == null) return;

        var dirs = Catalog.SearchDirs().ToArray();
        foreach (var o in W.Objects)
        {
            var entry = Catalog.Get(o.Type);
            var model = entry?.FullPath != null ? _models.Get(entry.FullPath, dirs) : null;

            var m = MuMath.AngleMatrixDegrees(new Vector3(o.AngleX, o.AngleY, o.AngleZ));
            var transform = Matrix4x4.CreateScale(o.Scale) * m;
            transform.M41 = o.X; transform.M42 = o.Y; transform.M43 = o.Z;

            Vector3 lo, hi;
            if (model != null) { lo = model.Min * o.Scale; hi = model.Max * o.Scale; }
            else { lo = new Vector3(-60, -60, 0); hi = new Vector3(60, 60, 160); }

            if (model != null && model.Animated && !State.ShowAnimatedObjects) continue;
            if (model != null && !model.Animated && !State.ShowStaticObjects) continue;

            var center = new Vector3(o.X, o.Y, o.Z);
            float r = Math.Max((hi - lo).Length(), 200f);
            if (!frustum.Intersects(center - new Vector3(r), center + new Vector3(r))) continue;
            VisibleObjects++;
            if (model != null && model.Animated) _animatedVisible = true;

            bool selected = State.Selection.Contains(o);
            bool hover = ReferenceEquals(o, HoverObject);
            var tint = selected ? new Vector4(1.08f, 1.08f, 1.08f, 1f) : Vector4.One;

            if (model != null)
            {
                _models.Draw(_shader, model, transform, tint);
                if (selected && State.HighlightSelected) _outlineSel.Add((model, transform));
                else if (hover && State.HoverHighlight) _outlineHover.Add((model, transform));
            }
            else
            {
                // sin modelo: una caja discreta que se marca al tocarla
                var col = selected ? DarkTheme.Accent : hover ? Color.FromArgb(200, 255, 255, 255) : Color.FromArgb(70, 230, 140, 90);
                _lines.AddTransformedBox(lo, hi, transform, col);
            }

            if (State.ShowObjectBBox && !selected)
                _lines.AddTransformedBox(lo, hi, transform, hover ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(55, 120, 190, 255));
        }
    }

    private void DrawOverlays()
    {
        _quads.Clear();

        if (State.ShowAttributes) BuildAttributeOverlay();
        if (State.ShowGrid) BuildGrid();
        if (State.ShowTerrainBBox) BuildTerrainBox();
        if (State.HasArea) BuildAreaBox();
        if (_cursorValid && IsBrushMode) BuildBrushCursor();

        _quads.Draw(_shader);
        _lines.Draw(_shader, 1.5f);
        _lines.Clear();
        _quads.Clear();
    }

    /// <summary>
    /// Ayudas para acomodar el objeto: una linea hasta el piso, la celda que
    /// ocupa y una cruz en el centro. Sin esto hay que adivinar donde cae.
    /// </summary>
    private void BuildObjectGuides()
    {
        if (State.Mode != EditMode.Objects || !State.ShowObjectGuides) return;

        foreach (var o in State.Selection)
        {
            float piso = W.HeightAtWorld(o.X, o.Y);
            var arriba = new Vector3(o.X, o.Y, o.Z);
            var abajo = new Vector3(o.X, o.Y, piso);

            // caida a plomo hasta el piso
            _lines.AddLine(arriba, abajo, Color.FromArgb(200, 255, 220, 80));

            // cruz en el piso, para centrar a ojo
            float k = MuMath.TerrainScale * 0.45f;
            _lines.AddLine(abajo - new Vector3(k, 0, 0), abajo + new Vector3(k, 0, 0), Color.FromArgb(220, 255, 120, 120));
            _lines.AddLine(abajo - new Vector3(0, k, 0), abajo + new Vector3(0, k, 0), Color.FromArgb(220, 120, 255, 120));

            // borde de la celda donde cae el objeto
            int cx = MuMath.Clamp((int)(o.X / MuMath.TerrainScale), 0, MuWorld.Size - 1);
            int cy = MuMath.Clamp((int)(o.Y / MuMath.TerrainScale), 0, MuWorld.Size - 1);
            float s = MuMath.TerrainScale;
            Vector3 P(int px, int py) => new(px * s, py * s, W.HeightAt(px, py) + 4f);
            var a = P(cx, cy); var b = P(cx + 1, cy); var c = P(cx + 1, cy + 1); var d = P(cx, cy + 1);
            var col = Color.FromArgb(200, 120, 220, 255);
            _lines.AddLine(a, b, col); _lines.AddLine(b, c, col);
            _lines.AddLine(c, d, col); _lines.AddLine(d, a, col);
        }
    }

    /// <summary>Las flechas del objeto seleccionado, siempre por encima de la escena.</summary>
    private void DrawGizmo()
    {
        _gizmo.Visible = State.Mode == EditMode.Objects && State.Selection.Count > 0;
        if (!_gizmo.Visible) return;


        _gizmo.Origin = new Vector3(
            State.Selection.Average(o => o.X),
            State.Selection.Average(o => o.Y),
            State.Selection.Average(o => o.Z));

        _quads.Clear();
        _lines.Clear();
        // las guias van junto con las flechas: siempre por encima de la escena,
        // si no quedan tapadas por la propia casa o el cerro
        BuildObjectGuides();
        _gizmo.Draw(_lines, _quads, State.Gizmo, Camera);

        Gl.Disable(Gl.DEPTH_TEST);
        _quads.Draw(_shader);
        _lines.Draw(_shader, 3f);
        Gl.Enable(Gl.DEPTH_TEST);

        _lines.Clear();
        _quads.Clear();
    }

    private bool IsBrushMode =>
        State.Mode is EditMode.Height or EditMode.Attribute or EditMode.Tile or EditMode.Alpha;

    /// <summary>
    /// Centro y radio en celdas del recuadro de trabajo. Se dibuja solo ese
    /// pedazo para no tener 65 mil celdas encima, y el tamano lo elige el usuario.
    /// </summary>
    private void OverlayArea(out int cx, out int cy, out int range)
    {
        var center = State.OverlayFollowsCursor && _cursorValid ? _cursorWorld : Camera.Target;
        cx = (int)(center.X / MuMath.TerrainScale);
        cy = (int)(center.Y / MuMath.TerrainScale);
        range = Math.Clamp(State.OverlayRange, 2, MuWorld.Size);
    }

    private void BuildAttributeOverlay()
    {
        OverlayArea(out int cx, out int cy, out int range);

        for (int y = Math.Max(0, cy - range); y < Math.Min(MuWorld.Size, cy + range); y++)
            for (int x = Math.Max(0, cx - range); x < Math.Min(MuWorld.Size, cx + range); x++)
            {
                ushort v = W.Attribute[MuWorld.Index(x, y)];
                if (v == 0) continue;
                var c = AttributeInfo.ColorOf(v);
                if (c.A == 0) continue;
                c = Color.FromArgb(State.AttributeOpacity, c);

                float s = MuMath.TerrainScale;
                Vector3 P(int px, int py) => new(px * s, py * s, W.HeightAt(px, py) + 3f);
                _quads.AddQuad(P(x, y), P(x + 1, y), P(x + 1, y + 1), P(x, y + 1), c);
            }
    }

    /// <summary>La caja que envuelve todo el terreno.</summary>
    private void BuildTerrainBox()
    {
        float s = MuMath.TerrainScale * MuWorld.Size;
        float lo = 0f, hi = 0f;
        for (int i = 0; i < W.Height.Length; i += 97)
        {
            float h = W.Height[i];
            if (h < lo) lo = h;
            if (h > hi) hi = h;
        }
        _lines.AddBox(new Vector3(0, 0, lo), new Vector3(s, s, hi + 1), Color.FromArgb(70, DarkTheme.Accent));
    }

    /// <summary>Pinta el rectangulo elegido y lo marca con un borde.</summary>
    private void BuildAreaBox()
    {
        var (x0, y0, x1, y1) = State.AreaOrdered();
        float s = MuMath.TerrainScale;
        Vector3 P(int px, int py) => new(px * s, py * s, W.HeightAt(Math.Min(px, MuWorld.Size - 1),
                                                                    Math.Min(py, MuWorld.Size - 1)) + 8f);

        // relleno suave celda por celda, para que siga el relieve
        var relleno = Color.FromArgb(60, 120, 210, 255);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                _quads.AddQuad(P(x, y), P(x + 1, y), P(x + 1, y + 1), P(x, y + 1), relleno);

        // borde bien visible
        var borde = Color.FromArgb(255, 120, 210, 255);
        for (int x = x0; x <= x1; x++)
        {
            _lines.AddLine(P(x, y0), P(x + 1, y0), borde);
            _lines.AddLine(P(x, y1 + 1), P(x + 1, y1 + 1), borde);
        }
        for (int y = y0; y <= y1; y++)
        {
            _lines.AddLine(P(x0, y), P(x0, y + 1), borde);
            _lines.AddLine(P(x1 + 1, y), P(x1 + 1, y + 1), borde);
        }
    }

    /// <summary>
    /// Grilla de celdas que se desvanece hacia los bordes del recuadro, con una
    /// linea mas marcada cada 8 celdas para medir a ojo.
    /// </summary>
    private void BuildGrid()
    {
        OverlayArea(out int cx, out int cy, out int range);
        float s = MuMath.TerrainScale;

        int x0 = Math.Max(0, cx - range), x1 = Math.Min(MuWorld.Size - 1, cx + range);
        int y0 = Math.Max(0, cy - range), y1 = Math.Min(MuWorld.Size - 1, cy + range);

        Color Col(int x, int y, bool major)
        {
            float d = MathF.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) / Math.Max(1, range);
            float fade = Math.Clamp(1f - d, 0f, 1f);
            fade *= fade;
            int a = (int)((major ? 150 : 60) * fade);
            return major ? Color.FromArgb(a, DarkTheme.Accent) : Color.FromArgb(a, 255, 255, 255);
        }

        for (int y = y0; y <= y1; y++)
        {
            bool major = y % 8 == 0;
            for (int x = x0; x < x1; x++)
            {
                var c = Col(x, y, major);
                if (c.A < 4) continue;
                _lines.AddLine(new Vector3(x * s, y * s, W.HeightAt(x, y) + 2f),
                               new Vector3((x + 1) * s, y * s, W.HeightAt(x + 1, y) + 2f), c);
            }
        }

        for (int x = x0; x <= x1; x++)
        {
            bool major = x % 8 == 0;
            for (int y = y0; y < y1; y++)
            {
                var c = Col(x, y, major);
                if (c.A < 4) continue;
                _lines.AddLine(new Vector3(x * s, y * s, W.HeightAt(x, y) + 2f),
                               new Vector3(x * s, (y + 1) * s, W.HeightAt(x, y + 1) + 2f), c);
            }
        }
    }

    /// <summary>
    /// Pinta en vivo las celdas que el pincel va a tocar, con el color de lo que
    /// se esta por poner y el degradado de la dureza. Es lo que hace el editor
    /// original: se ve exactamente donde va a caer antes de apretar.
    /// </summary>
    private void BuildBrushCells()
    {
        if (!_cursorValid) return;

        float cx = _cursorWorld.X / MuMath.TerrainScale;
        float cy = _cursorWorld.Y / MuMath.TerrainScale;
        float r = State.BrushRadius;

        var color = BrushColor();
        int x0 = Math.Max(0, (int)MathF.Floor(cx - r));
        int y0 = Math.Max(0, (int)MathF.Floor(cy - r));
        int x1 = Math.Min(MuWorld.Size - 1, (int)MathF.Ceiling(cx + r));
        int y1 = Math.Min(MuWorld.Size - 1, (int)MathF.Ceiling(cy + r));

        float s = MuMath.TerrainScale;
        Vector3 P(int px, int py) => new(px * s, py * s, W.HeightAt(px, py) + 6f);

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                if (d > r) continue;

                float w = Falloff(d / r, State.BrushHardness);
                if (w <= 0.02f) continue;

                var c = Color.FromArgb((int)(w * 165), color);
                _quads.AddQuad(P(x, y), P(x + 1, y), P(x + 1, y + 1), P(x, y + 1), c);
            }
    }

    /// <summary>De que color se muestra el pincel segun lo que va a hacer.</summary>
    private Color BrushColor()
    {
        switch (State.Mode)
        {
            case EditMode.Attribute:
                if (State.AttributeAction == AttributeMode.Remove || ModifierKeys.HasFlag(Keys.Shift))
                    return Color.FromArgb(220, 220, 220);
                foreach (var a in AttributeInfo.All)
                    if ((State.AttributeFlags & a.Flag) != 0) return a.Color;
                return Color.FromArgb(200, 200, 200);

            case EditMode.Height:
                return State.Height == HeightMode.Smooth ? Color.FromArgb(140, 200, 255)
                                                         : Color.FromArgb(120, 230, 140);
            case EditMode.Tile:
                return Color.FromArgb(120, 190, 255);

            default:
                return State.AlphaAdd ? Color.FromArgb(255, 220, 120) : Color.FromArgb(190, 190, 190);
        }
    }

    private void BuildBrushCursor()
    {
        BuildBrushCells();

        float r = State.BrushRadius * MuMath.TerrainScale;
        var col = State.Mode switch
        {
            EditMode.Height => Color.FromArgb(240, 130, 235, 150),
            EditMode.Attribute => Color.FromArgb(240, 255, 130, 120),
            EditMode.Tile => Color.FromArgb(240, 140, 200, 255),
            _ => Color.FromArgb(240, DarkTheme.Accent)
        };

        void Ring(float radius, Color c, float lift)
        {
            const int steps = 64;
            Vector3 prev = default;
            for (int i = 0; i <= steps; i++)
            {
                float a = i / (float)steps * MathF.Tau;
                float wx = _cursorWorld.X + MathF.Cos(a) * radius;
                float wy = _cursorWorld.Y + MathF.Sin(a) * radius;
                var p = new Vector3(wx, wy, W.HeightAtWorld(wx, wy) + lift);
                if (i > 0) _lines.AddLine(prev, p, c);
                prev = p;
            }
        }

        // sombra oscura debajo del anillo: se lee sobre pasto claro y sobre roca oscura
        Ring(r + 6f, Color.FromArgb(110, 0, 0, 0), 6f);
        Ring(r, col, 9f);

        // anillo interior: donde el pincel pega con toda la fuerza
        float inner = r * State.BrushHardness;
        if (inner > 20f) Ring(inner, Color.FromArgb(110, col), 9f);

        // cruz y marca de altura en el centro
        float k = Math.Clamp(r * 0.12f, 25f, 70f);
        var c0 = new Vector3(_cursorWorld.X, _cursorWorld.Y, _cursorWorld.Z + 10f);
        _lines.AddLine(c0 - new Vector3(k, 0, 0), c0 + new Vector3(k, 0, 0), Color.FromArgb(200, col));
        _lines.AddLine(c0 - new Vector3(0, k, 0), c0 + new Vector3(0, k, 0), Color.FromArgb(200, col));
        if (State.Mode == EditMode.Height)
            _lines.AddLine(c0, c0 + new Vector3(0, 0, Math.Clamp(r * 0.35f, 60f, 260f)), Color.FromArgb(170, col));
    }

    // ------------------------------------------------------------ mouse
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        _lastMouse = e.Location;

        if (e.Button == MouseButtons.Left && State.ShowMinimap && _minimapRect.Contains(e.Location) && HasWorld)
        {
            _minimapDrag = true;
            MinimapJump(e.Location, animate: true);
            return;
        }
        if (e.Button == MouseButtons.Right) { _dragCamera = true; _camAnim = false; return; }
        if (e.Button == MouseButtons.Middle) { StartPan(e); return; }
        if (e.Button != MouseButtons.Left) return;

        // el cuentagotas se lleva el clic entero
        if (TrySample(e)) return;

        // con la barra espaciadora se arrastra el mapa desde cualquier herramienta
        if (_spaceDown) { StartPan(e); return; }

        // en Ver, el boton izquierdo navega segun el modo de la barra (Mover / Girar),
        // igual que los botones Move y Rotate del editor original
        if (State.Mode == EditMode.View)
        {
            _clickStart = e.Location;
            _maybeClick = true;
            if (State.Navigation == NavMode.Rotate) _dragCamera = true;
            else StartPan(e);
            return;
        }

        UpdateCursor(e.Location);

        if (State.Mode == EditMode.Objects)
        {
            HandleObjectClick(e);
            return;
        }

        if (State.Mode == EditMode.Area)
        {
            if (!_cursorValid) return;
            State.AreaX0 = State.AreaX1 = _cursorCellX;
            State.AreaY0 = State.AreaY1 = _cursorCellY;
            _markingArea = true;
            AreaChanged?.Invoke();
            Invalidate();
            return;
        }

        if (IsBrushMode && _cursorValid)
        {
            BeginStroke();
            ApplyBrush();
            _painting = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int dx = e.X - _lastMouse.X, dy = e.Y - _lastMouse.Y;

        if (_minimapDrag)
        {
            MinimapJump(e.Location, animate: false);
            _lastMouse = e.Location;
            return;
        }

        if (_dragCamera)
        {
            float speed = 0.0035f * State.CameraSpeed;
            Camera.Yaw -= dx * speed;
            Camera.Pitch += dy * speed;
            _lastMouse = e.Location;
            Invalidate();
            return;
        }

        if (_panCamera)
        {
            DoPan(e);
            _lastMouse = e.Location;
            Invalidate();
            return;
        }

        if (_gizmoDrag) { DragGizmo(e); _lastMouse = e.Location; Invalidate(); return; }

        UpdateCursor(e.Location);
        _lastMouse = e.Location;
        UpdateHover(e);

        // resaltar la flecha que esta debajo del mouse
        if (State.Mode == EditMode.Objects && _gizmo.Visible && e.Button == MouseButtons.None)
        {
            var hover = _gizmo.Pick(Camera, e.X, e.Y, Width, Height, State.Gizmo);
            if (hover != _gizmo.Hover) { _gizmo.Hover = hover; Invalidate(); }
            Cursor = hover != GizmoAxis.None ? Cursors.Hand : Cursors.Default;
        }

        if (_markingArea && _cursorValid)
        {
            State.AreaX1 = _cursorCellX;
            State.AreaY1 = _cursorCellY;
            AreaChanged?.Invoke();
        }
        else if (_painting) ApplyBrush();
        else if (_dragObjects) DragObjects(e);

        Invalidate();
        StatusChanged?.Invoke();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_minimapDrag && e.Button == MouseButtons.Left) { _minimapDrag = false; return; }
        if (e.Button == MouseButtons.Right) _dragCamera = false;
        if (e.Button == MouseButtons.Middle) _panCamera = false;

        if (e.Button == MouseButtons.Left)
        {
            if (_maybeClick)
            {
                _maybeClick = false;
                int dx = Math.Abs(e.X - _clickStart.X), dy = Math.Abs(e.Y - _clickStart.Y);
                if (dx <= 3 && dy <= 3) PickFromView(e);
            }
            if (_panCamera) { _panCamera = false; Cursor = Cursors.Default; }
            if (_gizmoDrag) EndGizmoDrag();
            if (_markingArea) { _markingArea = false; AreaChanged?.Invoke(); }
            if (_painting) { EndStroke(); _painting = false; }
            if (_dragObjects) { EndObjectDrag(); _dragObjects = false; }
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (ModifierKeys.HasFlag(Keys.Control) && IsBrushMode)
        {
            State.BrushRadius = Math.Clamp(State.BrushRadius + (e.Delta > 0 ? 1 : -1), 0.5f, 40f);
            State.RaiseChanged();
            return;
        }
        // el zoom acompana la velocidad elegida y no pega saltos
        _camAnim = false;
        float step = 0.10f * State.ZoomSpeed;
        Camera.Distance *= e.Delta > 0 ? (1f - step) : (1f + step);
        Invalidate();
    }

    // --------------------------------------------------- arrastre del mapa
    private Vector3 _panAnchor;
    private float _panPlaneZ;

    /// <summary>
    /// Empieza a arrastrar el mapa. Se guarda el punto del piso que quedo bajo
    /// el cursor: a partir de ahi ese punto sigue al mouse, asi el mapa se mueve
    /// exactamente lo que se mueve la mano y no a una velocidad inventada.
    /// </summary>
    internal void StartPan(MouseEventArgs e)
    {
        _camAnim = false;
        _panCamera = true;
        Cursor = Cursors.SizeAll;

        Camera.Update(Width, Height);
        Camera.ScreenRay(e.X, e.Y, Width, Height, out var ro, out var rd);

        if (RayTerrain(ro, rd, out var hit))
        {
            _panAnchor = hit;
            _panPlaneZ = hit.Z;
        }
        else
        {
            _panPlaneZ = Camera.Target.Z;
            _panAnchor = PlanePoint(ro, rd, _panPlaneZ) ?? Camera.Target;
        }
    }

    internal void DoPan(MouseEventArgs e)
    {
        Camera.Update(Width, Height);
        Camera.ScreenRay(e.X, e.Y, Width, Height, out var ro, out var rd);

        var now = PlanePoint(ro, rd, _panPlaneZ);
        if (now == null) return;

        var delta = _panAnchor - now.Value;
        Camera.Target = new Vector3(
            Math.Clamp(Camera.Target.X + delta.X * State.CameraSpeed, -5000, 30000),
            Math.Clamp(Camera.Target.Y + delta.Y * State.CameraSpeed, -5000, 30000),
            Camera.Target.Z);
    }

    /// <summary>Donde cruza el rayo el plano horizontal a la altura dada.</summary>
    private static Vector3? PlanePoint(Vector3 origin, Vector3 dir, float z)
    {
        if (MathF.Abs(dir.Z) < 1e-5f) return null;
        float t = (z - origin.Z) / dir.Z;
        if (t <= 0) return null;
        return origin + dir * t;
    }

    // ------------------------------------------------------- interseccion
    private void UpdateCursor(Point p)
    {
        Camera.Update(Width, Height);
        Camera.ScreenRay(p.X, p.Y, Width, Height, out var origin, out var dir);
        _cursorValid = RayTerrain(origin, dir, out _cursorWorld);
        if (_cursorValid)
        {
            _cursorCellX = (int)(_cursorWorld.X / MuMath.TerrainScale);
            _cursorCellY = (int)(_cursorWorld.Y / MuMath.TerrainScale);
            int celda = MuWorld.Index(MuMath.Clamp(_cursorCellX, 0, MuWorld.Size - 1),
                                      MuMath.Clamp(_cursorCellY, 0, MuWorld.Size - 1));
            string detalle = "";
            if (State.Mode is EditMode.Tile or EditMode.Alpha)
            {
                byte t2 = W.Layer2[celda];
                string capa2 = t2 == TileCatalog.None
                    ? App.Lang.T("ui.capa2_vacia", "capa 2: vacia")
                    : $"{App.Lang.T("ui.capa2", "capa 2")}: {Tiles?.Get(t2)?.Name ?? t2.ToString()}, " +
                      $"{App.Lang.T("ui.alfa", "alfa")} {W.Alpha[celda]}";
                detalle = "   |   " + App.Lang.T("ui.capa1", "capa 1") + ": " +
                          (Tiles?.Get(W.Layer1[celda])?.Name ?? W.Layer1[celda].ToString()) + "   |   " + capa2;
            }
            else if (State.Mode == EditMode.Attribute)
            {
                ushort att = W.Attribute[celda];
                detalle = "   |   " + App.Lang.T("ui.atributo", "atributo") + ": " +
                          (att == 0 ? App.Lang.T("ui.libre", "libre") : "0x" + att.ToString("X4"));
            }

            StatusText = $"X: {_cursorCellX}   Y: {_cursorCellY}   {App.Lang.T("ui.altura_barra", "Altura")}: {W.HeightAtWorld(_cursorWorld.X, _cursorWorld.Y):0}{detalle}";
        }
        else StatusText = "";
        StatusChanged?.Invoke();
    }

    /// <summary>Avanza por el rayo hasta cruzar el terreno.</summary>
    internal bool RayTerrain(Vector3 origin, Vector3 dir, out Vector3 hit)
    {
        hit = default;
        float step = 30f;
        float maxDist = 60000f;
        float prevDiff = origin.Z - W.HeightAtWorld(origin.X, origin.Y);

        for (float t = step; t < maxDist; t += step)
        {
            var p = origin + dir * t;
            if (p.X < -2000 || p.Y < -2000 || p.X > 28000 || p.Y > 28000)
            {
                if (dir.X == 0 && dir.Y == 0) return false;
                if (p.Z < -3000) return false;
                continue;
            }

            float diff = p.Z - W.HeightAtWorld(p.X, p.Y);
            if (diff <= 0 && prevDiff > 0)
            {
                // refinamos con biseccion para que el cursor no tiemble
                float lo = t - step, hi = t;
                for (int i = 0; i < 24; i++)
                {
                    float mid = (lo + hi) * 0.5f;
                    var q = origin + dir * mid;
                    if (q.Z - W.HeightAtWorld(q.X, q.Y) > 0) lo = mid; else hi = mid;
                }
                hit = origin + dir * hi;
                return true;
            }
            prevDiff = diff;
            if (t > 8000) step = 120f;
        }
        return false;
    }

    // ------------------------------------------------------------ pincel
    private void BeginStroke()
    {
        switch (State.Mode)
        {
            case EditMode.Height:
                _strokeHeight = new ArrayStroke<float>("Altura", W.Height); break;
            case EditMode.Attribute:
                _strokeShort = new ArrayStroke<ushort>("Atributos", W.Attribute); break;
            case EditMode.Tile:
                _strokeByte = new ArrayStroke<byte>("Tiles", State.TileLayer == 1 ? W.Layer1 : W.Layer2);
                _strokeGroup = new List<ArrayStroke<byte>> { new("Alfa", W.Alpha) };
                break;
            case EditMode.Alpha:
                _strokeByte = new ArrayStroke<byte>("Transparencia", W.Alpha);
                _strokeGroup = new List<ArrayStroke<byte>> { new("Capa 2", W.Layer2) };
                break;
        }
    }

    private void EndStroke()
    {
        var parts = new List<IUndoable>();
        if (_strokeByte is { Count: > 0 }) parts.Add(_strokeByte);
        if (_strokeHeight is { Count: > 0 }) parts.Add(_strokeHeight);
        if (_strokeShort is { Count: > 0 }) parts.Add(_strokeShort);
        if (_strokeGroup != null)
            foreach (var s in _strokeGroup) if (s.Count > 0) parts.Add(s);

        if (parts.Count == 1) History.Push(parts[0]);
        else if (parts.Count > 1) History.Push(new CompositeAction(parts[0].Name, parts));

        _strokeByte = null; _strokeHeight = null; _strokeShort = null; _strokeGroup = null;
    }

    private void ApplyBrush()
    {
        if (!_cursorValid) return;

        float cx = _cursorWorld.X / MuMath.TerrainScale;
        float cy = _cursorWorld.Y / MuMath.TerrainScale;
        float r = State.BrushRadius;
        int x0 = (int)MathF.Floor(cx - r), x1 = (int)MathF.Ceiling(cx + r);
        int y0 = (int)MathF.Floor(cy - r), y1 = (int)MathF.Ceiling(cy + r);
        x0 = Math.Max(0, x0); y0 = Math.Max(0, y0);
        x1 = Math.Min(MuWorld.Size - 1, x1); y1 = Math.Min(MuWorld.Size - 1, y1);

        bool inverse = ModifierKeys.HasFlag(Keys.Shift);

        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                float d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                if (d > r) continue;
                float w = Falloff(d / r, State.BrushHardness);
                if (w <= 0.001f) continue;

                int i = MuWorld.Index(x, y);
                switch (State.Mode)
                {
                    case EditMode.Height: ApplyHeight(i, x, y, w, inverse); break;
                    case EditMode.Attribute: ApplyAttribute(i, inverse); break;
                    case EditMode.Tile: ApplyTile(i, w); break;
                    case EditMode.Alpha: ApplyAlpha(i, w, inverse); break;
                }
            }

        _terrain.InvalidateCells(x0, y0, x1, y1);
        W.Dirty = true;
        Invalidate();
    }

    private static float Falloff(float t, float hardness)
    {
        if (t <= hardness) return 1f;
        float k = (t - hardness) / Math.Max(0.0001f, 1f - hardness);
        return 1f - k * k * (3f - 2f * k);   // curva suave
    }

    private void ApplyHeight(int i, int x, int y, float w, bool inverse)
    {
        // todo en unidades de mundo: sirve igual para el formato clasico
        // (pasos de 1.5) y para el de 24 bits de los mapas nuevos
        float old = W.Height[i];
        float value = old;
        float force = State.BrushStrength * 4f * MuMath.HeightScale * w;

        switch (State.Height)
        {
            case HeightMode.Raise:
                value += inverse ? -force : force;
                break;
            case HeightMode.Smooth:
            {
                float sum = 0; int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = Math.Clamp(x + dx, 0, MuWorld.Size - 1);
                        int ny = Math.Clamp(y + dy, 0, MuWorld.Size - 1);
                        sum += W.Height[MuWorld.Index(nx, ny)]; n++;
                    }
                value = old + (sum / n - old) * Math.Min(1f, State.BrushStrength * w * 2f);
                break;
            }
            case HeightMode.Flatten:
                value = old + (State.FlattenTarget - old) * Math.Min(1f, State.BrushStrength * w * 2f);
                break;
            case HeightMode.Set:
                value = State.FlattenTarget;
                break;
        }

        float lo = Math.Max(State.HeightMin, W.MinHeight), hi = Math.Min(State.HeightMax, W.MaxHeight);
        float nv = W.Quantize(Math.Clamp(value, Math.Min(lo, hi), Math.Max(lo, hi)));
        if (nv == old) return;
        W.Height[i] = nv;
        _strokeHeight?.Record(i, old, nv);
    }

    private void ApplyAttribute(int i, bool inverse)
    {
        ushort old = W.Attribute[i];
        ushort nv = State.AttributeAction == AttributeMode.Add && !inverse
            ? (ushort)(old | (ushort)State.AttributeFlags)
            : (ushort)(old & ~(ushort)State.AttributeFlags);
        if (nv == old) return;
        W.Attribute[i] = nv;
        _strokeShort?.Record(i, old, nv);
    }

    private void ApplyTile(int i, float w)
    {
        if (w < 0.35f) return;   // el borde suave no pinta a medias
        var target = State.TileLayer == 1 ? W.Layer1 : W.Layer2;
        byte old = target[i];
        byte nv = (byte)State.TileId;
        if (nv != old)
        {
            target[i] = nv;
            _strokeByte?.Record(i, old, nv);
        }

        if (State.TileLayer == 2 && W.Alpha[i] == 0)
        {
            byte oa = W.Alpha[i];
            W.Alpha[i] = 255;
            _strokeGroup?[0].Record(i, oa, 255);
        }
    }

    private void ApplyAlpha(int i, float w, bool inverse)
    {
        bool sumando = State.AlphaAdd && !inverse;

        // sin textura en la capa 2 no hay nada que mostrar: si el usuario quiere,
        // se la ponemos con el tile elegido en la herramienta Tiles
        if (sumando && State.AlphaFillsLayer2 && W.Layer2[i] == TileCatalog.None)
        {
            byte oldTile = W.Layer2[i];
            byte nuevoTile = (byte)State.TileId;
            W.Layer2[i] = nuevoTile;
            _strokeGroup?[0].Record(i, oldTile, nuevoTile);
        }

        byte old = W.Alpha[i];
        float delta = State.BrushStrength * 30f * w * (sumando ? 1 : -1);

        // que el borde suave igual avance: si el cambio es menor a 1, empujamos 1
        if (delta > 0 && delta < 1f) delta = 1f;
        if (delta < 0 && delta > -1f) delta = -1f;

        byte nv = (byte)Math.Clamp(old + delta, 0, 255);
        if (nv == old) return;
        W.Alpha[i] = nv;
        _strokeByte?.Record(i, old, nv);
    }

    private Point _clickStart;
    private bool _maybeClick;

    /// <summary>Avisa que se toco un objeto desde la pestana Ver.</summary>
    public event Action ObjectPickedFromView;

    /// <summary>
    /// Un clic corto en la pestana Ver selecciona el objeto que tocaste, sin
    /// tener que cambiar de herramienta primero.
    /// </summary>
    private void PickFromView(MouseEventArgs e)
    {
        Camera.ScreenRay(e.X, e.Y, Width, Height, out var ro, out var rd);
        var hit = PickObject(ro, rd);
        if (hit == null) return;

        State.Selection.Clear();
        State.Selection.Add(hit);
        SelectionChanged?.Invoke();
        if (State.JumpToObjectOnPick) ObjectPickedFromView?.Invoke();
        Invalidate();
    }

    /// <summary>
    /// Dibuja un modelo suelto y devuelve la imagen, para mostrar en el panel
    /// que objeto es. Usa el mismo contexto de la vista 3D: pinta en el buffer
    /// de atras, lee los pixeles y no lo muestra en pantalla, asi que el mapa
    /// no se ve afectado.
    /// </summary>
    public unsafe Bitmap RenderModelPreview(int type, int width, int height)
    {
        if (!GlReady || _shader == null || Catalog == null) return null;

        var entry = Catalog.Get(type);
        if (entry?.FullPath == null) return null;

        MakeCurrent();
        var model = _models.Get(entry.FullPath, Catalog.SearchDirs().ToArray());
        if (model == null || model.Meshes.Count == 0) return null;

        // el modelo puede estar en cualquier parte y con cualquier tamano:
        // encuadramos la camara a su caja
        var min = model.Min;
        var max = model.Max;
        var centro = (min + max) * 0.5f;
        float radio = MathF.Max(0.001f, (max - min).Length() * 0.5f);

        var cam = new Camera
        {
            Target = centro,
            Distance = radio * 2.6f,
            Yaw = 45f * MathF.PI / 180f,
            Pitch = 22f * MathF.PI / 180f,
            Fov = 40f * MathF.PI / 180f,
            Near = MathF.Max(1f, radio * 0.05f),
            Far = radio * 40f
        };
        cam.Update(width, height);

        Gl.Viewport(0, 0, width, height);
        Gl.ClearColor(18 / 255f, 20 / 255f, 25 / 255f, 1f);
        Gl.Clear(Gl.COLOR_BUFFER_BIT | Gl.DEPTH_BUFFER_BIT);

        _shader.Use();
        _shader.SetMvp(cam.ViewProjection);
        _shader.SetTime(0f);
        _shader.SetFog(false, Vector3.Zero, 0f, -1f, 1f);
        _shader.SetLighting(Color.FromArgb(40, 40, 46), Color.White);
        _models.Draw(_shader, model, Matrix4x4.Identity, Vector4.One);

        var bmp = ReadPixels(width, height);

        // dejamos todo como estaba y pedimos un redibujado normal
        Gl.Viewport(0, 0, Math.Max(1, Width), Math.Max(1, Height));
        Invalidate();
        return bmp;
    }

    private unsafe Bitmap ReadPixels(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        fixed (byte* p = pixels)
            Gl.ReadPixels(0, 0, width, height, Gl.RGBA, Gl.UNSIGNED_BYTE, p);

        var bmp = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, width, height),
                                System.Drawing.Imaging.ImageLockMode.WriteOnly,
                                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var row = new byte[width * 4];
        for (int y = 0; y < height; y++)
        {
            int src = (height - 1 - y) * width * 4;    // OpenGL entrega la imagen al reves
            for (int x = 0; x < width; x++)
            {
                row[x * 4 + 0] = pixels[src + x * 4 + 2];
                row[x * 4 + 1] = pixels[src + x * 4 + 1];
                row[x * 4 + 2] = pixels[src + x * 4 + 0];
                row[x * 4 + 3] = 255;
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, width * 4);
        }
        bmp.UnlockBits(data);
        return bmp;
    }

    // -------------------------------------------------------- cuentagotas
    public event Action<string> Sampled;

    /// <summary>
    /// Toma del mapa lo que haya bajo el cursor (altura, tile o tipo de objeto)
    /// y lo carga en la herramienta, para copiar algo que ya existe en el mapa.
    /// </summary>
    private bool TrySample(MouseEventArgs e)
    {
        if (State.Sample == SampleMode.None) return false;
        UpdateCursor(e.Location);

        string what = null;
        switch (State.Sample)
        {
            case SampleMode.Height:
                if (!_cursorValid) return true;
                State.FlattenTarget = W.HeightAtWorld(_cursorWorld.X, _cursorWorld.Y);
                what = $"Altura tomada: {State.FlattenTarget:0}";
                break;

            case SampleMode.Tile:
            {
                if (!_cursorValid) return true;
                int i = MuWorld.Index(
                    MuMath.Clamp((int)(_cursorWorld.X / MuMath.TerrainScale), 0, MuWorld.Size - 1),
                    MuMath.Clamp((int)(_cursorWorld.Y / MuMath.TerrainScale), 0, MuWorld.Size - 1));
                byte tile = State.TileLayer == 1 ? W.Layer1[i] : W.Layer2[i];
                State.TileId = tile;
                what = $"Tile tomado: {tile} ({Tiles?.Get(tile)?.Name ?? "sin nombre"})";
                break;
            }

            case SampleMode.ObjectType:
            {
                Camera.ScreenRay(e.X, e.Y, Width, Height, out var ro, out var rd);
                var hit = PickObject(ro, rd);
                if (hit == null) { what = "Ahi no hay ningun objeto"; break; }
                State.NewObjectType = hit.Type;
                what = $"Objeto tomado: {Catalog?.DisplayName(hit.Type)} (tipo {hit.Type})";
                break;
            }
        }

        State.Sample = SampleMode.None;
        Cursor = Cursors.Default;
        Sampled?.Invoke(what);
        State.RaiseChanged();
        return true;
    }

    /// <summary>
    /// Pasa al siguiente objeto apilado en el mismo lugar. En MU es comun tener
    /// varios encimados y con el clic siempre agarras el de adelante.
    /// </summary>
    public void CycleDuplicate(int step)
    {
        if (State.Selection.Count != 1) return;
        var current = State.Selection[0];
        var apilados = StackedAt(current);
        if (apilados.Count < 2) return;

        int idx = apilados.IndexOf(current);
        idx = (idx + step + apilados.Count) % apilados.Count;

        State.Selection.Clear();
        State.Selection.Add(apilados[idx]);
        SelectionChanged?.Invoke();
        Invalidate();
    }

    private List<MapFiles.MapObject> StackedAt(MapFiles.MapObject c) =>
        W.Objects.Where(o => MathF.Abs(o.X - c.X) < 60f &&
                             MathF.Abs(o.Y - c.Y) < 60f &&
                             MathF.Abs(o.Z - c.Z) < 120f).ToList();

    /// <summary>Cuantos objetos comparten el lugar del seleccionado.</summary>
    public int DuplicatesAtSelection() =>
        State.Selection.Count == 1 ? StackedAt(State.Selection[0]).Count : 0;

    // -------------------------------------------------------------- area
    /// <summary>Se queda con todo lo que hay dentro del rectangulo elegido.</summary>
    public AreaClip CopyArea()
    {
        if (!State.HasArea) return null;
        var (x0, y0, x1, y1) = State.AreaOrdered();
        State.Clip = AreaClip.Copy(W, Tiles, Catalog, x0, y0, x1, y1);
        return State.Clip;
    }

    /// <summary>
    /// Pega lo copiado con la esquina en la celda que se le indique. Si viene de
    /// otro mapa, se copian ahi los modelos y las texturas que falten.
    /// </summary>
    public PasteReport PasteArea(int destX, int destY)
    {
        if (State.Clip == null || State.Clip.IsEmpty) return null;

        var accion = AreaPaste.Paste(State.Clip, W, Tiles, Catalog, destX, destY, State.Paste, out var report);
        if (accion != null)
        {
            History.Push(accion);
            _terrain.InvalidateAll();
            MakeCurrent();
            Invalidate();
            SelectionChanged?.Invoke();
        }
        return report;
    }

    /// <summary>Pega donde este el mouse, o en el centro de la vista.</summary>
    public PasteReport PasteAreaAtCursor()
    {
        int cx, cy;
        if (_cursorValid) { cx = _cursorCellX; cy = _cursorCellY; }
        else
        {
            cx = (int)(Camera.Target.X / MuMath.TerrainScale);
            cy = (int)(Camera.Target.Y / MuMath.TerrainScale);
        }
        return PasteArea(cx, cy);
    }

    /// <summary>Elige todo el mapa.</summary>
    public void SelectWholeMap()
    {
        State.AreaX0 = 0; State.AreaY0 = 0;
        State.AreaX1 = MuWorld.Size - 1; State.AreaY1 = MuWorld.Size - 1;
        AreaChanged?.Invoke();
        Invalidate();
    }

    // ------------------------------------------------------ vistas fijas
    public void SetCameraView(CameraView view)
    {
        switch (view)
        {
            case CameraView.Top:
                AnimateTo(Camera.Target, Camera.Distance, -MathF.PI / 2f, MathF.PI / 2f - 0.03f);
                break;
            case CameraView.Front:
                AnimateTo(Camera.Target, Camera.Distance, -MathF.PI / 2f, 0.12f);
                break;
            case CameraView.Side:
                AnimateTo(Camera.Target, Camera.Distance, MathF.PI, 0.12f);
                break;
        }
    }

    public void ResetCamera()
    {
        AnimateTo(new Vector3(12800, 12800, W.HeightAtWorld(12800, 12800)), 4000,
                  45f * MathF.PI / 180f, 45f * MathF.PI / 180f);
    }

    /// <summary>Lleva la camara de a poco al nuevo encuadre, en vez de saltar.</summary>
    public void AnimateTo(Vector3 target, float distance, float yaw, float pitch)
    {
        _fromTarget = Camera.Target; _fromDist = Camera.Distance;
        _fromYaw = Camera.Yaw; _fromPitch = Camera.Pitch;
        _toTarget = target; _toDist = distance; _toPitch = pitch;

        // el giro va por el camino mas corto
        float d = (yaw - _fromYaw) % MathF.Tau;
        if (d > MathF.PI) d -= MathF.Tau;
        if (d < -MathF.PI) d += MathF.Tau;
        _toYaw = _fromYaw + d;

        _camAnimStart = DateTime.Now;
        _camAnim = true;
        Invalidate();
    }

    private void StepCameraAnimation()
    {
        if (!_camAnim) return;
        float t = (float)((DateTime.Now - _camAnimStart).TotalSeconds / CamAnimSeconds);
        if (t >= 1f) { t = 1f; _camAnim = false; }
        float k = 1f - MathF.Pow(1f - t, 3f);          // sale rapido y frena suave
        Camera.Target = Vector3.Lerp(_fromTarget, _toTarget, k);
        Camera.Distance = _fromDist + (_toDist - _fromDist) * k;
        Camera.Yaw = _fromYaw + (_toYaw - _fromYaw) * k;
        Camera.Pitch = _fromPitch + (_toPitch - _fromPitch) * k;
    }

    // ----------------------------------------------------------- objetos
    /// <summary>Si el click cayo sobre una flecha del gizmo, empieza a arrastrarla.</summary>
    private bool TryGrabGizmo(MouseEventArgs e)
    {
        if (!_gizmo.Visible) return false;
        var axis = _gizmo.Pick(Camera, e.X, e.Y, Width, Height, State.Gizmo);
        if (axis == GizmoAxis.None) return false;

        _gizmo.Active = axis;
        _gizmoDrag = true;
        _gizmoStart = State.Selection.Select(o => (o, new TransformObjects.State(o))).ToList();

        _gizmo.AxisPoint(Camera, axis, e.X, e.Y, Width, Height, out _gizmoStartT);
        if (Gizmo.IsPlane(axis)) _gizmo.PlanePoint(Camera, axis, e.X, e.Y, Width, Height, out _gizmoStartPlane);
        _groundOffset.Clear();
        foreach (var o in State.Selection)
            _groundOffset[o] = o.Z - W.HeightAtWorld(o.X, o.Y);
        _gizmoStartAngle = _gizmo.ScreenAngle(Camera, e.X, e.Y, Width, Height);
        _gizmoStartRadius = _gizmo.ScreenRadius(Camera, e.X, e.Y, Width, Height);
        return true;
    }

    private void DragGizmo(MouseEventArgs e)
    {
        if (_gizmoStart == null || _gizmo.Active == GizmoAxis.None) return;
        var axis = _gizmo.Active;

        switch (State.Gizmo)
        {
            case GizmoMode.Move:
            {
                Vector3 dir;
                if (Gizmo.IsPlane(axis))
                {
                    if (!_gizmo.PlanePoint(Camera, axis, e.X, e.Y, Width, Height, out var now)) return;
                    dir = now - _gizmoStartPlane;
                }
                else
                {
                    if (!_gizmo.AxisPoint(Camera, axis, e.X, e.Y, Width, Height, out float t)) return;
                    dir = Gizmo.Direction(axis) * (t - _gizmoStartT);
                }

                foreach (var (o, before) in _gizmoStart)
                {
                    float nx = before.X + dir.X, ny = before.Y + dir.Y, nz = before.Z + dir.Z;
                    if (State.SnapToGrid && axis is not GizmoAxis.Z)
                    {
                        float g = Math.Max(1f, State.GridSnapSize);
                        nx = MathF.Round(nx / g) * g;
                        ny = MathF.Round(ny / g) * g;
                    }
                    o.X = nx; o.Y = ny;
                    bool vertical = axis is GizmoAxis.Z or GizmoAxis.XZ or GizmoAxis.YZ;
                    o.Z = (State.SnapToGround && !vertical)
                        ? W.HeightAtWorld(nx, ny) + (_groundOffset.TryGetValue(o, out var off) ? off : 0f)
                        : nz;
                }
                break;
            }

            case GizmoMode.Rotate:
            {
                float ang = _gizmo.ScreenAngle(Camera, e.X, e.Y, Width, Height);
                float deg = (ang - _gizmoStartAngle) * 180f / MathF.PI;
                if (State.SnapRotation && State.RotateSnapDegrees > 0.01f)
                    deg = MathF.Round(deg / State.RotateSnapDegrees) * State.RotateSnapDegrees;
                foreach (var (o, before) in _gizmoStart)
                {
                    o.AngleX = before.AX; o.AngleY = before.AY; o.AngleZ = before.AZ;
                    switch (axis)
                    {
                        case GizmoAxis.X: o.AngleX = before.AX + deg; break;
                        case GizmoAxis.Y: o.AngleY = before.AY + deg; break;
                        default: o.AngleZ = before.AZ + deg; break;
                    }
                }
                break;
            }

            case GizmoMode.Scale:
            {
                float r = _gizmo.ScreenRadius(Camera, e.X, e.Y, Width, Height);
                float factor = Math.Clamp(r / Math.Max(1f, _gizmoStartRadius), 0.05f, 20f);
                foreach (var (o, before) in _gizmoStart)
                {
                    float sc = before.Scale * factor;
                    if (State.SnapScale && State.ScaleSnapStep > 0.001f)
                        sc = MathF.Round(sc / State.ScaleSnapStep) * State.ScaleSnapStep;
                    o.Scale = Math.Clamp(sc, 0.05f, 20f);
                }
                break;
            }
        }

        W.Dirty = true;
        SelectionChanged?.Invoke();
    }

    private void EndGizmoDrag()
    {
        _gizmoDrag = false;
        _gizmo.Active = GizmoAxis.None;
        if (_gizmoStart == null) return;

        var items = _gizmoStart
            .Select(p => (p.obj, p.before, new TransformObjects.State(p.obj)))
            .Where(p => !Equals(p.before, p.Item3))
            .ToList();

        if (items.Count > 0)
        {
            string name = State.Gizmo switch
            {
                GizmoMode.Move => "Mover objeto",
                GizmoMode.Rotate => "Rotar objeto",
                _ => "Escalar objeto"
            };
            History.Push(new TransformObjects(name, items));
        }
        _gizmoStart = null;
    }

    private void HandleObjectClick(MouseEventArgs e)
    {
        if (TryGrabGizmo(e)) return;

        Camera.ScreenRay(e.X, e.Y, Width, Height, out var origin, out var dir);

        // Ctrl + boton izquierdo agrega un objeto, tal cual el editor original
        if (ModifierKeys.HasFlag(Keys.Control))
        {
            AddObjectAtCursor();
            return;
        }

        var hit = PickObject(origin, dir);
        if (hit == null)
        {
            // clic en el vacio: se navega igual que en la pestana Ver, para no
            // quedar clavado cuando estas trabajando con objetos
            if (!ModifierKeys.HasFlag(Keys.Shift))
            {
                State.Selection.Clear();
                SelectionChanged?.Invoke();
            }
            if (State.Navigation == NavMode.Rotate) _dragCamera = true;
            else StartPan(e);
            Invalidate();
            return;
        }

        if (ModifierKeys.HasFlag(Keys.Shift))
        {
            if (!State.Selection.Remove(hit)) State.Selection.Add(hit);
        }
        else if (!State.Selection.Contains(hit))
        {
            State.Selection.Clear();
            State.Selection.Add(hit);
        }

        SelectionChanged?.Invoke();
        BeginObjectDrag(e);
        Invalidate();
    }

    public MapFiles.MapObject PickObject(Vector3 origin, Vector3 dir)
    {
        MapFiles.MapObject best = null;
        float bestT = float.MaxValue;
        var dirs = Catalog?.SearchDirs().ToArray() ?? Array.Empty<string>();

        foreach (var o in W.Objects)
        {
            var entry = Catalog?.Get(o.Type);
            var model = entry?.FullPath != null ? _models.Get(entry.FullPath, dirs) : null;
            Vector3 lo = model != null ? model.Min * o.Scale : new Vector3(-60, -60, 0);
            Vector3 hi = model != null ? model.Max * o.Scale : new Vector3(60, 60, 160);

            var m = MuMath.AngleMatrixDegrees(new Vector3(o.AngleX, o.AngleY, o.AngleZ));
            m.M41 = o.X; m.M42 = o.Y; m.M43 = o.Z;
            if (!Matrix4x4.Invert(m, out var inv)) continue;

            var lOrigin = Vector3.Transform(origin, inv);
            var lDir = Vector3.TransformNormal(dir, inv);
            if (RayBox(lOrigin, lDir, lo, hi, out float t) && t < bestT)
            {
                bestT = t;
                best = o;
            }
        }
        return best;
    }

    private static bool RayBox(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float t)
    {
        t = 0;
        float tmin = 0.0001f, tmax = float.MaxValue;
        for (int a = 0; a < 3; a++)
        {
            float od = a == 0 ? d.X : a == 1 ? d.Y : d.Z;
            float oo = a == 0 ? o.X : a == 1 ? o.Y : o.Z;
            float lo = a == 0 ? min.X : a == 1 ? min.Y : min.Z;
            float hi = a == 0 ? max.X : a == 1 ? max.Y : max.Z;

            if (MathF.Abs(od) < 1e-6f)
            {
                if (oo < lo || oo > hi) return false;
                continue;
            }
            float t1 = (lo - oo) / od, t2 = (hi - oo) / od;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = MathF.Max(tmin, t1);
            tmax = MathF.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        t = tmin;
        return true;
    }

    private float _dragPlaneZ;
    private Point _dragStartScreen;
    private readonly Dictionary<MapFiles.MapObject, float> _groundOffset = new();

    private void BeginObjectDrag(MouseEventArgs e)
    {
        if (State.Selection.Count == 0) return;
        _dragStart = State.Selection.Select(o => (o, new TransformObjects.State(o))).ToList();
        _dragStartScreen = e.Location;

        // se arrastra sobre un plano horizontal a la altura del objeto, y no
        // sobre el terreno: mirando casi de costado, el punto donde el rayo
        // pega en el piso se va lejisimos y el objeto pegaria saltos.
        _dragPlaneZ = State.Selection.Average(o => o.Z);
        Camera.Update(Width, Height);
        Camera.ScreenRay(e.X, e.Y, Width, Height, out var ro, out var rd);
        _dragAnchor = PlanePoint(ro, rd, _dragPlaneZ) ?? _cursorWorld;

        // cada objeto recuerda a que altura estaba respecto del piso, asi no se
        // hunde de golpe si estaba colgado o arriba de algo
        _groundOffset.Clear();
        foreach (var o in State.Selection)
            _groundOffset[o] = o.Z - W.HeightAtWorld(o.X, o.Y);

        _dragObjects = true;
    }

    private void DragObjects(MouseEventArgs e)
    {
        if (_dragStart == null) return;

        Camera.Update(Width, Height);
        Camera.ScreenRay(e.X, e.Y, Width, Height, out var ro, out var rd);
        var now = PlanePoint(ro, rd, _dragPlaneZ);
        if (now == null) return;
        var delta = now.Value - _dragAnchor;

        foreach (var (o, before) in _dragStart)
        {
            switch (State.Gizmo)
            {
                case GizmoMode.Move:
                    float nx = before.X + delta.X, ny = before.Y + delta.Y;
                    if (State.SnapToGrid)
                    {
                        float g = Math.Max(1f, State.GridSnapSize);
                        nx = MathF.Round(nx / g) * g;
                        ny = MathF.Round(ny / g) * g;
                    }
                    o.X = nx; o.Y = ny;
                    o.Z = State.SnapToGround
                        ? W.HeightAtWorld(nx, ny) + (_groundOffset.TryGetValue(o, out var off) ? off : 0f)
                        : before.Z;
                    break;

                case GizmoMode.Rotate:
                    o.AngleZ = before.AZ + (e.X - _dragStartScreen.X) * 0.5f;
                    break;

                case GizmoMode.Scale:
                    o.Scale = Math.Clamp(before.Scale + delta.X * 0.0006f, 0.05f, 20f);
                    break;
            }
        }
        W.Dirty = true;
        SelectionChanged?.Invoke();
    }

    private void EndObjectDrag()
    {
        if (_dragStart == null) return;
        var items = _dragStart
            .Select(p => (p.obj, p.before, new TransformObjects.State(p.obj)))
            .Where(p => !Equals(p.before, p.Item3))
            .ToList();

        if (items.Count > 0)
        {
            string name = State.Gizmo switch
            {
                GizmoMode.Move => "Mover objeto",
                GizmoMode.Rotate => "Rotar objeto",
                _ => "Escalar objeto"
            };
            History.Push(new TransformObjects(name, items));
        }
        _dragStart = null;
    }

    public void AddObjectAtCursor()
    {
        if (!_cursorValid) return;
        var o = new MapFiles.MapObject
        {
            Type = (short)State.NewObjectType,
            X = _cursorWorld.X,
            Y = _cursorWorld.Y,
            Z = W.HeightAtWorld(_cursorWorld.X, _cursorWorld.Y),
            AngleX = 0, AngleY = 0, AngleZ = 0,
            Scale = 1f,
            Extra = W.NewObjectExtra()
        };
        W.Objects.Add(o);
        History.Push(new AddObjects(W.Objects, new List<MapFiles.MapObject> { o }));
        State.Selection.Clear();
        State.Selection.Add(o);
        SelectionChanged?.Invoke();
        Invalidate();
    }

    public void DeleteSelection()
    {
        if (State.Selection.Count == 0) return;
        var removed = State.Selection
            .Select(o => (W.Objects.IndexOf(o), o))
            .Where(p => p.Item1 >= 0)
            .ToList();
        foreach (var (_, o) in removed) W.Objects.Remove(o);
        History.Push(new RemoveObjects(W.Objects, removed));
        State.Selection.Clear();
        SelectionChanged?.Invoke();
        Invalidate();
    }

    public void CopySelection()
    {
        State.Clipboard.Clear();
        foreach (var o in State.Selection) State.Clipboard.Add(o.Clone());
    }

    public void PasteClipboard()
    {
        if (State.Clipboard.Count == 0) return;
        var added = new List<MapFiles.MapObject>();

        // pega centrado donde esta el mouse
        var center = new Vector3(State.Clipboard.Average(o => o.X), State.Clipboard.Average(o => o.Y), 0);
        var target = _cursorValid ? _cursorWorld : center;

        foreach (var src in State.Clipboard)
        {
            var o = src.Clone();
            float offset = src.Z - W.HeightAtWorld(src.X, src.Y);
            o.X += target.X - center.X;
            o.Y += target.Y - center.Y;
            if (State.SnapToGround) o.Z = W.HeightAtWorld(o.X, o.Y) + offset;
            W.Objects.Add(o);
            added.Add(o);
        }

        History.Push(new AddObjects(W.Objects, added));
        State.Selection.Clear();
        State.Selection.AddRange(added);
        SelectionChanged?.Invoke();
        Invalidate();
    }

    public void DuplicateSelection()
    {
        if (State.Selection.Count == 0) return;
        var added = new List<MapFiles.MapObject>();
        foreach (var src in State.Selection)
        {
            var o = src.Clone();
            float offset = src.Z - W.HeightAtWorld(src.X, src.Y);
            o.X += MuMath.TerrainScale;
            o.Y += MuMath.TerrainScale;
            // queda a la misma altura respecto del piso que el original
            if (State.SnapToGround) o.Z = W.HeightAtWorld(o.X, o.Y) + offset;
            W.Objects.Add(o);
            added.Add(o);
        }
        History.Push(new AddObjects(W.Objects, added));
        State.Selection.Clear();
        State.Selection.AddRange(added);
        SelectionChanged?.Invoke();
        Invalidate();
    }

    public void DropSelectionToGround()
    {
        if (State.Selection.Count == 0) return;
        var items = new List<(MapFiles.MapObject, TransformObjects.State, TransformObjects.State)>();
        foreach (var o in State.Selection)
        {
            var before = new TransformObjects.State(o);
            o.Z = W.HeightAtWorld(o.X, o.Y);
            items.Add((o, before, new TransformObjects.State(o)));
        }
        History.Push(new TransformObjects("Apoyar en el piso", items));
        Invalidate();
    }

    public void FocusOnSelection()
    {
        if (State.Selection.Count == 0) return;
        var c = new Vector3(State.Selection.Average(o => o.X), State.Selection.Average(o => o.Y), State.Selection.Average(o => o.Z));
        AnimateTo(c, 1200, Camera.Yaw, Camera.Pitch);
    }

    public void FocusCell(int x, int y)
    {
        AnimateTo(new Vector3(x * MuMath.TerrainScale, y * MuMath.TerrainScale, W.HeightAt(x, y)),
                  Camera.Distance, Camera.Yaw, Camera.Pitch);
    }

    protected override bool IsInputKey(Keys keyData) => true;

    // ---------------------------------------------------------- teclado
    private bool _spaceDown;

    /// <summary>Desplaza el mapa una tanda, en pasos relativos al zoom.</summary>
    public void PanBy(float dx, float dy)
    {
        float k = Camera.Distance * 0.04f * State.CameraSpeed;
        Camera.Pan(dx * k, dy * k);
        Invalidate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.KeyCode)
        {
            case Keys.Space:
                _spaceDown = true;
                Cursor = Cursors.SizeAll;
                e.Handled = true;
                break;

            case Keys.Left: case Keys.A: PanBy(-1, 0); e.Handled = true; break;
            case Keys.Right: case Keys.D: PanBy(1, 0); e.Handled = true; break;
            case Keys.Up: case Keys.W: PanBy(0, 1); e.Handled = true; break;
            case Keys.Down: case Keys.S: PanBy(0, -1); e.Handled = true; break;

            case Keys.Home: ResetCamera(); e.Handled = true; break;

            case Keys.Add: case Keys.Oemplus:
                Camera.Distance *= 0.85f; Invalidate(); e.Handled = true; break;
            case Keys.Subtract: case Keys.OemMinus:
                Camera.Distance *= 1.18f; Invalidate(); e.Handled = true; break;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode == Keys.Space)
        {
            _spaceDown = false;
            if (!_panCamera) Cursor = Cursors.Default;
        }
    }


    // ------------------------------------------------------------ hover
    private void UpdateHover(MouseEventArgs e)
    {
        MapFiles.MapObject h = null;
        if (State.HoverHighlight && e.Button == MouseButtons.None && State.ShowObjects &&
            State.Mode is EditMode.View or EditMode.Objects &&
            !(State.ShowMinimap && _minimapRect.Contains(e.Location)) &&
            (_gizmo.Hover == GizmoAxis.None))
        {
            Camera.ScreenRay(e.X, e.Y, Width, Height, out var ro, out var rd);
            h = PickObject(ro, rd);
        }
        if (!ReferenceEquals(h, HoverObject)) { HoverObject = h; Invalidate(); }
    }

    // ---------------------------------------------------------- minimapa
    private const int MinimapSize = 184;

    /// <summary>Vuelve a armar la imagen del minimapa (piso visto desde arriba).</summary>
    private void RebuildMinimap()
    {
        _minimapDirty = false;
        _minimapBuilt = DateTime.Now;
        if (!HasWorld || Tiles == null) return;
        try
        {
            using var bmp = WorldMapExport.Render(W, Tiles, WorldMapExport.Layer.Tiles, 1);
            var tex = BitmapToTexture(bmp);
            _minimapTex?.Dispose();
            _minimapTex = GpuTexture.FromMu(tex, false);
        }
        catch { /* sin minimapa no pasa nada */ }
    }

    /// <summary>Copia una imagen de GDI a una textura RGBA lista para OpenGL.</summary>
    public static MuTexture BitmapToTexture(Bitmap bmp)
    {
        var tex = new MuTexture { Width = bmp.Width, Height = bmp.Height, Pixels = new byte[bmp.Width * bmp.Height * 4] };
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height),
                                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var row = new byte[bmp.Width * 4];
        for (int y = 0; y < bmp.Height; y++)
        {
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
            for (int x = 0; x < bmp.Width; x++)
            {
                int d = (y * bmp.Width + x) * 4;
                tex.Pixels[d] = row[x * 4 + 2];
                tex.Pixels[d + 1] = row[x * 4 + 1];
                tex.Pixels[d + 2] = row[x * 4];
                tex.Pixels[d + 3] = 255;
            }
        }
        bmp.UnlockBits(data);
        return tex;
    }

    private void MinimapJump(Point p, bool animate)
    {
        var r = _minimapRect;
        float u = Math.Clamp((p.X - r.X) / r.Width, 0f, 1f);
        float v = Math.Clamp((p.Y - r.Y) / r.Height, 0f, 1f);
        float wx = u * MuWorld.Size * MuMath.TerrainScale;
        float wy = v * MuWorld.Size * MuMath.TerrainScale;
        var target = new Vector3(wx, wy, W.HeightAtWorld(wx, wy));
        if (animate) AnimateTo(target, Camera.Distance, Camera.Yaw, Camera.Pitch);
        else { _camAnim = false; Camera.Target = target; Invalidate(); }
    }

    /// <summary>Todo lo que va pegado a la pantalla: minimapa y ejes.</summary>
    private void DrawHud()
    {
        if (!HasWorld) return;

        _shader.SetMvp(Matrix4x4.CreateOrthographicOffCenter(0, Math.Max(1, Width), Math.Max(1, Height), 0, -1, 1));
        _shader.SetFog(false, Vector3.Zero, 0f, -1f, 1f);
        _shader.SetLighting(Color.Black, Color.White);
        _shader.SetWater(false);
        Gl.Disable(Gl.DEPTH_TEST);

        if (State.ShowMinimap) DrawMinimap();
        else _minimapRect = RectangleF.Empty;
        if (State.ShowAxisHud) DrawAxisHud();

        Gl.Enable(Gl.DEPTH_TEST);
        _shader.SetMvp(Camera.ViewProjection);
    }

    private void DrawMinimap()
    {
        if (_minimapDirty && (DateTime.Now - _minimapBuilt).TotalSeconds > 1.2) RebuildMinimap();
        if (_minimapTex == null) return;

        float size = Math.Min(MinimapSize, Math.Min(Width, Height) * 0.32f);
        if (size < 80) { _minimapRect = RectangleF.Empty; return; }
        var r = new RectangleF(Width - size - 14, 14, size, size);
        _minimapRect = r;

        _quads.Clear();
        _lines.Clear();
        // marco con sombra
        _quads.AddQuad(new Vector3(r.Left - 5, r.Top - 5, 0), new Vector3(r.Right + 5, r.Top - 5, 0),
                       new Vector3(r.Right + 5, r.Bottom + 5, 0), new Vector3(r.Left - 5, r.Bottom + 5, 0), Color.FromArgb(200, 12, 14, 18));
        _quads.Draw(_shader);
        _quads.Clear();

        _texQuad.Draw(_shader, _minimapTex, r, Color.FromArgb(235, 255, 255, 255));

        // lo que ve la camara, proyectado sobre el piso
        var pts = new List<Vector2>();
        foreach (var (sx, sy) in new[] { (0, 0), (Width, 0), (Width, Height), (0, Height) })
        {
            Camera.ScreenRay(sx, sy, Width, Height, out var ro, out var rd);
            Vector3 hit;
            if (rd.Z < -0.02f) hit = ro + rd * ((Camera.Target.Z - ro.Z) / rd.Z);
            else hit = ro + Vector3.Normalize(new Vector3(rd.X, rd.Y, 0.0001f)) * 20000f;
            var flat = new Vector2(hit.X, hit.Y) - new Vector2(Camera.Target.X, Camera.Target.Y);
            if (flat.Length() > 20000f) flat = Vector2.Normalize(flat) * 20000f;
            hit = new Vector3(Camera.Target.X + flat.X, Camera.Target.Y + flat.Y, 0);
            pts.Add(ToMinimap(hit, r));
        }
        var view = Color.FromArgb(230, DarkTheme.Accent);
        for (int i = 0; i < 4; i++)
        {
            var a = pts[i]; var b = pts[(i + 1) % 4];
            a = Vector2.Clamp(a, new Vector2(r.Left, r.Top), new Vector2(r.Right, r.Bottom));
            b = Vector2.Clamp(b, new Vector2(r.Left, r.Top), new Vector2(r.Right, r.Bottom));
            _lines.AddLine(new Vector3(a, 0), new Vector3(b, 0), view);
        }
        var c = ToMinimap(Camera.Target, r);
        _quads.AddQuad(new Vector3(c.X - 3, c.Y - 3, 0), new Vector3(c.X + 3, c.Y - 3, 0),
                       new Vector3(c.X + 3, c.Y + 3, 0), new Vector3(c.X - 3, c.Y + 3, 0), DarkTheme.Accent);

        // borde
        var border = Color.FromArgb(255, DarkTheme.Border);
        _lines.AddLine(new Vector3(r.Left, r.Top, 0), new Vector3(r.Right, r.Top, 0), border);
        _lines.AddLine(new Vector3(r.Right, r.Top, 0), new Vector3(r.Right, r.Bottom, 0), border);
        _lines.AddLine(new Vector3(r.Right, r.Bottom, 0), new Vector3(r.Left, r.Bottom, 0), border);
        _lines.AddLine(new Vector3(r.Left, r.Bottom, 0), new Vector3(r.Left, r.Top, 0), border);

        _quads.Draw(_shader);
        _lines.Draw(_shader, 1.6f);
        _quads.Clear();
        _lines.Clear();
    }

    private static Vector2 ToMinimap(Vector3 world, RectangleF r)
    {
        float total = MuWorld.Size * MuMath.TerrainScale;
        return new Vector2(r.X + world.X / total * r.Width, r.Y + world.Y / total * r.Height);
    }

    /// <summary>Ejes X (rojo), Y (verde) y Z (azul) en la esquina, girando con la camara.</summary>
    private void DrawAxisHud()
    {
        var center = new Vector2(46, Height - 46);
        const float len = 26f;
        _lines.Clear();
        _quads.Clear();

        var axes = new[]
        {
            (Vector3.UnitX, Color.FromArgb(235, 90, 90), 'X'),
            (Vector3.UnitY, Color.FromArgb(110, 220, 110), 'Y'),
            (Vector3.UnitZ, Color.FromArgb(100, 160, 255), 'Z')
        };

        // fondo redondo
        const int seg = 28;
        for (int i = 0; i < seg; i++)
        {
            float a0 = i / (float)seg * MathF.Tau, a1 = (i + 1) / (float)seg * MathF.Tau;
            var p0 = center + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * 38f;
            var p1 = center + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * 38f;
            _quads.AddQuad(new Vector3(center, 0), new Vector3(p0, 0), new Vector3(p1, 0), new Vector3(center, 0), Color.FromArgb(120, 12, 14, 18));
        }

        foreach (var (axis, color, letter) in axes)
        {
            var v = Vector3.TransformNormal(axis, Camera.View);
            var tip = center + new Vector2(v.X, -v.Y) * len;
            _lines.AddLine(new Vector3(center, 0), new Vector3(tip, 0), color);
            var lp = center + new Vector2(v.X, -v.Y) * (len + 8f);
            Letter(letter, lp, color);
        }
        _quads.Draw(_shader);
        _lines.Draw(_shader, 2f);
        _lines.Clear();
        _quads.Clear();
    }

    /// <summary>Letras X, Y, Z dibujadas con rayitas (sin fuentes en OpenGL).</summary>
    private void Letter(char ch, Vector2 c, Color col)
    {
        const float h = 4f;
        void L(float x0, float y0, float x1, float y1) =>
            _lines.AddLine(new Vector3(c.X + x0, c.Y + y0, 0), new Vector3(c.X + x1, c.Y + y1, 0), col);
        switch (ch)
        {
            case 'X': L(-h, -h, h, h); L(h, -h, -h, h); break;
            case 'Y': L(-h, -h, 0, 0); L(h, -h, 0, 0); L(0, 0, 0, h); break;
            default: L(-h, -h, h, -h); L(h, -h, -h, h); L(-h, h, h, h); break;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _terrain?.Dispose();
            _models?.Dispose();
            _lines?.Dispose();
            _quads?.Dispose();
            _texQuad?.Dispose();
            _minimapTex?.Dispose();
            _skyQuads.Dispose();
            Tiles?.Dispose();
        }
        base.Dispose(disposing);
    }
}
