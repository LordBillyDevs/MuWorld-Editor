using MuWorldEditor.Formats;
using MuWorldEditor.World;

namespace MuWorldEditor.Editing;

public enum EditMode { View, Height, Attribute, Tile, Alpha, Objects, Area }
/// <summary>Que hace el boton izquierdo cuando no toca nada: los botones Move y Rotate de la barra.</summary>
public enum NavMode { Move, Rotate }
/// <summary>Cuentagotas: toma del mapa el valor en vez de pintarlo.</summary>
public enum SampleMode { None, Height, Tile, ObjectType }
public enum CameraView { Free, Top, Front, Side }
public enum HeightMode { Raise, Smooth, Flatten, Set }
public enum GizmoMode { Move, Rotate, Scale }
public enum AttributeMode { Add, Remove }

/// <summary>Todo lo que la interfaz configura y la vista 3D obedece.</summary>
public sealed class EditorState
{
    public EditMode Mode = EditMode.View;
    public NavMode Navigation = NavMode.Move;
    /// <summary>Multiplicador de sensibilidad de la camara (1 = normal).</summary>
    public float CameraSpeed = 1f;
    public float ZoomSpeed = 1f;

    // ------------------------------------------------------------- pincel
    public float BrushRadius = 3f;        // en celdas
    public float BrushHardness = 0.5f;    // 0 = borde suave, 1 = borde duro
    public float BrushStrength = 0.5f;
    public HeightMode Height = HeightMode.Raise;
    // limites del pincel de altura, en unidades de mundo
    public float HeightMin = 0f;
    public float HeightMax = 255 * MuWorldEditor.Formats.MuMath.HeightScale;
    public float FlattenTarget = 100f;

    // ---------------------------------------------------------- atributos
    public TerrainFlag AttributeFlags = TerrainFlag.NoMove;
    public AttributeMode AttributeAction = AttributeMode.Add;

    // --------------------------------------------------------------- tiles
    public int TileId = 0;
    public int TileLayer = 1;             // 1 o 2
    public bool AlphaAdd = true;
    /// <summary>Si la celda no tiene textura en la capa 2, ponerla al pintar alfa.</summary>
    public bool AlphaFillsLayer2 = true;

    // ------------------------------------------------------------ objetos
    public GizmoMode Gizmo = GizmoMode.Move;
    public bool SnapToGround = true;
    public bool SnapToGrid;
    public float GridSnapSize = 100f;   // una celda del terreno
    public float RotateSnapDegrees = 15f;
    public float ScaleSnapStep = 0.1f;
    public bool SnapRotation, SnapScale;
    /// <summary>Cuentagotas activo: el proximo clic toma el valor del mapa.</summary>
    public SampleMode Sample = SampleMode.None;

    // ------------------------------------------------------------- area
    /// <summary>Rectangulo de celdas elegido con la herramienta Area.</summary>
    public int AreaX0 = -1, AreaY0 = -1, AreaX1 = -1, AreaY1 = -1;
    public bool HasArea => AreaX0 >= 0 && AreaX1 >= 0;
    public AreaClip Clip;
    public readonly PasteOptions Paste = new();

    public void ClearArea() { AreaX0 = AreaY0 = AreaX1 = AreaY1 = -1; }

    public (int x0, int y0, int x1, int y1) AreaOrdered()
    {
        int x0 = Math.Min(AreaX0, AreaX1), x1 = Math.Max(AreaX0, AreaX1);
        int y0 = Math.Min(AreaY0, AreaY1), y1 = Math.Max(AreaY0, AreaY1);
        return (x0, y0, x1, y1);
    }
    public int NewObjectType = 0;
    public readonly List<MapFiles.MapObject> Selection = new();
    public readonly List<MapFiles.MapObject> Clipboard = new();

    // --------------------------------------------------------- que se ve
    public bool ShowLayer1 = true;
    public bool ShowLayer2 = true;
    public bool ShowGrid;
    public bool ShowAttributes;
    public int AttributeOpacity = 90;
    /// <summary>Radio en celdas del recuadro donde se dibujan la grilla y las marcas.</summary>
    public int OverlayRange = 40;
    /// <summary>El recuadro sigue al mouse en vez de quedarse en el centro de la vista.</summary>
    public bool OverlayFollowsCursor = true;
    public bool ShowObjects = true;
    public bool ShowStaticObjects = true;
    public bool ShowAnimatedObjects = true;
    /// <summary>Reproducir las animaciones de los objetos (pajaros, banderas, molinos...).</summary>
    public bool PlayAnimations = true;
    /// <summary>Velocidad de las animaciones, en porcentaje.</summary>
    public int AnimationSpeed = 100;
    public bool ShowObjectBBox;
    /// <summary>Marcar con lineas la malla del objeto seleccionado.</summary>
    public bool HighlightSelected = true;
    /// <summary>Linea a plomo, cruz y celda del objeto seleccionado.</summary>
    public bool ShowObjectGuides = true;
    /// <summary>Tocar un objeto en la pestana Ver salta a la herramienta Objetos.</summary>
    public bool JumpToObjectOnPick = true;
    public bool ShowLightmap = true;
    public bool AnimateWater = true;
    public bool ShowTerrainBBox;
    public bool ShowFog;
    public bool Wireframe;

    public float FogStart = 5000f, FogEnd = 20000f, FogDensity = 1f;
    public Color Ambient = Color.FromArgb(0, 0, 0);
    public Color Diffuse = Color.FromArgb(255, 255, 255);
    public float CameraAngle = 45f;      // apertura de la camara, en grados
    public float CameraFar = 60000f;
    public Color FogColor = Color.FromArgb(120, 130, 150);
    public Color Background = Color.FromArgb(35, 38, 46);

    // ------------------------------------------------------- presentacion
    /// <summary>Cielo con degradado detras del mapa, en vez de un fondo liso.</summary>
    public bool ShowSky = true;
    public Color SkyTop = Color.FromArgb(17, 21, 31);
    public Color SkyHorizon = Color.FromArgb(70, 78, 96);
    /// <summary>La niebla toma el color del horizonte, asi se funde con el cielo.</summary>
    public bool FogFromSky = true;
    public bool ShowMinimap = true;
    /// <summary>Ejes X/Y/Z chiquitos en la esquina, para orientarse.</summary>
    public bool ShowAxisHud = true;
    /// <summary>Contorno del objeto bajo el mouse.</summary>
    public bool HoverHighlight = true;
    /// <summary>0 = luz real del mapa, 1 = relieve, 2 = combinada, 3 = plana.</summary>
    public int TerrainShading;

    public event Action Changed;
    public void RaiseChanged() => Changed?.Invoke();
}
