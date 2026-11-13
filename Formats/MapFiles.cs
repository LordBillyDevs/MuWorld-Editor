using System.Text;

namespace MuWorldEditor.Formats;

/// <summary>Lectura y escritura de los archivos de un mundo de MU. Todos son 256x256.</summary>
public static class MapFiles
{
    public const int Size = 256;
    public const int Cells = Size * Size;

    // ---------------------------------------------------------------- .att
    public sealed class AttributeFile
    {
        public byte Version;
        public byte MapNumber;
        public byte Width = 255, Height = 255;
        /// <summary>True si el archivo guarda 2 bytes por celda (att extendido).</summary>
        public bool Extended;
        public ushort[] Data = new ushort[Cells];
        /// <summary>Si vino con el cifrado nuevo ("ATT\x01"), con que claves volver a guardarlo.</summary>
        public ModulusCrypto.Header Modulus;
    }

    public static AttributeFile ReadAtt(string path)
    {
        var raw = File.ReadAllBytes(path);
        ModulusCrypto.Header modulus = null;
        var buf = ModulusCrypto.IsModulus(raw)
            ? ModulusCrypto.Decrypt(raw, out modulus)
            : MuCrypto.MapFileDecrypt(raw);
        MuCrypto.BuxConvert(buf);

        var att = new AttributeFile
        {
            Version = buf[0],
            MapNumber = buf[1],
            Width = buf[2],
            Height = buf[3],
            Extended = buf.Length >= 4 + Cells * 2,
            Modulus = modulus
        };

        if (att.Extended)
            for (int i = 0; i < Cells; i++)
                att.Data[i] = (ushort)(buf[4 + i * 2] | (buf[5 + i * 2] << 8));
        else
            for (int i = 0; i < Cells; i++)
                att.Data[i] = buf[4 + i];

        return att;
    }

    public static void WriteAtt(string path, AttributeFile att)
    {
        int len = 4 + Cells * (att.Extended ? 2 : 1);
        var buf = new byte[len];
        buf[0] = att.Version;
        buf[1] = att.MapNumber;
        buf[2] = att.Width;
        buf[3] = att.Height;

        if (att.Extended)
            for (int i = 0; i < Cells; i++)
            {
                buf[4 + i * 2] = (byte)(att.Data[i] & 0xFF);
                buf[5 + i * 2] = (byte)(att.Data[i] >> 8);
            }
        else
            for (int i = 0; i < Cells; i++)
                buf[4 + i] = (byte)att.Data[i];

        MuCrypto.BuxConvert(buf);
        File.WriteAllBytes(path, att.Modulus != null
            ? ModulusCrypto.Encrypt(buf, att.Modulus)
            : MuCrypto.MapFileEncrypt(buf));
    }

    // ---------------------------------------------------------------- .map
    public sealed class MappingFile
    {
        public byte Version;
        public byte MapNumber;
        public byte[] Layer1 = new byte[Cells];
        public byte[] Layer2 = new byte[Cells];
        public byte[] Alpha = new byte[Cells];
        public ModulusCrypto.Header Modulus;
    }

    public static MappingFile ReadMap(string path)
    {
        var raw = File.ReadAllBytes(path);
        ModulusCrypto.Header modulus = null;
        var buf = ModulusCrypto.IsModulus(raw)
            ? ModulusCrypto.Decrypt(raw, out modulus)
            : MuCrypto.MapFileDecrypt(raw);
        if (buf.Length < 2 + Cells * 3) throw new InvalidDataException("Archivo .map incompleto: " + path);
        var m = new MappingFile { Version = buf[0], MapNumber = buf[1], Modulus = modulus };
        Buffer.BlockCopy(buf, 2, m.Layer1, 0, Cells);
        Buffer.BlockCopy(buf, 2 + Cells, m.Layer2, 0, Cells);
        Buffer.BlockCopy(buf, 2 + Cells * 2, m.Alpha, 0, Cells);
        return m;
    }

    public static void WriteMap(string path, MappingFile m)
    {
        var buf = new byte[2 + Cells * 3];
        buf[0] = m.Version;
        buf[1] = m.MapNumber;
        Buffer.BlockCopy(m.Layer1, 0, buf, 2, Cells);
        Buffer.BlockCopy(m.Layer2, 0, buf, 2 + Cells, Cells);
        Buffer.BlockCopy(m.Alpha, 0, buf, 2 + Cells * 2, Cells);
        File.WriteAllBytes(path, m.Modulus != null
            ? ModulusCrypto.Encrypt(buf, m.Modulus)
            : MuCrypto.MapFileEncrypt(buf));
    }

    // ---------------------------------------------------------------- .obj
    // Version 0: 30 bytes por objeto (tipo, posicion, giro, escala).
    // Las seasons nuevas agregan bytes al final de cada objeto: v1 = 32,
    // v3 = 45, v4 = 46, v5 = 54. El editor no los interpreta: los conserva
    // tal cual para que el cliente los siga leyendo igual.
    public const int ObjBaseSize = 30;

