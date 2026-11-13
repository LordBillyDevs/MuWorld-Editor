using MuWorldEditor.App;

namespace MuWorldEditor;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // el idioma se elige antes de armar cualquier ventana
        App.Lang.Use(App.Settings.Load().Language);

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        App.DarkTheme.EnableDarkApp();

        // --selftest [dataPath]
        if (args.Length > 0 && args[0].Equals("--selftest", StringComparison.OrdinalIgnoreCase))
        {
            SelfTest.Run(args.Length > 1 ? args[1] : @"D:\MuServer\Client\Data");
            return;
        }

        // --render <dataPath> <mundo> <salida.png> [att] [grid] [dist=3500]
        if (args.Length >= 4 && args[0].Equals("--render", StringComparison.OrdinalIgnoreCase))
        {
            RenderShot.Run(args[1], int.Parse(args[2]), args[3],
                           args.Contains("att"), args.Contains("grid"),
                           args.FirstOrDefault(a => a.StartsWith("dist=")) is string d ? float.Parse(d[5..]) : 3500f);
            return;
        }

        // --edittest <dataPath> <mundo> [salida.txt]
        if (args.Length >= 3 && args[0].Equals("--edittest", StringComparison.OrdinalIgnoreCase))
        {
            App.EditTest.Run(args[1], int.Parse(args[2]), args.Length > 3 ? args[3] : "edittest.txt");
            return;
        }

        // ---- arranque con registro: si algo falla queda escrito en inicio.log
        StartLog.Begin();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => StartLog.Fatal(e.Exception, null);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => StartLog.Fatal(e.ExceptionObject as Exception, null);

        // si el arranque anterior no termino, se arranca en modo seguro:
        // sin antialiasing y con la barra de titulo normal de Windows
        if (StartLog.PreviousStartFailed)
        {
            StartLog.Write("el arranque anterior no termino: modo seguro (sin MSAA, barra de Windows)");
            var cfg = App.Settings.Load();
            cfg.Antialias = false;
            cfg.CustomTitleBar = false;
            App.Settings.Save(cfg);
        }
        StartLog.MarkPending();

        SplashForm splash = null;
        try
        {
            // presentacion mientras se arma la ventana (OpenGL, paneles, fuentes)
            splash = new SplashForm();
            splash.Show();
            Application.DoEvents();
        }
        catch (Exception ex) { StartLog.Write("splash: " + ex.Message); splash = null; }
        var started = DateTime.Now;

        MainForm main;
        try
        {
            StartLog.Write("creando la ventana principal");
            main = new MainForm();
            StartLog.Write("ventana principal creada");
        }
        catch (Exception ex)
        {
            StartLog.Fatal(ex, splash);
            return;
        }
        main.Shown += (_, _) =>
        {
            StartLog.Write("ventana visible");
            StartLog.Done();
            // que se alcance a ver, pero sin hacer esperar de mas
            int wait = Math.Max(0, 700 - (int)(DateTime.Now - started).TotalMilliseconds);
            var t = new System.Windows.Forms.Timer { Interval = Math.Max(1, wait) };
            t.Tick += (_, _) => { t.Stop(); t.Dispose(); splash?.Close(); splash?.Dispose(); main.Activate(); };
            t.Start();
        };
        StartLog.FatalHook = () => { try { splash?.Close(); } catch { } };
        Application.Run(main);
    }
}

/// <summary>
/// Registro del arranque (inicio.log al lado del exe, o en %AppData% si no se
/// puede escribir ahi). Cada paso se escribe y se vacia enseguida, asi que si
/// el programa se cae sin mensaje la ultima linea dice donde fue.
/// </summary>
internal static class StartLog
{
    private static string _path, _marker;
    public static Action FatalHook;
    private static bool _fatalShown;

    public static bool PreviousStartFailed { get; private set; }

    private static string Folder()
    {
        string dir = AppContext.BaseDirectory;
        try
        {
            string probe = Path.Combine(dir, ".probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return dir;
        }
        catch
        {
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MuWorldEditor");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    public static void Begin()
    {
        try
        {
            string dir = Folder();
            _path = Path.Combine(dir, "inicio.log");
            _marker = Path.Combine(dir, "inicio.pendiente");
            PreviousStartFailed = File.Exists(_marker);
            // si el arranque anterior se cayo, se guarda su registro aparte para revisarlo
            if (PreviousStartFailed && File.Exists(_path)) File.Copy(_path, Path.Combine(dir, "inicio.anterior.log"), true);
            File.WriteAllText(_path, $"MU World Editor  {typeof(StartLog).Assembly.GetName().Version}  {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n" +
                                     $"Windows {Environment.OSVersion.Version}  .NET {Environment.Version}  {(Environment.Is64BitProcess ? "x64" : "x86")}\r\n");
        }
        catch { }
    }

    public static void Write(string line)
    {
        try { if (_path != null) File.AppendAllText(_path, DateTime.Now.ToString("HH:mm:ss.fff  ") + line + "\r\n"); } catch { }
    }

    public static void MarkPending() { try { if (_marker != null) File.WriteAllText(_marker, DateTime.Now.ToString("o")); } catch { } }
    public static void Done() { try { if (_marker != null && File.Exists(_marker)) File.Delete(_marker); } catch { } }

    public static void Fatal(Exception ex, Form splash)
    {
        Write("ERROR: " + ex);
        if (_fatalShown) return;
        _fatalShown = true;
        try { splash?.Close(); } catch { }
        try { FatalHook?.Invoke(); } catch { }
        try
        {
            MessageBox.Show("Error inesperado:\r\n\r\n" + ex + "\r\n\r\nQuedo guardado en:\r\n" + _path,
                            "MU World Editor", MessageBoxButtons.OK, MessageBoxIcon.Error,
                            MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
        }
        catch { }
    }
}
