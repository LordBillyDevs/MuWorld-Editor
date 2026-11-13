using System.Runtime.InteropServices;
using System.Text;

namespace MuWorldEditor.Render;

/// <summary>
/// Enlace directo a opengl32.dll mas las funciones de OpenGL 2.1 que se
/// resuelven en tiempo de ejecucion con wglGetProcAddress. Sin NuGet.
/// </summary>
public static unsafe class Gl
{
    private const string Lib = "opengl32.dll";

    // ---------------------------------------------------------- constantes
    public const uint COLOR_BUFFER_BIT = 0x4000, DEPTH_BUFFER_BIT = 0x0100;
    public const uint DEPTH_TEST = 0x0B71, CULL_FACE = 0x0B44, BLEND = 0x0BE2;
    public const uint TEXTURE_2D = 0x0DE1, TEXTURE0 = 0x84C0;
    public const uint TRIANGLES = 0x0004, LINES = 0x0001, LINE_STRIP = 0x0003, TRIANGLE_FAN = 0x0006;
    public const uint UNSIGNED_INT = 0x1405, UNSIGNED_BYTE = 0x1401, FLOAT = 0x1406;
    public const uint RGBA = 0x1908, RGB = 0x1907, BGRA = 0x80E1;
    public const uint TEXTURE_MIN_FILTER = 0x2801, TEXTURE_MAG_FILTER = 0x2800;
    public const uint TEXTURE_WRAP_S = 0x2802, TEXTURE_WRAP_T = 0x2803;
    public const uint LINEAR = 0x2601, NEAREST = 0x2600, REPEAT = 0x2901, CLAMP_TO_EDGE = 0x812F;
    public const uint LINEAR_MIPMAP_LINEAR = 0x2703;
    public const uint GENERATE_MIPMAP = 0x8191;
    public const uint ARRAY_BUFFER = 0x8892, ELEMENT_ARRAY_BUFFER = 0x8893, STATIC_DRAW = 0x88E4, DYNAMIC_DRAW = 0x88E8;
    public const uint VERTEX_SHADER = 0x8B31, FRAGMENT_SHADER = 0x8B30;
    public const uint COMPILE_STATUS = 0x8B81, LINK_STATUS = 0x8B82, INFO_LOG_LENGTH = 0x8B84;
    public const uint SRC_ALPHA = 0x0302, ONE_MINUS_SRC_ALPHA = 0x0303, ONE = 1;
    public const uint LEQUAL = 0x0203, LESS = 0x0201, ALWAYS = 0x0207;
    public const uint BACK = 0x0405, FRONT = 0x0404, CCW = 0x0901, CW = 0x0900;
    public const uint DEPTH_COMPONENT = 0x1902;
    public const uint VENDOR = 0x1F00, RENDERER = 0x1F01, VERSION = 0x1F02;
    public const uint UNPACK_ALIGNMENT = 0x0CF5;
    public const uint POLYGON_OFFSET_FILL = 0x8037;
    public const uint MULTISAMPLE = 0x809D;
    public const uint FRONT_AND_BACK = 0x0408, LINE = 0x1B01, FILL = 0x1B02;
    public const uint TEXTURE_MAX_ANISOTROPY = 0x84FE;
    public const uint STENCIL_TEST = 0x0B90, STENCIL_BUFFER_BIT = 0x0400;
    public const uint KEEP = 0x1E00, REPLACE = 0x1E01, NOTEQUAL = 0x0205, EQUAL = 0x0202;
    public const uint LINE_SMOOTH = 0x0B20, LINE_SMOOTH_HINT = 0x0C52, NICEST = 0x1102;

