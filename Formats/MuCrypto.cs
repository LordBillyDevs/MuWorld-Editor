namespace MuWorldEditor.Formats;

/// <summary>
/// Cifrados usados por los archivos de datos de MU Online.
/// Verificado contra Client/Data/World1 (EncTerrain1.map/.obj/.att).
/// </summary>
public static class MuCrypto
{
    // Clave del MapFileEncrypt/Decrypt clasico.
    private static readonly byte[] MapKey =
    {
        0xD1, 0x73, 0x52, 0xF6, 0xD2, 0x9A, 0xCB, 0x27,
        0x3E, 0xAF, 0x59, 0x31, 0x37, 0xB3, 0xE7, 0xA2
    };

    // Clave del BuxConvert (segunda capa, usada por .att y .bmd).
    private static readonly byte[] BuxKey = { 0xFC, 0xCF, 0xAB };

    public static byte[] MapFileDecrypt(byte[] src)
    {
        var dst = new byte[src.Length];
        byte key = 0x5E;
        for (int i = 0; i < src.Length; i++)
        {
            dst[i] = (byte)((src[i] ^ MapKey[i % 16]) - key);
            key = (byte)(src[i] + 0x3D);
        }
        return dst;
    }

    public static byte[] MapFileEncrypt(byte[] src)
    {
        var dst = new byte[src.Length];
        byte key = 0x5E;
        for (int i = 0; i < src.Length; i++)
        {
            dst[i] = (byte)((src[i] + key) ^ MapKey[i % 16]);
            key = (byte)(dst[i] + 0x3D);
        }
        return dst;
    }

    /// <summary>XOR simple de 3 bytes. Es su propia inversa.</summary>
    public static void BuxConvert(byte[] buffer, int offset = 0, int count = -1)
    {
        if (count < 0) count = buffer.Length - offset;
        for (int i = 0; i < count; i++)
            buffer[offset + i] ^= BuxKey[i % 3];
    }

    /// <summary>Checksum de 32 bits que cierra los archivos .bmd y .bmd-like.</summary>
    public static uint CalcCrc(byte[] data, int offset, int count, uint seed)
    {
        uint crc = 0;
        for (int i = 0; i < count; i++)
            crc += (uint)(data[offset + i] ^ (byte)(seed & 0xFF));
        return crc;
    }
}
