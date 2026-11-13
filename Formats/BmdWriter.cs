using System.Numerics;
using System.Text;

namespace MuWorldEditor.Formats;

/// <summary>
/// Arma un .bmd desde cero. Sirve para convertir una imagen en un objeto que el
/// cliente pueda cargar: sale un cartel plano con esa imagen. No inventa volumen
/// (un PNG no tiene), pero para pasto, arbustos, carteles y efectos alcanza,
/// que es justo como estan hechos varios objetos del propio MU.
/// </summary>
public static class BmdWriter
{
    public sealed class Options
    {
        public string Name = "Custom";
        public string TextureFile = "custom.OZT";
        /// <summary>Ancho en unidades de mundo (100 = una celda del terreno).</summary>
        public float Width = 150f;
        public float Height = 150f;
        /// <summary>Dos planos cruzados en X, como los arbustos de MU.</summary>
        public bool Crossed = true;
        /// <summary>Levanta la base del piso, para que no quede hundido.</summary>
        public float GroundOffset = 0f;
    }

    public static void WritePlane(string path, Options opt)
    {
        using var body = new MemoryStream();
        using var w = new BinaryWriter(body, Encoding.ASCII);

        WriteFixed(w, opt.Name, 32);

        int planos = opt.Crossed ? 2 : 1;
        int nv = 4 * planos;
        int ntri = 2 * planos;

        w.Write((short)1);          // una malla
        w.Write((short)1);          // un hueso
        w.Write((short)1);          // una accion

        // ---- malla ----
        w.Write((short)nv);         // vertices
        w.Write((short)nv);         // normales
        w.Write((short)4);          // coordenadas de textura
        w.Write((short)ntri);       // triangulos
        w.Write((short)0);          // indice de textura

        float hw = opt.Width * 0.5f;
        float z0 = opt.GroundOffset;
        float z1 = opt.GroundOffset + opt.Height;

        // plano 1 sobre el eje X, plano 2 girado 90 grados
        var esquinas = new List<Vector3>();
        for (int p = 0; p < planos; p++)
        {
            bool girado = p == 1;
            Vector3 P(float x, float z) => girado ? new Vector3(0, x, z) : new Vector3(x, 0, z);
            esquinas.Add(P(-hw, z0));
            esquinas.Add(P(hw, z0));
            esquinas.Add(P(hw, z1));
            esquinas.Add(P(-hw, z1));
        }

        foreach (var v in esquinas)
        {
            w.Write((short)0); w.Write((short)0);       // hueso 0
            w.Write(v.X); w.Write(v.Y); w.Write(v.Z);
        }

        for (int i = 0; i < nv; i++)
        {
            bool girado = i >= 4;
            var n = girado ? new Vector3(-1, 0, 0) : new Vector3(0, -1, 0);
            w.Write((short)0); w.Write((short)0);
            w.Write(n.X); w.Write(n.Y); w.Write(n.Z);
            w.Write((short)i); w.Write((short)0);        // vertice al que pertenece
        }

        // la imagen entera, de esquina a esquina
        w.Write(0f); w.Write(1f);   // 0: abajo izquierda
        w.Write(1f); w.Write(1f);   // 1: abajo derecha
        w.Write(1f); w.Write(0f);   // 2: arriba derecha
        w.Write(0f); w.Write(0f);   // 3: arriba izquierda

        for (int p = 0; p < planos; p++)
        {
            int b = p * 4;
            WriteTriangle(w, b + 0, b + 1, b + 2, 0, 1, 2);
            WriteTriangle(w, b + 0, b + 2, b + 3, 0, 2, 3);
        }

        WriteFixed(w, opt.TextureFile, 32);

        // ---- accion ----
        w.Write((short)1);          // un solo cuadro
        w.Write((byte)0);           // sin posiciones bloqueadas

        // ---- hueso ----
        w.Write((byte)0);           // no es dummy
        WriteFixed(w, "Root", 32);
        w.Write((short)-1);         // sin padre
        w.Write(0f); w.Write(0f); w.Write(0f);      // posicion del cuadro 1
        w.Write(0f); w.Write(0f); w.Write(0f);      // rotacion del cuadro 1

        w.Flush();

        // ---- archivo: "BMD" + version 10 (sin cifrar) + cuerpo ----
        var bytes = body.ToArray();
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        file.Write(new byte[] { (byte)'B', (byte)'M', (byte)'D', 10 }, 0, 4);
        file.Write(bytes, 0, bytes.Length);
    }

    private static void WriteTriangle(BinaryWriter w, int v0, int v1, int v2, int t0, int t1, int t2)
    {
        long start = w.BaseStream.Position;

        w.Write((byte)3);                 // triangulo
        w.Write((byte)0);                 // relleno
        w.Write((short)v0); w.Write((short)v1); w.Write((short)v2); w.Write((short)0);
        w.Write((short)v0); w.Write((short)v1); w.Write((short)v2); w.Write((short)0);   // normales
        w.Write((short)t0); w.Write((short)t1); w.Write((short)t2); w.Write((short)0);

        // cada triangulo ocupa 64 bytes
        while (w.BaseStream.Position < start + 64) w.Write((byte)0);
    }

    private static void WriteFixed(BinaryWriter w, string text, int size)
    {
        var buf = new byte[size];
        var raw = Encoding.ASCII.GetBytes(text ?? "");
        Buffer.BlockCopy(raw, 0, buf, 0, Math.Min(raw.Length, size - 1));
        w.Write(buf);
    }
}