    // ------------------------------------------------------------ core 1.1
    [DllImport(Lib, EntryPoint = "glClear")] public static extern void Clear(uint mask);
    [DllImport(Lib, EntryPoint = "glClearColor")] public static extern void ClearColor(float r, float g, float b, float a);
    [DllImport(Lib, EntryPoint = "glEnable")] public static extern void Enable(uint cap);
    [DllImport(Lib, EntryPoint = "glDisable")] public static extern void Disable(uint cap);
    [DllImport(Lib, EntryPoint = "glViewport")] public static extern void Viewport(int x, int y, int w, int h);
    [DllImport(Lib, EntryPoint = "glGenTextures")] public static extern void GenTextures(int n, uint* ids);
    [DllImport(Lib, EntryPoint = "glDeleteTextures")] public static extern void DeleteTextures(int n, uint* ids);
    [DllImport(Lib, EntryPoint = "glBindTexture")] public static extern void BindTexture(uint target, uint id);
    [DllImport(Lib, EntryPoint = "glTexImage2D")] public static extern void TexImage2D(uint target, int level, int internalFormat, int w, int h, int border, uint format, uint type, void* pixels);
    [DllImport(Lib, EntryPoint = "glTexParameteri")] public static extern void TexParameteri(uint target, uint pname, int param);
    [DllImport(Lib, EntryPoint = "glTexParameterf")] public static extern void TexParameterf(uint target, uint pname, float param);
    [DllImport(Lib, EntryPoint = "glDrawArrays")] public static extern void DrawArrays(uint mode, int first, int count);
    [DllImport(Lib, EntryPoint = "glDrawElements")] public static extern void DrawElements(uint mode, int count, uint type, void* indices);
    [DllImport(Lib, EntryPoint = "glBlendFunc")] public static extern void BlendFunc(uint src, uint dst);
    [DllImport(Lib, EntryPoint = "glDepthFunc")] public static extern void DepthFunc(uint func);
    [DllImport(Lib, EntryPoint = "glDepthMask")] public static extern void DepthMask(bool flag);
    [DllImport(Lib, EntryPoint = "glCullFace")] public static extern void CullFace(uint mode);
    [DllImport(Lib, EntryPoint = "glFrontFace")] public static extern void FrontFace(uint mode);
    [DllImport(Lib, EntryPoint = "glGetError")] public static extern uint GetError();
    [DllImport(Lib, EntryPoint = "glPixelStorei")] public static extern void PixelStorei(uint pname, int param);
    [DllImport(Lib, EntryPoint = "glGetString")] private static extern IntPtr glGetString(uint name);
    [DllImport(Lib, EntryPoint = "glReadPixels")] public static extern void ReadPixels(int x, int y, int w, int h, uint format, uint type, void* pixels);
    [DllImport(Lib, EntryPoint = "glLineWidth")] public static extern void LineWidth(float w);
    [DllImport(Lib, EntryPoint = "glPolygonOffset")] public static extern void PolygonOffset(float factor, float units);
    [DllImport(Lib, EntryPoint = "glFinish")] public static extern void Finish();
    [DllImport(Lib, EntryPoint = "glFlush")] public static extern void Flush();
    [DllImport(Lib, EntryPoint = "glPolygonMode")] public static extern void PolygonMode(uint face, uint mode);
    [DllImport(Lib, EntryPoint = "glStencilFunc")] public static extern void StencilFunc(uint func, int reference, uint mask);
    [DllImport(Lib, EntryPoint = "glStencilOp")] public static extern void StencilOp(uint sfail, uint dpfail, uint dppass);
    [DllImport(Lib, EntryPoint = "glStencilMask")] public static extern void StencilMask(uint mask);
    [DllImport(Lib, EntryPoint = "glClearStencil")] public static extern void ClearStencil(int s);
    [DllImport(Lib, EntryPoint = "glColorMask")] public static extern void ColorMask(bool r, bool g, bool b, bool a);
    [DllImport(Lib, EntryPoint = "glHint")] public static extern void Hint(uint target, uint mode);

    public static string GetString(uint name)
    {
        var p = glGetString(name);
        return p == IntPtr.Zero ? "" : Marshal.PtrToStringAnsi(p);
    }

