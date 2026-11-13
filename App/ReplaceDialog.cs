using MuWorldEditor.Formats;
using MuWorldEditor.World;

namespace MuWorldEditor.App;

/// <summary>
/// Ventana para cambiarle el aspecto a un objeto. Tres caminos:
///  1. reemplazar una de sus imagenes por un PNG (el modelo queda igual pero
///     se ve distinto: es lo que se hace a mano copiando un .OZJ o .OZT);
///  2. armar un objeto nuevo con una imagen, que sale como cartel plano;
///  3. apuntar el objeto a otro archivo .bmd.
/// </summary>
public sealed class ReplaceDialog : Form
{
    private readonly MapView _view;
    private readonly MapFiles.MapObject _obj;
    private readonly ObjectCatalog.Entry _entry;
    private readonly BmdModel _model;

    private ListBox _texList;
    private Label _info;

    /// <summary>Que hay que rehacer despues de cerrar.</summary>
    public bool ModelChanged { get; private set; }

    private static string L(string k, string es) => Lang.T(k, es);

    public ReplaceDialog(MapView view, MapFiles.MapObject obj)
    {
        _view = view;
        _obj = obj;
        _entry = view.Catalog?.Get(obj.Type);

        try { if (_entry?.FullPath != null) _model = BmdModel.Load(_entry.FullPath); }
        catch { _model = null; }

        Text = L("ui.reemplazar_titulo", "Reemplazar el aspecto del objeto");
        Width = 620;
        Height = 520;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        BackColor = DarkTheme.Back;
        ForeColor = DarkTheme.Text;
        Icon = Embedded.AppIcon();

        Build();
        HandleCreated += (_, _) => DarkTheme.UseDarkTitleBar(Handle);
        DarkTheme.Apply(this);
    }

    private void Build()
    {
        string nombre = _view.Catalog?.DisplayName(_obj.Type) ?? ("Tipo " + _obj.Type);
        string archivo = _entry?.FullPath != null ? Path.GetFileName(_entry.FullPath)
                                                  : L("ui.sin_modelo", "(sin modelo)");

        _info = new Label
        {
            Left = 14, Top = 12, Width = 580, Height = 40,
            ForeColor = DarkTheme.Text,
            Text = $"{nombre}   ({L("ui.tipo", "tipo")} {_obj.Type})" + Environment.NewLine +
                   $"{L("ui.modelo", "Modelo")}: {archivo}"
        };
        Controls.Add(_info);

        // ---------- 1) cambiar una imagen del modelo ----------
        Controls.Add(Titulo(L("ui.opcion_textura", "1. Cambiar una imagen de este objeto"), 60));
        Controls.Add(new Label
        {
            Left = 14, Top = 82, Width = 580, Height = 32, ForeColor = DarkTheme.TextDim,
            Text = L("ui.opcion_textura_detalle",
                     "El objeto sigue teniendo la misma forma, pero pintado con tu imagen. " +
                     "Es lo mismo que reemplazar el .OZJ o el .OZT a mano.")
        });

        _texList = new ListBox { Left = 14, Top = 116, Width = 380, Height = 92 };
        if (_model != null)
            foreach (var mesh in _model.Meshes)
            {
                string resuelta = MuTexture.Resolve(_model.Directory, mesh.TexturePath,
                                                    _view.Catalog?.SearchDirs().ToArray() ?? Array.Empty<string>());
                _texList.Items.Add(new TexItem
                {
                    Nombre = mesh.TexturePath,
                    Ruta = resuelta,
                    Text = mesh.TexturePath + (resuelta == null ? "   " + L("ui.no_encontrada", "(no la encuentro)")
                                                               : "   -> " + Path.GetFileName(resuelta))
                });
            }
        if (_texList.Items.Count > 0) _texList.SelectedIndex = 0;
        Controls.Add(_texList);

        var btnTex = new Button
        {
            Left = 402, Top = 116, Width = 192, Height = 34,
            Text = L("ui.elegir_imagen", "Elegir imagen...")
        };
        btnTex.Click += (_, _) => ReplaceTexture();
        Controls.Add(btnTex);

        var btnExport = new Button
        {
            Left = 402, Top = 156, Width = 192, Height = 30,
            Text = L("ui.guardar_imagen_actual", "Guardar la imagen actual")
        };
        btnExport.Click += (_, _) => ExportTexture();
        Controls.Add(btnExport);

        // ---------- 2) armar un objeto con una imagen ----------
        Controls.Add(Titulo(L("ui.opcion_cartel", "2. Armar un objeto nuevo con una imagen"), 224));
        Controls.Add(new Label
        {
            Left = 14, Top = 246, Width = 580, Height = 46, ForeColor = DarkTheme.TextDim,
            Text = L("ui.opcion_cartel_detalle",
                     "Convierte tu imagen en un .bmd que el cliente puede cargar. Como una imagen " +
                     "no tiene volumen, sale plano: dos planos cruzados, igual que el pasto y los " +
                     "arbustos del propio MU. Sirve para carteles, plantas y efectos.")
        });

        var btnCartel = new Button
        {
            Left = 14, Top = 296, Width = 260, Height = 34,
            Text = L("ui.crear_desde_imagen", "Crear objeto desde una imagen...")
        };
        btnCartel.Click += (_, _) => CreateFromImage();
        Controls.Add(btnCartel);

        // ---------- 3) otro modelo ----------
        Controls.Add(Titulo(L("ui.opcion_bmd", "3. Usar otro modelo .bmd"), 348));
        Controls.Add(new Label
        {
            Left = 14, Top = 370, Width = 580, Height = 32, ForeColor = DarkTheme.TextDim,
            Text = L("ui.opcion_bmd_detalle",
                     "Cambia el objeto por otro modelo, por ejemplo uno de otro mapa. " +
                     "El archivo se copia a la carpeta de este mapa.")
        });

        var btnBmd = new Button
        {
            Left = 14, Top = 404, Width = 260, Height = 34,
            Text = L("ui.elegir_modelo", "Elegir un archivo .bmd...")
        };
        btnBmd.Click += (_, _) => ReplaceModel();
        Controls.Add(btnBmd);

        var cerrar = new Button
        {
            Left = 474, Top = 440, Width = 120, Height = 30,
            Text = L("ui.cerrar", "Cerrar"),
            DialogResult = DialogResult.OK
        };
        Controls.Add(cerrar);
        AcceptButton = cerrar;
    }

