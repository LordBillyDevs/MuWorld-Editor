using System.Runtime.InteropServices;

namespace MuWorldEditor.Render;

/// <summary>Control de WinForms con contexto OpenGL propio (WGL, sin dependencias).</summary>
public class GlControl : Control
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize, nVersion;
        public uint dwFlags;
        public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift;
        public byte cBlueBits, cBlueShift, cAlphaBits, cAlphaShift, cAccumBits;
        public byte cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
        public byte cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
        public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    private const uint PFD_DRAW_TO_WINDOW = 4, PFD_SUPPORT_OPENGL = 32, PFD_DOUBLEBUFFER = 1;

    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
    [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR pfd);
    [DllImport("gdi32.dll")] private static extern bool SwapBuffers(IntPtr hdc);
    [DllImport("opengl32.dll")] private static extern IntPtr wglCreateContext(IntPtr hdc);
    [DllImport("opengl32.dll")] private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);
    [DllImport("opengl32.dll")] private static extern bool wglDeleteContext(IntPtr hglrc);
    [DllImport("opengl32.dll")] private static extern IntPtr wglGetProcAddress(string name);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate bool DSwapInterval(int interval);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate bool DChoosePixelFormatARB(IntPtr hdc, int[] iAttribs, float[] fAttribs, uint max, int[] formats, out uint count);

    /// <summary>Muestras de antialiasing pedidas (0 = sin MSAA). Se fija antes de crear el control.</summary>
    public static int RequestedSamples = 4;
    /// <summary>Muestras que realmente dio la placa.</summary>
    public int Samples { get; private set; }

    private IntPtr _hdc, _hrc;
    public bool GlReady { get; private set; }
    public static int ContextCount;
    public string GlInfo { get; private set; } = "";

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x0020 | 0x0001 | 0x0002;   // CS_OWNDC | CS_VREDRAW | CS_HREDRAW
            return cp;
        }
    }

    public GlControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
        SetStyle(ControlStyles.OptimizedDoubleBuffer, false);
        DoubleBuffered = false;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (DesignMode) return;

        StartLog.Write("OpenGL: creando contexto (MSAA pedido: " + RequestedSamples + ")");
        _hdc = GetDC(Handle);

        var pfd = new PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
            iPixelType = 0,
            cColorBits = 32,
            cDepthBits = 24,
            cStencilBits = 8,
            iLayerType = 0
        };

        // primero intentamos un formato con antialiasing (MSAA); si la placa no
        // lo ofrece, se usa el formato comun de siempre
        int format = 0;
        if (RequestedSamples > 0)
        {
            int samples = RequestedSamples;
            format = ChooseMultisampleFormat(_hdc, ref samples);
            if (format != 0 && SetPixelFormat(_hdc, format, ref pfd)) Samples = samples;
            else format = 0;
        }
        if (format == 0)
        {
            format = ChoosePixelFormat(_hdc, ref pfd);
            if (format == 0) throw new Exception("No se encontro un formato de pixel compatible con OpenGL.");
            SetPixelFormat(_hdc, format, ref pfd);
        }

        StartLog.Write("OpenGL: formato " + format + ", muestras " + Samples);
        _hrc = wglCreateContext(_hdc);
        if (_hrc == IntPtr.Zero) throw new Exception("No se pudo crear el contexto de OpenGL.");
        wglMakeCurrent(_hdc, _hrc);

        Gl.LoadExtensions();
        GlInfo = Gl.GetString(Gl.RENDERER) + "  |  OpenGL " + Gl.GetString(Gl.VERSION);
        StartLog.Write("OpenGL: " + GlInfo);

        var sw = wglGetProcAddress("wglSwapIntervalEXT");
        if (sw != IntPtr.Zero)
            Marshal.GetDelegateForFunctionPointer<DSwapInterval>(sw)(1);

        if (Samples > 0) Gl.Enable(Gl.MULTISAMPLE);
        Gl.Hint(Gl.LINE_SMOOTH_HINT, Gl.NICEST);

        GlReady = true;
        ContextCount++;
        OnGlInit();
        StartLog.Write("OpenGL: listo");
    }

    /// <summary>
    /// Pide a la placa un formato con antialiasing. Para eso hace falta un
    /// contexto de OpenGL ya creado, asi que se arma uno temporal en una
    /// ventana invisible y se descarta enseguida.
    /// </summary>
    private static int ChooseMultisampleFormat(IntPtr realDc, ref int samples)
    {
        var dummy = new NativeWindow();
        IntPtr dc = IntPtr.Zero, rc = IntPtr.Zero;
        try
        {
            dummy.CreateHandle(new CreateParams { ClassName = "STATIC", Width = 1, Height = 1 });
            dc = GetDC(dummy.Handle);
            var pfd = new PIXELFORMATDESCRIPTOR
            {
                nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(), nVersion = 1,
                dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
                cColorBits = 32, cDepthBits = 24, cStencilBits = 8
            };
            int f = ChoosePixelFormat(dc, ref pfd);
            if (f == 0 || !SetPixelFormat(dc, f, ref pfd)) return 0;
            rc = wglCreateContext(dc);
            if (rc == IntPtr.Zero || !wglMakeCurrent(dc, rc)) return 0;

            var proc = wglGetProcAddress("wglChoosePixelFormatARB");
            if (proc == IntPtr.Zero) return 0;
            var choose = Marshal.GetDelegateForFunctionPointer<DChoosePixelFormatARB>(proc);

            for (int s = samples; s >= 2; s /= 2)
            {
                int[] attribs =
                {
                    0x2001, 1,        // WGL_DRAW_TO_WINDOW_ARB
                    0x2010, 1,        // WGL_SUPPORT_OPENGL_ARB
                    0x2011, 1,        // WGL_DOUBLE_BUFFER_ARB
                    0x2013, 0x202B,   // WGL_PIXEL_TYPE_ARB = RGBA
                    0x2014, 32,       // WGL_COLOR_BITS_ARB
                    0x2022, 24,       // WGL_DEPTH_BITS_ARB
                    0x2023, 8,        // WGL_STENCIL_BITS_ARB
                    0x2041, 1,        // WGL_SAMPLE_BUFFERS_ARB
                    0x2042, s,        // WGL_SAMPLES_ARB
                    0
                };
                var formats = new int[1];
                if (choose(realDc, attribs, null, 1, formats, out uint n) && n > 0 && formats[0] != 0)
                {
                    samples = s;
                    return formats[0];
                }
            }
            return 0;
        }
        catch { return 0; }
        finally
        {
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            if (rc != IntPtr.Zero) wglDeleteContext(rc);
            if (dc != IntPtr.Zero) ReleaseDC(dummy.Handle, dc);
            try { dummy.DestroyHandle(); } catch { }
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_hrc != IntPtr.Zero)
        {
            wglMakeCurrent(IntPtr.Zero, IntPtr.Zero);
            wglDeleteContext(_hrc);
            _hrc = IntPtr.Zero;
        }
        if (_hdc != IntPtr.Zero) { ReleaseDC(Handle, _hdc); _hdc = IntPtr.Zero; }
        GlReady = false;
        base.OnHandleDestroyed(e);
    }

    public void MakeCurrent()
    {
        if (_hrc != IntPtr.Zero) wglMakeCurrent(_hdc, _hrc);
    }

    public void Swap() => SwapBuffers(_hdc);

    protected virtual void OnGlInit() { }
    protected virtual void OnGlRender() { }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!GlReady) { e.Graphics.Clear(Color.Black); return; }
        MakeCurrent();
        bool first = !_firstFrameLogged;
        if (first) StartLog.Write("OpenGL: primer cuadro");
        OnGlRender();
        Swap();
        if (first) { _firstFrameLogged = true; StartLog.Write("OpenGL: primer cuadro dibujado"); }
    }
    private bool _firstFrameLogged;

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (GlReady)
        {
            MakeCurrent();
            Gl.Viewport(0, 0, Math.Max(1, Width), Math.Max(1, Height));
            Invalidate();
        }
    }
}