    // --------------------------------------------------- funciones dinamicas
    [DllImport(Lib, EntryPoint = "wglGetProcAddress")] private static extern IntPtr wglGetProcAddress(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GetProcAddress(IntPtr module, string name);

    private static IntPtr Resolve(string name)
    {
        var p = wglGetProcAddress(name);
        if (p == IntPtr.Zero || (long)p is >= -1 and <= 3)
            p = GetProcAddress(GetModuleHandle(Lib), name);
        return p;
    }

    private static T Bind<T>(string name) where T : Delegate
    {
        var p = Resolve(name);
        if (p == IntPtr.Zero) throw new NotSupportedException("Esta placa de video no expone " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DGenBuffers(int n, uint* ids);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DBindBuffer(uint target, uint id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DBufferData(uint target, IntPtr size, void* data, uint usage);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DBufferSubData(uint target, IntPtr offset, IntPtr size, void* data);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DDeleteBuffers(int n, uint* ids);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate uint DCreateShader(uint type);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DShaderSource(uint shader, int count, string[] src, int[] len);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DCompileShader(uint shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DGetShaderiv(uint shader, uint pname, int* p);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DGetShaderInfoLog(uint shader, int max, int* len, byte* log);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate uint DCreateProgram();
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DAttachShader(uint prog, uint shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DLinkProgram(uint prog);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DGetProgramiv(uint prog, uint pname, int* p);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DGetProgramInfoLog(uint prog, int max, int* len, byte* log);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DUseProgram(uint prog);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DDeleteShader(uint shader);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int DGetUniformLocation(uint prog, string name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate int DGetAttribLocation(uint prog, string name);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DUniform1i(int loc, int v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DUniform1f(int loc, float v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DUniform2f(int loc, float a, float b);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DUniform3f(int loc, float a, float b, float c);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DUniform4f(int loc, float a, float b, float c, float d);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DUniformMatrix4fv(int loc, int count, bool transpose, float* v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DEnableVertexAttribArray(int index);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DDisableVertexAttribArray(int index);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DVertexAttribPointer(int index, int size, uint type, bool normalized, int stride, IntPtr offset);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DActiveTexture(uint unit);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate void DGenerateMipmap(uint target);

    public static DGenBuffers GenBuffers;
    public static DBindBuffer BindBuffer;
    public static DBufferData BufferData;
    public static DBufferSubData BufferSubData;
    public static DDeleteBuffers DeleteBuffers;
    public static DCreateShader CreateShader;
    public static DShaderSource ShaderSource;
    public static DCompileShader CompileShader;
    public static DGetShaderiv GetShaderiv;
    public static DGetShaderInfoLog GetShaderInfoLog;
    public static DCreateProgram CreateProgram;
    public static DAttachShader AttachShader;
    public static DLinkProgram LinkProgram;
    public static DGetProgramiv GetProgramiv;
    public static DGetProgramInfoLog GetProgramInfoLog;
    public static DUseProgram UseProgram;
    public static DDeleteShader DeleteShader;
    public static DGetUniformLocation GetUniformLocation;
    public static DGetAttribLocation GetAttribLocation;
    public static DUniform1i Uniform1i;
    public static DUniform1f Uniform1f;
    public static DUniform2f Uniform2f;
    public static DUniform3f Uniform3f;
    public static DUniform4f Uniform4f;
    public static DUniformMatrix4fv UniformMatrix4fv;
    public static DEnableVertexAttribArray EnableVertexAttribArray;
    public static DDisableVertexAttribArray DisableVertexAttribArray;
    public static DVertexAttribPointer VertexAttribPointer;
    public static DActiveTexture ActiveTexture;
    public static DGenerateMipmap GenerateMipmap;

    private static bool _loaded;

    public static void LoadExtensions()
    {
        if (_loaded) return;
        GenBuffers = Bind<DGenBuffers>("glGenBuffers");
        BindBuffer = Bind<DBindBuffer>("glBindBuffer");
        BufferData = Bind<DBufferData>("glBufferData");
        BufferSubData = Bind<DBufferSubData>("glBufferSubData");
        DeleteBuffers = Bind<DDeleteBuffers>("glDeleteBuffers");
        CreateShader = Bind<DCreateShader>("glCreateShader");
        ShaderSource = Bind<DShaderSource>("glShaderSource");
        CompileShader = Bind<DCompileShader>("glCompileShader");
        GetShaderiv = Bind<DGetShaderiv>("glGetShaderiv");
        GetShaderInfoLog = Bind<DGetShaderInfoLog>("glGetShaderInfoLog");
        CreateProgram = Bind<DCreateProgram>("glCreateProgram");
        AttachShader = Bind<DAttachShader>("glAttachShader");
        LinkProgram = Bind<DLinkProgram>("glLinkProgram");
        GetProgramiv = Bind<DGetProgramiv>("glGetProgramiv");
        GetProgramInfoLog = Bind<DGetProgramInfoLog>("glGetProgramInfoLog");
        UseProgram = Bind<DUseProgram>("glUseProgram");
        DeleteShader = Bind<DDeleteShader>("glDeleteShader");
        GetUniformLocation = Bind<DGetUniformLocation>("glGetUniformLocation");
        GetAttribLocation = Bind<DGetAttribLocation>("glGetAttribLocation");
        Uniform1i = Bind<DUniform1i>("glUniform1i");
        Uniform1f = Bind<DUniform1f>("glUniform1f");
        Uniform2f = Bind<DUniform2f>("glUniform2f");
        Uniform3f = Bind<DUniform3f>("glUniform3f");
        Uniform4f = Bind<DUniform4f>("glUniform4f");
        UniformMatrix4fv = Bind<DUniformMatrix4fv>("glUniformMatrix4fv");
        EnableVertexAttribArray = Bind<DEnableVertexAttribArray>("glEnableVertexAttribArray");
        DisableVertexAttribArray = Bind<DDisableVertexAttribArray>("glDisableVertexAttribArray");
        VertexAttribPointer = Bind<DVertexAttribPointer>("glVertexAttribPointer");
        ActiveTexture = Bind<DActiveTexture>("glActiveTexture");
        try { GenerateMipmap = Bind<DGenerateMipmap>("glGenerateMipmap"); } catch { GenerateMipmap = null; }
        _loaded = true;
    }

    // ------------------------------------------------------------- utilidad
    public static uint BuildProgram(string vertexSrc, string fragmentSrc)
    {
        uint vs = CompileOne(VERTEX_SHADER, vertexSrc);
        uint fs = CompileOne(FRAGMENT_SHADER, fragmentSrc);
        uint prog = CreateProgram();
        AttachShader(prog, vs);
        AttachShader(prog, fs);
        LinkProgram(prog);

        int ok;
        GetProgramiv(prog, LINK_STATUS, &ok);
        if (ok == 0)
        {
            var log = stackalloc byte[4096];
            int len;
            GetProgramInfoLog(prog, 4096, &len, log);
            throw new Exception("Error al enlazar el shader: " + Encoding.ASCII.GetString(log, len));
        }
        DeleteShader(vs);
        DeleteShader(fs);
        return prog;
    }

    private static uint CompileOne(uint type, string src)
    {
        uint sh = CreateShader(type);
        ShaderSource(sh, 1, new[] { src }, null);
        CompileShader(sh);

        int ok;
        GetShaderiv(sh, COMPILE_STATUS, &ok);
        if (ok == 0)
        {
            var log = stackalloc byte[4096];
            int len;
            GetShaderInfoLog(sh, 4096, &len, log);
            throw new Exception("Error al compilar el shader: " + Encoding.ASCII.GetString(log, len));
        }
        return sh;
    }
}
