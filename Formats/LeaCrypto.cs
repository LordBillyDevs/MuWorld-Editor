using System.Buffers.Binary;
using System.Numerics;

namespace MuWorldEditor.Formats;

/// <summary>
/// LEA-256 en modo ECB, que es como vienen cifrados los .bmd version 15
/// (los modelos de las seasons nuevas: "BMD" + 0x0F + tamaño + datos).
/// Verificado contra el vector oficial de KISA y contra los modelos del
/// cliente S21.
/// </summary>
public static class LeaCrypto
{
    private static readonly byte[] BmdKey =
    {
        0xcc, 0x50, 0x45, 0x13, 0xc2, 0xa6, 0x57, 0x4e, 0xd6, 0x9a, 0x45, 0x89, 0xbf, 0x2f, 0xbc, 0xd9,
        0x39, 0xb3, 0xb3, 0xbd, 0x50, 0xbd, 0xcc, 0xb6, 0x85, 0x46, 0xd1, 0xd6, 0x16, 0x54, 0xe0, 0x87
    };

    private static readonly uint[] Delta =
    {
        0xc3efe9db, 0x44626b02, 0x79e27c8a, 0x78df30ec,
        0x715ea49e, 0xc785da0a, 0xe04ef22a, 0xe5c40957
    };

    private static readonly uint[][] BmdRoundKeys = Schedule(BmdKey);

    private static uint Rol(uint x, int n) => BitOperations.RotateLeft(x, n & 31);
    private static uint Ror(uint x, int n) => BitOperations.RotateRight(x, n & 31);

    private static uint[][] Schedule(byte[] key)
    {
        var t = new uint[8];
        for (int i = 0; i < 8; i++) t[i] = BinaryPrimitives.ReadUInt32LittleEndian(key.AsSpan(i * 4));
        int[] shifts = { 1, 3, 6, 11, 13, 17 };

        var rk = new uint[32][];
        for (int i = 0; i < 32; i++)
        {
            rk[i] = new uint[6];
            for (int j = 0; j < 6; j++)
            {
                int idx = (6 * i + j) % 8;
                t[idx] = Rol(t[idx] + Rol(Delta[i % 8], i + j), shifts[j]);
                rk[i][j] = t[idx];
            }
        }
        return rk;
    }

    /// <summary>Descifra en el lugar los bloques enteros de 16 bytes (lo que sobra queda igual).</summary>
    public static void DecryptEcb(byte[] data, int offset, int count) => Run(data, offset, count, false);

    /// <summary>Cifra en el lugar los bloques enteros de 16 bytes.</summary>
    public static void EncryptEcb(byte[] data, int offset, int count) => Run(data, offset, count, true);

    private static void Run(byte[] data, int offset, int count, bool encrypt)
    {
        var rk = BmdRoundKeys;
        for (int p = offset; p + 16 <= offset + count; p += 16)
        {
            var s = data.AsSpan(p, 16);
            uint x0 = BinaryPrimitives.ReadUInt32LittleEndian(s);
            uint x1 = BinaryPrimitives.ReadUInt32LittleEndian(s[4..]);
            uint x2 = BinaryPrimitives.ReadUInt32LittleEndian(s[8..]);
            uint x3 = BinaryPrimitives.ReadUInt32LittleEndian(s[12..]);

            if (encrypt)
            {
                for (int r = 0; r < 32; r++)
                {
                    var k = rk[r];
                    uint n0 = Rol((x0 ^ k[0]) + (x1 ^ k[1]), 9);
                    uint n1 = Ror((x1 ^ k[2]) + (x2 ^ k[3]), 5);
                    uint n2 = Ror((x2 ^ k[4]) + (x3 ^ k[5]), 3);
                    x3 = x0; x0 = n0; x1 = n1; x2 = n2;
                }
            }
            else
            {
                for (int r = 31; r >= 0; r--)
                {
                    var k = rk[r];
                    uint o0 = x3;
                    uint o1 = (Ror(x0, 9) - (o0 ^ k[0])) ^ k[1];
                    uint o2 = (Rol(x1, 5) - (o1 ^ k[2])) ^ k[3];
                    uint o3 = (Rol(x2, 3) - (o2 ^ k[4])) ^ k[5];
                    x0 = o0; x1 = o1; x2 = o2; x3 = o3;
                }
            }

            BinaryPrimitives.WriteUInt32LittleEndian(s, x0);
            BinaryPrimitives.WriteUInt32LittleEndian(s[4..], x1);
            BinaryPrimitives.WriteUInt32LittleEndian(s[8..], x2);
            BinaryPrimitives.WriteUInt32LittleEndian(s[12..], x3);
        }
    }
}