    private static Label Titulo(string texto, int top) => new()
    {
        Text = texto,
        Left = 14, Top = top, Width = 580, Height = 20,
        ForeColor = DarkTheme.Accent,
        Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold)
    };

    private sealed class TexItem
    {
        public string Nombre, Ruta, Text;
        public override string ToString() => Text;
    }

    // ------------------------------------------------------------ acciones
    private void ReplaceTexture()
    {
        if (_texList.SelectedItem is not TexItem it)
        {
            Aviso(L("msg.elegi_una_imagen_de_la_lista", "Elegí primero una imagen de la lista."));
            return;
        }

        string destino = it.Ruta;
        if (destino == null)
        {
            // no existe todavia: la creamos junto al modelo, con el nombre que pide el .bmd
            string carpeta = _model?.Directory ?? _view.Catalog?.WriteFolder();
            if (carpeta == null) { Aviso(L("msg.no_se_donde_guardar", "No se en que carpeta guardarla.")); return; }
            string baseName = Path.GetFileNameWithoutExtension(it.Nombre);
            string ext = Path.GetExtension(it.Nombre).ToLowerInvariant() == ".tga" ? ".OZT" : ".OZJ";
            destino = Path.Combine(carpeta, baseName + ext);
        }

        using var d = new OpenFileDialog
        {
            Title = L("ui.elegi_la_imagen", "Elegí la imagen"),
            Filter = L("ui.filtro_imagenes", "Imagenes|*.png;*.jpg;*.jpeg;*.bmp;*.tga|Todos|*.*")
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var img = LoadImage(d.FileName);
            TextureWriter.Save(img, destino);

            _view.MakeCurrent();
            if (_entry?.FullPath != null) _view.Models.Forget(_entry.FullPath);
            _view.Invalidate();
            ModelChanged = true;

            Aviso(string.Format(L("msg.textura_cambiada", "Listo. Se reemplazo {0} y el original quedo en _backup."),
                                Path.GetFileName(destino)));
        }
        catch (Exception ex) { Aviso(L("msg.no_pude", "No pude hacerlo: ") + ex.Message); }
    }

    private void ExportTexture()
    {
        if (_texList.SelectedItem is not TexItem it || it.Ruta == null)
        {
            Aviso(L("msg.esa_imagen_no_esta", "Esa imagen no esta en el disco."));
            return;
        }

        using var d = new SaveFileDialog
        {
            Title = L("ui.guardar_imagen_actual", "Guardar la imagen actual"),
            Filter = "PNG|*.png",
            FileName = Path.GetFileNameWithoutExtension(it.Ruta) + ".png"
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            var tex = MuTexture.Load(it.Ruta);
            using var bmp = new Bitmap(tex.Width, tex.Height);
            for (int y = 0; y < tex.Height; y++)
                for (int x = 0; x < tex.Width; x++)
                {
                    int i = (y * tex.Width + x) * 4;
                    bmp.SetPixel(x, y, Color.FromArgb(tex.Pixels[i + 3], tex.Pixels[i], tex.Pixels[i + 1], tex.Pixels[i + 2]));
                }
            bmp.Save(d.FileName, System.Drawing.Imaging.ImageFormat.Png);
            Aviso(L("msg.imagen_guardada", "Imagen guardada. Editala y volve a cargarla con el boton de arriba."));
        }
        catch (Exception ex) { Aviso(L("msg.no_pude", "No pude hacerlo: ") + ex.Message); }
    }

    private void CreateFromImage()
    {
        using var d = new OpenFileDialog
        {
            Title = L("ui.elegi_la_imagen", "Elegí la imagen"),
            Filter = L("ui.filtro_imagenes", "Imagenes|*.png;*.jpg;*.jpeg;*.bmp;*.tga|Todos|*.*")
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            using var img = LoadImage(d.FileName);

            // el alto sale de la proporcion de la imagen
            float ancho = 200f;
            float alto = ancho * img.Height / Math.Max(1, img.Width);
            using (var med = new SizeDialog(ancho, alto))
            {
                if (med.ShowDialog(this) != DialogResult.OK) return;
                ancho = med.Ancho; alto = med.Alto;
            }

            string carpeta = _view.Catalog?.WriteFolder();
            if (carpeta == null) { Aviso(L("msg.no_se_donde_guardar", "No se en que carpeta guardarla.")); return; }

            string baseName = Limpiar(Path.GetFileNameWithoutExtension(d.FileName));
            string texExt = TextureWriter.BestExtension(img);
            string texPath = Path.Combine(carpeta, baseName + texExt);
            string bmdPath = Path.Combine(carpeta, baseName + ".bmd");

            TextureWriter.Save(img, texPath);
            BmdWriter.WritePlane(bmdPath, new BmdWriter.Options
            {
                Name = baseName,
                TextureFile = Path.GetFileName(texPath),
                Width = ancho,
                Height = alto,
                Crossed = true
            });

            int tipo = _view.Catalog.FreeTypeId();
            _view.Catalog.SetEntry(tipo, baseName, bmdPath);
            _view.Catalog.SaveCsv();

            _obj.Type = (short)tipo;
            _view.MakeCurrent();
            _view.Models.Forget(bmdPath);
            _view.Ref.World.Dirty = true;
            _view.Invalidate();
            ModelChanged = true;

            Aviso(string.Format(
                L("msg.objeto_creado",
                  "Listo. Cree {0} y {1} en la carpeta del mapa, le di el tipo {2} y el objeto elegido ahora lo usa."),
                Path.GetFileName(bmdPath), Path.GetFileName(texPath), tipo));
        }
        catch (Exception ex) { Aviso(L("msg.no_pude", "No pude hacerlo: ") + ex.Message); }
    }

    private void ReplaceModel()
    {
        using var d = new OpenFileDialog
        {
            Title = L("ui.elegi_modelo", "Elegí un archivo .bmd"),
            Filter = "MU (*.bmd)|*.bmd"
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        try
        {
            // lo leemos antes de tocar nada, para no meter un archivo roto
            var nuevo = BmdModel.Load(d.FileName);

            string carpeta = _view.Catalog?.WriteFolder();
            if (carpeta == null) { Aviso(L("msg.no_se_donde_guardar", "No se en que carpeta guardarla.")); return; }

            string destino = Path.Combine(carpeta, Path.GetFileName(d.FileName));
            if (!string.Equals(destino, d.FileName, StringComparison.OrdinalIgnoreCase))
            {
                TextureWriter.Backup(destino);
                File.Copy(d.FileName, destino, true);
                CopiarTexturas(nuevo, Path.GetDirectoryName(d.FileName), carpeta);
            }

            int tipo = _view.Catalog.FreeTypeId();
            string nombre = Path.GetFileNameWithoutExtension(destino);
            _view.Catalog.SetEntry(tipo, nombre, destino);
            _view.Catalog.SaveCsv();

            _obj.Type = (short)tipo;
            _view.MakeCurrent();
            _view.Models.Forget(destino);
            _view.Ref.World.Dirty = true;
            _view.Invalidate();
            ModelChanged = true;

            Aviso(string.Format(L("msg.modelo_cambiado", "Listo. El objeto ahora usa {0} (tipo {1})."),
                                Path.GetFileName(destino), tipo));
        }
        catch (Exception ex) { Aviso(L("msg.no_pude", "No pude hacerlo: ") + ex.Message); }
    }

    /// <summary>Se lleva tambien las imagenes que el modelo necesita.</summary>
    private static void CopiarTexturas(BmdModel modelo, string origen, string destino)
    {
        if (origen == null) return;
        foreach (var mesh in modelo.Meshes)
        {
            string ruta = MuTexture.Resolve(origen, mesh.TexturePath);
            if (ruta == null) continue;
            string dst = Path.Combine(destino, Path.GetFileName(ruta));
            if (!File.Exists(dst)) File.Copy(ruta, dst);
        }
    }

    private static Bitmap LoadImage(string path)
    {
        if (Path.GetExtension(path).Equals(".tga", StringComparison.OrdinalIgnoreCase))
        {
            var tex = MuTexture.Load(path);
            var bmp = new Bitmap(tex.Width, tex.Height);
            for (int y = 0; y < tex.Height; y++)
                for (int x = 0; x < tex.Width; x++)
                {
                    int i = (y * tex.Width + x) * 4;
                    bmp.SetPixel(x, y, Color.FromArgb(tex.Pixels[i + 3], tex.Pixels[i], tex.Pixels[i + 1], tex.Pixels[i + 2]));
                }
            return bmp;
        }
        using var tmp = new Bitmap(path);
        return new Bitmap(tmp);      // copia, para soltar el archivo
    }

    private static string Limpiar(string s)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Replace(' ', '_');
    }

    private void Aviso(string texto) =>
        MessageBox.Show(this, texto, "MU World Editor", MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>Pide el tamano del cartel en unidades del mapa.</summary>
    private sealed class SizeDialog : Form
    {
        private readonly NumericUpDown _w, _h;
        public float Ancho => (float)_w.Value;
        public float Alto => (float)_h.Value;

        public SizeDialog(float ancho, float alto)
        {
            Text = Lang.T("ui.tamano_del_objeto", "Tamano del objeto");
            Width = 340; Height = 200;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MinimizeBox = false; MaximizeBox = false;
            BackColor = DarkTheme.Back; ForeColor = DarkTheme.Text;

            Controls.Add(new Label
            {
                Left = 14, Top = 12, Width = 300, Height = 32, ForeColor = DarkTheme.TextDim,
                Text = Lang.T("ui.tamano_detalle", "En unidades del mapa. Una celda del terreno mide 100.")
            });
            Controls.Add(new Label { Left = 14, Top = 56, Width = 60, Text = Lang.T("ui.ancho", "Ancho") });
            Controls.Add(new Label { Left = 14, Top = 88, Width = 60, Text = Lang.T("ui.alto", "Alto") });

            _w = new NumericUpDown { Left = 80, Top = 54, Width = 90, Minimum = 10, Maximum = 5000, Value = (decimal)Math.Clamp(ancho, 10, 5000) };
            _h = new NumericUpDown { Left = 80, Top = 86, Width = 90, Minimum = 10, Maximum = 5000, Value = (decimal)Math.Clamp(alto, 10, 5000) };
            Controls.Add(_w); Controls.Add(_h);

            var ok = new Button { Left = 100, Top = 122, Width = 90, Text = Lang.T("ui.crear", "Crear"), DialogResult = DialogResult.OK };
            var no = new Button { Left = 198, Top = 122, Width = 90, Text = Lang.T("ui.cancelar", "Cancelar"), DialogResult = DialogResult.Cancel };
            Controls.Add(ok); Controls.Add(no);
            AcceptButton = ok; CancelButton = no;

            HandleCreated += (_, _) => DarkTheme.UseDarkTitleBar(Handle);
            DarkTheme.Apply(this);
        }
    }
}