    private static readonly Dictionary<int, int> ObjRecordSizes = new()
    {
        [0] = 30, [1] = 32, [3] = 45, [4] = 46, [5] = 54
    };

    public sealed class MapObject
    {
        public short Type;
        public float X, Y, Z;
        public float AngleX, AngleY, AngleZ;
        public float Scale = 1f;
        /// <summary>Bytes extra de las versiones nuevas del .obj (null = usar los de por defecto).</summary>
        public byte[] Extra;

        public MapObject Clone()
        {
            var c = (MapObject)MemberwiseClone();
            c.Extra = Extra == null ? null : (byte[])Extra.Clone();
            return c;
        }
    }

    public sealed class ObjectFile
    {
        public byte Version;
        public byte MapNumber;
        public List<MapObject> Objects = new();
        /// <summary>El archivo declara mas objetos de los que tiene: no se debe reescribir.</summary>
        public bool Truncated;
        /// <summary>Bytes por objeto en el archivo (30 en los mapas clasicos).</summary>
        public int RecordSize = ObjBaseSize;
        /// <summary>Extra que se usa para los objetos nuevos (el mas comun del archivo).</summary>
        public byte[] DefaultExtra = Array.Empty<byte>();
        public ModulusCrypto.Header Modulus;
        /// <summary>El cuerpo Modulus traia ademas el cifrado clasico adentro.</summary>
        public bool ModulusInnerClassic;
    }

    private static int GuessRecordSize(int version, int count, int bodyLength)
    {
        int payload = bodyLength - 4;
        if (count > 0 && payload % count == 0 && payload / count >= ObjBaseSize && payload / count <= 128)
            return payload / count;
        return ObjRecordSizes.TryGetValue(version, out int known) ? known : ObjBaseSize;
    }

    private static bool LooksLikeObj(byte[] buf)
    {
        if (buf.Length < 4) return false;
        int count = buf[2] | (buf[3] << 8);
        int payload = buf.Length - 4;
        if (count == 0) return payload == 0;
        return payload % count == 0 && payload / count >= ObjBaseSize && payload / count <= 128;
    }

    public static ObjectFile ReadObj(string path)
    {
        var raw = File.ReadAllBytes(path);
        ModulusCrypto.Header modulus = null;
        bool innerClassic = false;
        byte[] buf;
        if (ModulusCrypto.IsModulus(raw))
        {
            buf = ModulusCrypto.Decrypt(raw, out modulus);
            if (!LooksLikeObj(buf))
            {
                var alt = MuCrypto.MapFileDecrypt(buf);
                if (LooksLikeObj(alt)) { buf = alt; innerClassic = true; }
            }
        }
        else buf = MuCrypto.MapFileDecrypt(raw);

        using var ms = new MemoryStream(buf);
        using var r = new BinaryReader(ms);

        var o = new ObjectFile
        {
            Version = r.ReadByte(), MapNumber = r.ReadByte(),
            Modulus = modulus, ModulusInnerClassic = innerClassic
        };
        int count = r.ReadUInt16();
        o.RecordSize = GuessRecordSize(o.Version, count, buf.Length);
        int extraSize = o.RecordSize - ObjBaseSize;

        if (4 + (long)count * o.RecordSize > buf.Length)
        {
            o.Truncated = true;
            count = Math.Max(0, (buf.Length - 4) / o.RecordSize);
        }

        var freq = new Dictionary<string, (int n, byte[] bytes)>();
        for (int i = 0; i < count; i++)
        {
            var ob = new MapObject
            {
                Type = r.ReadInt16(),
                X = r.ReadSingle(), Y = r.ReadSingle(), Z = r.ReadSingle(),
                AngleX = r.ReadSingle(), AngleY = r.ReadSingle(), AngleZ = r.ReadSingle(),
                Scale = r.ReadSingle()
            };
            if (extraSize > 0)
            {
                ob.Extra = r.ReadBytes(extraSize);
                string key = Convert.ToHexString(ob.Extra);
                freq[key] = freq.TryGetValue(key, out var f) ? (f.n + 1, f.bytes) : (1, ob.Extra);
            }
            o.Objects.Add(ob);
        }

        o.DefaultExtra = freq.Count > 0
            ? (byte[])freq.Values.OrderByDescending(v => v.n).First().bytes.Clone()
            : new byte[Math.Max(0, extraSize)];
        return o;
    }

