namespace MuWorldEditor.World;

/// <summary>
/// Banderas de atributo de celda. Los valores salen del propio source del
/// cliente (Main/Defines2.h), asi que son exactamente los que lee el juego.
/// </summary>
[Flags]
public enum TerrainFlag : ushort
{
    None = 0,
    SafeZone = 0x0001,
    Character = 0x0002,
    NoMove = 0x0004,
    NoGround = 0x0008,
    Water = 0x0010,
    Action = 0x0020,
    Height = 0x0040,
    CameraUp = 0x0080,
    NoAttack = 0x0100,
    Att1 = 0x0200,
    Att2 = 0x0400,
    Att3 = 0x0800,
    Att4 = 0x1000,
    Att5 = 0x2000,
    Att6 = 0x4000,
    Att7 = 0x8000
}

public sealed class AttributeInfo
{
    public TerrainFlag Flag;
    public string Label;
    public string Hint;
    public Color Color;

    public static readonly AttributeInfo[] All =
    {
        new() { Flag = TerrainFlag.SafeZone,  Label = App.Lang.T("att.zona_segura", "Zona segura"),   Hint = App.Lang.T("hint.no_se_puede_atacar_ni_recibir_dano", "No se puede atacar ni recibir dano"),         Color = Color.FromArgb(60, 200, 60) },
        new() { Flag = TerrainFlag.NoMove,    Label = App.Lang.T("att.no_caminable", "No caminable"),  Hint = App.Lang.T("hint.el_personaje_no_puede_pisar_la_celda", "El personaje no puede pisar la celda"),       Color = Color.FromArgb(230, 60, 60) },
        new() { Flag = TerrainFlag.NoGround,  Label = App.Lang.T("att.sin_piso", "Sin piso"),      Hint = App.Lang.T("hint.hueco_no_hay_terreno_abajo", "Hueco: no hay terreno abajo"),               Color = Color.FromArgb(140, 40, 160) },
        new() { Flag = TerrainFlag.Water,     Label = App.Lang.T("att.agua", "Agua"),          Hint = App.Lang.T("hint.el_personaje_camina_como_en_agua", "El personaje camina como en agua"),           Color = Color.FromArgb(40, 190, 210) },
        new() { Flag = TerrainFlag.Character, Label = App.Lang.T("att.personaje", "Personaje"),     Hint = App.Lang.T("hint.celda_ocupada_por_un_personaje", "Celda ocupada por un personaje"),             Color = Color.FromArgb(240, 200, 60) },
        new() { Flag = TerrainFlag.Action,    Label = App.Lang.T("att.accion", "Accion"),        Hint = App.Lang.T("hint.dispara_un_evento_al_pisarla", "Dispara un evento al pisarla"),               Color = Color.FromArgb(120, 220, 90) },
        new() { Flag = TerrainFlag.Height,    Label = App.Lang.T("att.altura", "Altura"),        Hint = App.Lang.T("hint.marca_de_altura_especial", "Marca de altura especial"),                   Color = Color.FromArgb(200, 130, 60) },
        new() { Flag = TerrainFlag.CameraUp,  Label = App.Lang.T("att.camara_arriba", "Camara arriba"), Hint = App.Lang.T("hint.sube_la_camara_al_pasar", "Sube la camara al pasar"),                    Color = Color.FromArgb(160, 160, 160) },
        new() { Flag = TerrainFlag.NoAttack,  Label = App.Lang.T("att.no_atacar", "No atacar"),     Hint = App.Lang.T("hint.zona_donde_no_se_puede_atacar", "Zona donde no se puede atacar"),              Color = Color.FromArgb(70, 130, 255) },
        new() { Flag = TerrainFlag.Att1,      Label = App.Lang.T("att.extra_1", "Extra 1"),       Hint = App.Lang.T("hint.bandera_libre_0x0200", "Bandera libre 0x0200"),                       Color = Color.FromArgb(120, 100, 200) },
        new() { Flag = TerrainFlag.Att2,      Label = App.Lang.T("att.extra_2", "Extra 2"),       Hint = App.Lang.T("hint.bandera_libre_0x0400", "Bandera libre 0x0400"),                       Color = Color.FromArgb(100, 200, 160) },
        new() { Flag = TerrainFlag.Att3,      Label = App.Lang.T("att.extra_3", "Extra 3"),       Hint = App.Lang.T("hint.bandera_libre_0x0800", "Bandera libre 0x0800"),                       Color = Color.FromArgb(200, 160, 100) },
        new() { Flag = TerrainFlag.Att4,      Label = App.Lang.T("att.extra_4", "Extra 4"),       Hint = App.Lang.T("hint.bandera_libre_0x1000", "Bandera libre 0x1000"),                       Color = Color.FromArgb(150, 150, 60) },
        new() { Flag = TerrainFlag.Att5,      Label = App.Lang.T("att.extra_5", "Extra 5"),       Hint = App.Lang.T("hint.bandera_libre_0x2000", "Bandera libre 0x2000"),                       Color = Color.FromArgb(60, 150, 150) },
        new() { Flag = TerrainFlag.Att6,      Label = App.Lang.T("att.extra_6", "Extra 6"),       Hint = App.Lang.T("hint.bandera_libre_0x4000", "Bandera libre 0x4000"),                       Color = Color.FromArgb(190, 90, 130) },
        new() { Flag = TerrainFlag.Att7,      Label = App.Lang.T("att.extra_7", "Extra 7"),       Hint = App.Lang.T("hint.bandera_libre_0x8000", "Bandera libre 0x8000"),                       Color = Color.FromArgb(90, 190, 90) },
    };

    public static Color ColorOf(ushort value)
    {
        foreach (var a in All)
            if ((value & (ushort)a.Flag) != 0) return a.Color;
        return Color.Transparent;
    }
}