    public static void WriteObj(string path, ObjectFile o)
    {
        int extraSize = Math.Max(0, o.RecordSize - ObjBaseSize);
        using var ms = new MemoryStream(4 + o.Objects.Count * o.RecordSize);
        using (var w = new BinaryWriter(ms, Encoding.ASCII, true))
        {
            w.Write(o.Version);
            w.Write(o.MapNumber);
            w.Write((ushort)o.Objects.Count);
            foreach (var ob in o.Objects)
            {
                w.Write(ob.Type);
                w.Write(ob.X); w.Write(ob.Y); w.Write(ob.Z);
                w.Write(ob.AngleX); w.Write(ob.AngleY); w.Write(ob.AngleZ);
                w.Write(ob.Scale);
                if (extraSize > 0)
                {
                    var extra = ob.Extra ?? o.DefaultExtra ?? Array.Empty<byte>();
                    var fixedExtra = new byte[extraSize];
                    Buffer.BlockCopy(extra, 0, fixedExtra, 0, Math.Min(extra.Length, extraSize));
                    w.Write(fixedExtra);
                }
            }
        }

        var body = ms.ToArray();
        byte[] outBytes;
        if (o.Modulus != null)
            outBytes = ModulusCrypto.Encrypt(o.ModulusInnerClassic ? MuCrypto.MapFileEncrypt(body) : body, o.Modulus);
        else
            outBytes = MuCrypto.MapFileEncrypt(body);
        File.WriteAllBytes(path, outBytes);
    }

    // ------------------------------------------------- TerrainHeight.OZB
    // OZB = 4 bytes de relleno + un BMP completo, sin invertir filas (igual que
    // el cliente).
    //  * Clasico: BMP de 8 bits; la altura es el indice de paleta x 1.5.
    //  * Extendido (mapas nuevos): BMP de 24 bits; cada pixel guarda un numero
    //    de 24 bits (R + G*256 + B*65536) y la altura es ese numero - 500, sin
    //    escala. Es el "OpenTerrainHeightNew" del cliente.
    public const float ExtendedHeightOffset = -500f;
    public const int ExtendedHeightMax = 0xFFFFFF;

    public sealed class HeightFile
    {
        public byte[] Header;          // se conserva tal cual para no romper el archivo
        /// <summary>Altura de cada celda en unidades de mundo.</summary>
        public float[] Height = new float[Cells];
        /// <summary>True si es el formato de 24 bits de los mapas nuevos.</summary>
        public bool Extended;
    }

    private static bool IsExtendedOzb(byte[] raw)
    {
        // 4 de relleno + "BM" + ... bits por pixel en el offset 28 del BMP
        if (raw.Length < 4 + 54 + Cells * 3) return false;
        if (raw[4] != (byte)'B' || raw[5] != (byte)'M') return false;
        return BitConverter.ToUInt16(raw, 4 + 28) == 24;
    }

    public static HeightFile ReadOzb(string path)
    {
        var raw = File.ReadAllBytes(path);
        var h = new HeightFile { Extended = IsExtendedOzb(raw) };

        if (h.Extended)
        {
            int dataOffset = raw.Length - Cells * 3;
            h.Header = raw[..dataOffset];
            for (int i = 0; i < Cells; i++)
            {
                int p = dataOffset + i * 3;
                int v = raw[p + 2] | (raw[p + 1] << 8) | (raw[p] << 16);
                h.Height[i] = v + ExtendedHeightOffset;
            }
        }
        else
        {
            int dataOffset = raw.Length - Cells;
            if (dataOffset < 0) throw new InvalidDataException("OZB demasiado chico: " + path);
            h.Header = raw[..dataOffset];
            for (int i = 0; i < Cells; i++) h.Height[i] = raw[dataOffset + i] * MuMath.HeightScale;
        }
        return h;
    }

    /// <summary>Lleva una altura de mundo al valor que se puede guardar en el formato.</summary>
    public static float QuantizeHeight(float height, bool extended)
    {
        if (extended)
            return Math.Clamp(MathF.Round(height - ExtendedHeightOffset), 0, ExtendedHeightMax) + ExtendedHeightOffset;
        return Math.Clamp(MathF.Round(height / MuMath.HeightScale), 0, 255) * MuMath.HeightScale;
    }

    public static void WriteOzb(string path, HeightFile h)
    {
        int bpp = h.Extended ? 3 : 1;
        var raw = new byte[h.Header.Length + Cells * bpp];
        Buffer.BlockCopy(h.Header, 0, raw, 0, h.Header.Length);
        int p = h.Header.Length;

        for (int i = 0; i < Cells; i++)
        {
            if (h.Extended)
            {
                int v = (int)Math.Clamp(MathF.Round(h.Height[i] - ExtendedHeightOffset), 0, ExtendedHeightMax);
                raw[p++] = (byte)(v >> 16);
                raw[p++] = (byte)(v >> 8);
                raw[p++] = (byte)v;
            }
            else
                raw[p++] = (byte)Math.Clamp(MathF.Round(h.Height[i] / MuMath.HeightScale), 0, 255);
        }
        File.WriteAllBytes(path, raw);
    }
}
