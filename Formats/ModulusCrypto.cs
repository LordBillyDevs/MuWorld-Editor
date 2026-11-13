using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace MuWorldEditor.Formats;

/// <summary>
/// Cifrado "Modulus" de los mapas nuevos (EncTerrainN.map / .att que empiezan
/// con "MAP\x01" o "ATT\x01"). Lo usan los mundos que agrego Webzen en las
/// seasons mas nuevas.
///
/// Estructura del archivo:
///   [4]  firma "MAP\x01" / "ATT\x01" / "OBJ\x01"
///   [1]  algoritmo del cuerpo (se usa &amp; 7)
///   [1]  algoritmo de la cabecera (se usa &amp; 7)
///   [32] clave del cuerpo (cifrada)
///   [n]  cuerpo
///
/// Para descifrar: con el algoritmo de cabecera y la clave fija
/// "webzen#@!01webzen#@!01..." se descifran tres trozos de 1024 bytes
/// (redondeado al bloque) del tramo clave+cuerpo: el del medio, el del final
/// y el del principio (que incluye la clave). Despues, con la clave ya
/// descifrada, se descifra el cuerpo entero con el otro algoritmo.
/// Los 8 algoritmos son los de Crypto++, con su largo de clave por defecto.
/// Verificado byte a byte contra Crypto++ y contra los mapas del cliente S21.
/// </summary>
public static class ModulusCrypto
{
    private static readonly byte[] FixedKey = Encoding.ASCII.GetBytes("webzen#@!01webzen#@!01webzen#@!0");

    /// <summary>Lo que hace falta para volver a cifrar el archivo igual que vino.</summary>
    public sealed class Header
    {
        public byte[] Magic = new byte[4];
        public byte BodyAlgorithm;
        public byte KeyAlgorithm;
        /// <summary>Clave del cuerpo ya descifrada.</summary>
        public byte[] BodyKey = new byte[32];
    }

    public static bool IsModulus(byte[] raw)
    {
        if (raw.Length < 38 || raw[3] != 1) return false;
        string m = Encoding.ASCII.GetString(raw, 0, 3);
        return m is "MAP" or "ATT" or "OBJ";
    }

    public static byte[] Decrypt(byte[] raw, out Header header)
    {
        if (!IsModulus(raw)) throw new InvalidDataException("No es un archivo Modulus");

        header = new Header { BodyAlgorithm = raw[4], KeyAlgorithm = raw[5] };
        Buffer.BlockCopy(raw, 0, header.Magic, 0, 4);

        // tramo clave + cuerpo
        var buf = new byte[raw.Length - 6];
        Buffer.BlockCopy(raw, 6, buf, 0, buf.Length);
        int dataSize = buf.Length - 32;

        var c1 = Create(header.KeyAlgorithm, FixedKey);
        int block = 1024 - 1024 % c1.BlockSize;
        if (dataSize > 4 * block) Process(c1, buf, dataSize >> 1, block, false);
        if (dataSize > block) Process(c1, buf, buf.Length - block, block, false);
        Process(c1, buf, 0, block, false);

        Buffer.BlockCopy(buf, 0, header.BodyKey, 0, 32);
        var c2 = Create(header.BodyAlgorithm, header.BodyKey);
        Process(c2, buf, 32, dataSize, false);

        var body = new byte[dataSize];
        Buffer.BlockCopy(buf, 32, body, 0, dataSize);
        return body;
    }

    public static byte[] Encrypt(byte[] body, Header header)
    {
        var buf = new byte[32 + body.Length];
        Buffer.BlockCopy(header.BodyKey, 0, buf, 0, 32);
        Buffer.BlockCopy(body, 0, buf, 32, body.Length);
        int dataSize = body.Length;

        var c2 = Create(header.BodyAlgorithm, header.BodyKey);
        Process(c2, buf, 32, dataSize, true);

        // los trozos se cifran en el orden inverso al que se descifran
        var c1 = Create(header.KeyAlgorithm, FixedKey);
        int block = 1024 - 1024 % c1.BlockSize;
        Process(c1, buf, 0, block, true);
        if (dataSize > block) Process(c1, buf, buf.Length - block, block, true);
        if (dataSize > 4 * block) Process(c1, buf, dataSize >> 1, block, true);

        var raw = new byte[6 + buf.Length];
        Buffer.BlockCopy(header.Magic, 0, raw, 0, 4);
        raw[4] = header.BodyAlgorithm;
        raw[5] = header.KeyAlgorithm;
        Buffer.BlockCopy(buf, 0, raw, 6, buf.Length);
        return raw;
    }

    /// <summary>Cifra o descifra en modo ECB los bloques enteros del tramo; el resto queda igual.</summary>
    private static void Process(IBlockCipher c, byte[] buf, int offset, int length, bool encrypt)
    {
        int bs = c.BlockSize;
        int end = Math.Min(buf.Length, offset + length);
        for (int p = offset; p + bs <= end; p += bs)
        {
            if (encrypt) c.Encrypt(buf, p);
            else c.Decrypt(buf, p);
        }
    }

    public static IBlockCipher Create(int algorithm, byte[] key) => (algorithm & 7) switch
    {
        0 => new Tea(key),
        1 => new ThreeWay(key),
        2 => new Cast128(key),
        3 => new Rc5(key),
        4 => new Rc6(key),
        5 => new Mars(key),
        6 => new Idea(key),
        _ => new Gost(key),
    };

    // =================================================================== cifrados
    public interface IBlockCipher
    {
        int BlockSize { get; }
        void Encrypt(byte[] b, int o);
        void Decrypt(byte[] b, int o);
    }

    private static uint Rol(uint x, int n) => BitOperations.RotateLeft(x, n & 31);
    private static uint Ror(uint x, int n) => BitOperations.RotateRight(x, n & 31);
    private static uint BE(byte[] b, int o) => BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o));
    private static uint LE(byte[] b, int o) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o));
    private static void PutBE(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o), v);
    private static void PutLE(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(o), v);

    // ------------------------------------------------------------------- TEA
    private sealed class Tea : IBlockCipher
    {
        private const uint Delta = 0x9E3779B9;
        private const int Rounds = 32;
        private readonly uint[] _k = new uint[4];
        public int BlockSize => 8;

        public Tea(byte[] key) { for (int i = 0; i < 4; i++) _k[i] = BE(key, i * 4); }

        public void Encrypt(byte[] b, int o)
        {
            uint y = BE(b, o), z = BE(b, o + 4), sum = 0;
            for (int i = 0; i < Rounds; i++)
            {
                sum += Delta;
                y += ((z << 4) + _k[0]) ^ (z + sum) ^ ((z >> 5) + _k[1]);
                z += ((y << 4) + _k[2]) ^ (y + sum) ^ ((y >> 5) + _k[3]);
            }
            PutBE(b, o, y); PutBE(b, o + 4, z);
        }

        public void Decrypt(byte[] b, int o)
        {
            uint y = BE(b, o), z = BE(b, o + 4), sum = unchecked(Delta * Rounds);
            for (int i = 0; i < Rounds; i++)
            {
                z -= ((y << 4) + _k[2]) ^ (y + sum) ^ ((y >> 5) + _k[3]);
                y -= ((z << 4) + _k[0]) ^ (z + sum) ^ ((z >> 5) + _k[1]);
                sum -= Delta;
            }
            PutBE(b, o, y); PutBE(b, o + 4, z);
        }
    }

    // --------------------------------------------------------------- 3-Way
    private sealed class ThreeWay : IBlockCipher
    {
        private const int Rounds = 11;
        private readonly uint[] _ek = new uint[3], _dk = new uint[3];
        private readonly uint[] _ercon = new uint[Rounds + 1], _drcon = new uint[Rounds + 1];
        public int BlockSize => 12;

        public ThreeWay(byte[] key)
        {
            for (int i = 0; i < 3; i++) _ek[i] = BE(key, i * 4);
            uint a0 = _ek[0], a1 = _ek[1], a2 = _ek[2];
            Theta(ref a0, ref a1, ref a2);
            Mu(ref a0, ref a1, ref a2);
            _dk[0] = a0; _dk[1] = a1; _dk[2] = a2;
            RndCon(0x0b0b, _ercon);
            RndCon(0xb1b1, _drcon);
        }

        private static void RndCon(uint strt, uint[] t)
        {
            for (int i = 0; i <= Rounds; i++)
            {
                t[i] = strt;
                strt <<= 1;
                if ((strt & 0x10000) != 0) strt ^= 0x11011;
            }
        }

        private static uint Rev(uint x)
        {
            uint y = 0;
            for (int i = 0; i < 32; i++) { y = (y << 1) | (x & 1); x >>= 1; }
            return y;
        }

        private static void Mu(ref uint a0, ref uint a1, ref uint a2)
        {
            uint b0 = Rev(a2), b1 = Rev(a1), b2 = Rev(a0);
            a0 = b0; a1 = b1; a2 = b2;
        }

        private static void Gamma(ref uint a0, ref uint a1, ref uint a2)
        {
            uint b0 = a0 ^ (a1 | ~a2), b1 = a1 ^ (a2 | ~a0), b2 = a2 ^ (a0 | ~a1);
            a0 = b0; a1 = b1; a2 = b2;
        }

        private static void Theta(ref uint a0, ref uint a1, ref uint a2)
        {
            uint b0 = a0 ^ (a0 >> 16) ^ (a1 << 16) ^ (a1 >> 16) ^ (a2 << 16) ^ (a1 >> 24) ^ (a2 << 8) ^
                      (a2 >> 8) ^ (a0 << 24) ^ (a2 >> 16) ^ (a0 << 16) ^ (a2 >> 24) ^ (a0 << 8);
            uint b1 = a1 ^ (a1 >> 16) ^ (a2 << 16) ^ (a2 >> 16) ^ (a0 << 16) ^ (a2 >> 24) ^ (a0 << 8) ^
                      (a0 >> 8) ^ (a1 << 24) ^ (a0 >> 16) ^ (a1 << 16) ^ (a0 >> 24) ^ (a1 << 8);
            uint b2 = a2 ^ (a2 >> 16) ^ (a0 << 16) ^ (a0 >> 16) ^ (a1 << 16) ^ (a0 >> 24) ^ (a1 << 8) ^
                      (a1 >> 8) ^ (a2 << 24) ^ (a1 >> 16) ^ (a2 << 16) ^ (a1 >> 24) ^ (a2 << 8);
            a0 = b0; a1 = b1; a2 = b2;
        }

        private static void Rho(ref uint a0, ref uint a1, ref uint a2)
        {
            Theta(ref a0, ref a1, ref a2);
            a0 = (a0 >> 10) ^ (a0 << 22); a2 = (a2 << 1) ^ (a2 >> 31);
            Gamma(ref a0, ref a1, ref a2);
            a0 = (a0 << 1) ^ (a0 >> 31); a2 = (a2 >> 10) ^ (a2 << 22);
        }

        private static void Run(uint[] k, uint[] rcon, ref uint a0, ref uint a1, ref uint a2)
        {
            for (int i = 0; i < Rounds; i++)
            {
                a0 ^= k[0] ^ (rcon[i] << 16); a1 ^= k[1]; a2 ^= k[2] ^ rcon[i];
                Rho(ref a0, ref a1, ref a2);
            }
            a0 ^= k[0] ^ (rcon[Rounds] << 16); a1 ^= k[1]; a2 ^= k[2] ^ rcon[Rounds];
            Theta(ref a0, ref a1, ref a2);
        }

        public void Encrypt(byte[] b, int o)
        {
            uint a0 = BE(b, o), a1 = BE(b, o + 4), a2 = BE(b, o + 8);
            Run(_ek, _ercon, ref a0, ref a1, ref a2);
            PutBE(b, o, a0); PutBE(b, o + 4, a1); PutBE(b, o + 8, a2);
        }

        public void Decrypt(byte[] b, int o)
        {
            uint a0 = BE(b, o), a1 = BE(b, o + 4), a2 = BE(b, o + 8);
            Mu(ref a0, ref a1, ref a2);
            Run(_dk, _drcon, ref a0, ref a1, ref a2);
            Mu(ref a0, ref a1, ref a2);
            PutBE(b, o, a0); PutBE(b, o + 4, a1); PutBE(b, o + 8, a2);
        }
    }

    // ------------------------------------------------------------ CAST-128
    private sealed class Cast128 : IBlockCipher
    {
        private readonly uint[] _km = new uint[16];
        private readonly int[] _kr = new int[16];
        public int BlockSize => 8;

        private static uint S(int box, uint i) => ModulusTables.CastS[box * 256 + (int)(i & 0xFF)];
        private static uint S5(uint i) => S(4, i);
        private static uint S6(uint i) => S(5, i);
        private static uint S7(uint i) => S(6, i);
        private static uint S8(uint i) => S(7, i);

        public Cast128(byte[] key)
        {
            var x = new byte[16];
            Buffer.BlockCopy(key, 0, x, 0, 16);
            var z = new byte[16];
            var k = new uint[32];

            void Split(byte[] dst, int at, uint v)
            {
                dst[at] = (byte)(v >> 24); dst[at + 1] = (byte)(v >> 16);
                dst[at + 2] = (byte)(v >> 8); dst[at + 3] = (byte)v;
            }
            uint W(byte[] src, int at) => BE(src, at);

            for (int half = 0; half < 2; half++)
            {
                int kb = half * 16;
                Split(z, 0, W(x, 0) ^ S5(x[0xD]) ^ S6(x[0xF]) ^ S7(x[0xC]) ^ S8(x[0xE]) ^ S7(x[0x8]));
                Split(z, 4, W(x, 8) ^ S5(z[0x0]) ^ S6(z[0x2]) ^ S7(z[0x1]) ^ S8(z[0x3]) ^ S8(x[0xA]));
                Split(z, 8, W(x, 12) ^ S5(z[0x7]) ^ S6(z[0x6]) ^ S7(z[0x5]) ^ S8(z[0x4]) ^ S5(x[0x9]));
                Split(z, 12, W(x, 4) ^ S5(z[0xA]) ^ S6(z[0x9]) ^ S7(z[0xB]) ^ S8(z[0x8]) ^ S6(x[0xB]));
                k[kb + 0] = S5(z[0x8]) ^ S6(z[0x9]) ^ S7(z[0x7]) ^ S8(z[0x6]) ^ S5(z[0x2]);
                k[kb + 1] = S5(z[0xA]) ^ S6(z[0xB]) ^ S7(z[0x5]) ^ S8(z[0x4]) ^ S6(z[0x6]);
                k[kb + 2] = S5(z[0xC]) ^ S6(z[0xD]) ^ S7(z[0x3]) ^ S8(z[0x2]) ^ S7(z[0x9]);
                k[kb + 3] = S5(z[0xE]) ^ S6(z[0xF]) ^ S7(z[0x1]) ^ S8(z[0x0]) ^ S8(z[0xC]);
                Split(x, 0, W(z, 8) ^ S5(z[0x5]) ^ S6(z[0x7]) ^ S7(z[0x4]) ^ S8(z[0x6]) ^ S7(z[0x0]));
                Split(x, 4, W(z, 0) ^ S5(x[0x0]) ^ S6(x[0x2]) ^ S7(x[0x1]) ^ S8(x[0x3]) ^ S8(z[0x2]));
                Split(x, 8, W(z, 4) ^ S5(x[0x7]) ^ S6(x[0x6]) ^ S7(x[0x5]) ^ S8(x[0x4]) ^ S5(z[0x1]));
                Split(x, 12, W(z, 12) ^ S5(x[0xA]) ^ S6(x[0x9]) ^ S7(x[0xB]) ^ S8(x[0x8]) ^ S6(z[0x3]));
                k[kb + 4] = S5(x[0x3]) ^ S6(x[0x2]) ^ S7(x[0xC]) ^ S8(x[0xD]) ^ S5(x[0x8]);
                k[kb + 5] = S5(x[0x1]) ^ S6(x[0x0]) ^ S7(x[0xE]) ^ S8(x[0xF]) ^ S6(x[0xD]);
                k[kb + 6] = S5(x[0x7]) ^ S6(x[0x6]) ^ S7(x[0x8]) ^ S8(x[0x9]) ^ S7(x[0x3]);
                k[kb + 7] = S5(x[0x5]) ^ S6(x[0x4]) ^ S7(x[0xA]) ^ S8(x[0xB]) ^ S8(x[0x7]);
                Split(z, 0, W(x, 0) ^ S5(x[0xD]) ^ S6(x[0xF]) ^ S7(x[0xC]) ^ S8(x[0xE]) ^ S7(x[0x8]));
                Split(z, 4, W(x, 8) ^ S5(z[0x0]) ^ S6(z[0x2]) ^ S7(z[0x1]) ^ S8(z[0x3]) ^ S8(x[0xA]));
                Split(z, 8, W(x, 12) ^ S5(z[0x7]) ^ S6(z[0x6]) ^ S7(z[0x5]) ^ S8(z[0x4]) ^ S5(x[0x9]));
                Split(z, 12, W(x, 4) ^ S5(z[0xA]) ^ S6(z[0x9]) ^ S7(z[0xB]) ^ S8(z[0x8]) ^ S6(x[0xB]));
                k[kb + 8] = S5(z[0x3]) ^ S6(z[0x2]) ^ S7(z[0xC]) ^ S8(z[0xD]) ^ S5(z[0x9]);
                k[kb + 9] = S5(z[0x1]) ^ S6(z[0x0]) ^ S7(z[0xE]) ^ S8(z[0xF]) ^ S6(z[0xC]);
                k[kb + 10] = S5(z[0x7]) ^ S6(z[0x6]) ^ S7(z[0x8]) ^ S8(z[0x9]) ^ S7(z[0x2]);
                k[kb + 11] = S5(z[0x5]) ^ S6(z[0x4]) ^ S7(z[0xA]) ^ S8(z[0xB]) ^ S8(z[0x6]);
                Split(x, 0, W(z, 8) ^ S5(z[0x5]) ^ S6(z[0x7]) ^ S7(z[0x4]) ^ S8(z[0x6]) ^ S7(z[0x0]));
                Split(x, 4, W(z, 0) ^ S5(x[0x0]) ^ S6(x[0x2]) ^ S7(x[0x1]) ^ S8(x[0x3]) ^ S8(z[0x2]));
                Split(x, 8, W(z, 4) ^ S5(x[0x7]) ^ S6(x[0x6]) ^ S7(x[0x5]) ^ S8(x[0x4]) ^ S5(z[0x1]));
                Split(x, 12, W(z, 12) ^ S5(x[0xA]) ^ S6(x[0x9]) ^ S7(x[0xB]) ^ S8(x[0x8]) ^ S6(z[0x3]));
                k[kb + 12] = S5(x[0x8]) ^ S6(x[0x9]) ^ S7(x[0x7]) ^ S8(x[0x6]) ^ S5(x[0x3]);
                k[kb + 13] = S5(x[0xA]) ^ S6(x[0xB]) ^ S7(x[0x5]) ^ S8(x[0x4]) ^ S6(x[0x7]);
                k[kb + 14] = S5(x[0xC]) ^ S6(x[0xD]) ^ S7(x[0x3]) ^ S8(x[0x2]) ^ S7(x[0x8]);
                k[kb + 15] = S5(x[0xE]) ^ S6(x[0xF]) ^ S7(x[0x1]) ^ S8(x[0x0]) ^ S8(x[0xD]);
            }

            for (int i = 0; i < 16; i++)
            {
                _km[i] = k[i];
                _kr[i] = (int)(k[16 + i] & 31);
            }
        }

        private uint F(int i, uint d)
        {
            uint I;
            switch (i % 3)
            {
                case 0:
                    I = Rol(_km[i] + d, _kr[i]);
                    return ((S(0, I >> 24) ^ S(1, I >> 16)) - S(2, I >> 8)) + S(3, I);
                case 1:
                    I = Rol(_km[i] ^ d, _kr[i]);
                    return ((S(0, I >> 24) - S(1, I >> 16)) + S(2, I >> 8)) ^ S(3, I);
                default:
                    I = Rol(_km[i] - d, _kr[i]);
                    return ((S(0, I >> 24) + S(1, I >> 16)) ^ S(2, I >> 8)) - S(3, I);
            }
        }

        public void Encrypt(byte[] b, int o)
        {
            uint l = BE(b, o), r = BE(b, o + 4);
            for (int i = 0; i < 16; i++) { uint t = r; r = l ^ F(i, r); l = t; }
            PutBE(b, o, r); PutBE(b, o + 4, l);
        }

        public void Decrypt(byte[] b, int o)
        {
            uint l = BE(b, o), r = BE(b, o + 4);
            for (int i = 15; i >= 0; i--) { uint t = r; r = l ^ F(i, r); l = t; }
            PutBE(b, o, r); PutBE(b, o + 4, l);
        }
    }

    // ------------------------------------------------- RC5 / RC6 (clave RC5)
    private static uint[] RcSchedule(byte[] key, int words)
    {
        const uint P = 0xB7E15163, Q = 0x9E3779B9;
        int c = Math.Max(1, (key.Length + 3) / 4);
        var L = new uint[c];
        for (int i = key.Length - 1; i >= 0; i--) L[i / 4] = (L[i / 4] << 8) + key[i];
        var S = new uint[words];
        S[0] = P;
        for (int i = 1; i < words; i++) S[i] = S[i - 1] + Q;
        uint A = 0, B = 0;
        int ii = 0, jj = 0;
        for (int s = 0; s < 3 * Math.Max(words, c); s++)
        {
            A = S[ii] = Rol(S[ii] + A + B, 3);
            B = L[jj] = Rol(L[jj] + A + B, (int)(A + B));
            ii = (ii + 1) % words;
            jj = (jj + 1) % c;
        }
        return S;
    }

    private static byte[] Take(byte[] key, int n)
    {
        var k = new byte[n];
        Buffer.BlockCopy(key, 0, k, 0, Math.Min(n, key.Length));
        return k;
    }

    private sealed class Rc5 : IBlockCipher
    {
        private const int R = 16;
        private readonly uint[] _s;
        public int BlockSize => 8;
        public Rc5(byte[] key) { _s = RcSchedule(Take(key, 16), 2 * R + 2); }

        public void Encrypt(byte[] b, int o)
        {
            uint a = LE(b, o) + _s[0], c = LE(b, o + 4) + _s[1];
            for (int i = 1; i <= R; i++)
            {
                a = Rol(a ^ c, (int)c) + _s[2 * i];
                c = Rol(c ^ a, (int)a) + _s[2 * i + 1];
            }
            PutLE(b, o, a); PutLE(b, o + 4, c);
        }

        public void Decrypt(byte[] b, int o)
        {
            uint a = LE(b, o), c = LE(b, o + 4);
            for (int i = R; i >= 1; i--)
            {
                c = Ror(c - _s[2 * i + 1], (int)a) ^ a;
                a = Ror(a - _s[2 * i], (int)c) ^ c;
            }
            PutLE(b, o, a - _s[0]); PutLE(b, o + 4, c - _s[1]);
        }
    }

    private sealed class Rc6 : IBlockCipher
    {
        private const int R = 20;
        private readonly uint[] _s;
        public int BlockSize => 16;
        public Rc6(byte[] key) { _s = RcSchedule(Take(key, 16), 2 * R + 4); }

        public void Encrypt(byte[] b, int o)
        {
            uint A = LE(b, o), B = LE(b, o + 4), C = LE(b, o + 8), D = LE(b, o + 12);
            B += _s[0]; D += _s[1];
            for (int i = 1; i <= R; i++)
            {
                uint t = Rol(B * (2 * B + 1), 5), u = Rol(D * (2 * D + 1), 5);
                A = Rol(A ^ t, (int)u) + _s[2 * i];
                C = Rol(C ^ u, (int)t) + _s[2 * i + 1];
                uint x = A; A = B; B = C; C = D; D = x;
            }
            A += _s[2 * R + 2]; C += _s[2 * R + 3];
            PutLE(b, o, A); PutLE(b, o + 4, B); PutLE(b, o + 8, C); PutLE(b, o + 12, D);
        }

        public void Decrypt(byte[] b, int o)
        {
            uint A = LE(b, o), B = LE(b, o + 4), C = LE(b, o + 8), D = LE(b, o + 12);
            C -= _s[2 * R + 3]; A -= _s[2 * R + 2];
            for (int i = R; i >= 1; i--)
            {
                uint x = D; D = C; C = B; B = A; A = x;
                uint u = Rol(D * (2 * D + 1), 5), t = Rol(B * (2 * B + 1), 5);
                C = Ror(C - _s[2 * i + 1], (int)t) ^ u;
                A = Ror(A - _s[2 * i], (int)u) ^ t;
            }
            D -= _s[1]; B -= _s[0];
            PutLE(b, o, A); PutLE(b, o + 4, B); PutLE(b, o + 8, C); PutLE(b, o + 12, D);
        }
    }

    // ------------------------------------------------------------------ MARS
    private sealed class Mars : IBlockCipher
    {
        private readonly uint[] _k = new uint[40];
        public int BlockSize => 16;
        private static uint[] Sb => ModulusTables.MarsSbox;
        private static uint S(uint a) => Sb[a & 0x1FF];
        private static uint S0(uint a) => Sb[a & 0xFF];
        private static uint S1(uint a) => Sb[(a & 0xFF) + 256];

        public Mars(byte[] key)
        {
            int n = 4;                                    // clave por defecto: 16 bytes
            var T = new uint[15];
            for (int i = 0; i < n; i++) T[i] = LE(key, i * 4);
            T[n] = (uint)n;

            for (int j = 0; j < 4; j++)
            {
                for (int i = 0; i < 15; i++)
                    T[i] ^= Rol(T[(i + 8) % 15] ^ T[(i + 13) % 15], 3) ^ (uint)(4 * i + j);
                for (int r = 0; r < 4; r++)
                    for (int i = 0; i < 15; i++)
                        T[i] = Rol(T[i] + Sb[T[(i + 14) % 15] & 0x1FF], 9);
                for (int i = 0; i < 10; i++)
                    _k[10 * j + i] = T[(4 * i) % 15];
            }

            for (int i = 5; i <= 35; i += 2)
            {
                uint w = _k[i] | 3;
                uint m = 0;
                for (int l = 2; l <= 30; l++)
                {
                    uint bit = (w >> l) & 1;
                    if (((w >> (l - 1)) & 1) != bit || ((w >> (l + 1)) & 1) != bit) continue;
                    // ¿pertenece a una racha de al menos 10 bits iguales?
                    int lo = l, hi = l;
                    while (lo > 0 && ((w >> (lo - 1)) & 1) == bit) lo--;
                    while (hi < 31 && ((w >> (hi + 1)) & 1) == bit) hi++;
                    if (hi - lo + 1 >= 10) m |= 1u << l;
                }
                uint p = Rol(Sb[265 + (_k[i] & 3)], (int)(_k[i - 1] & 31));
                _k[i] = w ^ (p & m);
            }
        }

        public void Encrypt(byte[] blk, int o)
        {
            uint a = LE(blk, o) + _k[0], b = LE(blk, o + 4) + _k[1];
            uint c = LE(blk, o + 8) + _k[2], d = LE(blk, o + 12) + _k[3], t;

            for (int i = 0; i < 8; i++)
            {
                b = (b ^ S0(a)) + S1(a >> 8);
                c += S0(a >> 16);
                a = Ror(a, 24);
                d ^= S1(a);
                if (i % 4 == 0) a += d;
                if (i % 4 == 1) a += b;
                t = a; a = b; b = c; c = d; d = t;
            }

            for (int i = 0; i < 16; i++)
            {
                t = Rol(a, 13);
                uint r = Rol(t * _k[2 * i + 5], 10);
                uint m = a + _k[2 * i + 4];
                uint l = Rol(S(m) ^ Ror(r, 5) ^ r, (int)r);
                c += Rol(m, (int)Ror(r, 5));
                if (i < 8) { b += l; d ^= r; } else { d += l; b ^= r; }
                a = b; b = c; c = d; d = t;
            }

            for (int i = 0; i < 8; i++)
            {
                if (i % 4 == 2) a -= d;
                if (i % 4 == 3) a -= b;
                b ^= S1(a);
                c -= S0(a >> 24);
                t = Rol(a, 24);
                d = (d - S1(a >> 16)) ^ S0(t);
                a = b; b = c; c = d; d = t;
            }

            PutLE(blk, o, a - _k[36]); PutLE(blk, o + 4, b - _k[37]);
            PutLE(blk, o + 8, c - _k[38]); PutLE(blk, o + 12, d - _k[39]);
        }

        public void Decrypt(byte[] blk, int o)
        {
            uint a = LE(blk, o) + _k[36], b = LE(blk, o + 4) + _k[37];
            uint c = LE(blk, o + 8) + _k[38], d = LE(blk, o + 12) + _k[39];

            for (int i = 7; i >= 0; i--)
            {
                uint t = d;
                uint a0 = Ror(t, 24);
                uint d1 = c, c1 = b, b1 = a;
                uint d0 = (d1 ^ S0(t)) + S1(a0 >> 16);
                uint c0 = c1 + S0(a0 >> 24);
                uint b0 = b1 ^ S1(a0);
                if (i % 4 == 3) a0 += b0;
                if (i % 4 == 2) a0 += d0;
                a = a0; b = b0; c = c0; d = d0;
            }

            for (int i = 15; i >= 0; i--)
            {
                uint t = d;
                uint a0 = Ror(t, 13);
                uint b1 = a, c1 = b, d1 = c;
                uint r = Rol(t * _k[2 * i + 5], 10);
                uint m = a0 + _k[2 * i + 4];
                uint l = Rol(S(m) ^ Ror(r, 5) ^ r, (int)r);
                uint c0 = c1 - Rol(m, (int)Ror(r, 5));
                uint b0, d0;
                if (i < 8) { b0 = b1 - l; d0 = d1 ^ r; } else { d0 = d1 - l; b0 = b1 ^ r; }
                a = a0; b = b0; c = c0; d = d0;
            }

            for (int i = 7; i >= 0; i--)
            {
                uint a1 = d, b1 = a, c1 = b, d1 = c;
                uint ap = a1;
                if (i % 4 == 0) ap -= d1;
                if (i % 4 == 1) ap -= b1;
                uint a0 = Rol(ap, 24);
                uint d0 = d1 ^ S1(ap);
                uint c0 = c1 - S0(a0 >> 16);
                uint b0 = (b1 - S1(a0 >> 8)) ^ S0(a0);
                a = a0; b = b0; c = c0; d = d0;
            }

            PutLE(blk, o, a - _k[0]); PutLE(blk, o + 4, b - _k[1]);
            PutLE(blk, o + 8, c - _k[2]); PutLE(blk, o + 12, d - _k[3]);
        }
    }

    // ------------------------------------------------------------------ IDEA
    private sealed class Idea : IBlockCipher
    {
        private readonly ushort[] _ek = new ushort[52], _dk = new ushort[52];
        public int BlockSize => 8;

        public Idea(byte[] key)
        {
            UInt128 k = 0;
            for (int i = 0; i < 16; i++) k = (k << 8) | key[i];
            for (int g = 0; g < 7; g++)
            {
                UInt128 r = g == 0 ? k : (k << (25 * g)) | (k >> (128 - 25 * g));
                for (int j = 0; j < 8 && g * 8 + j < 52; j++)
                    _ek[g * 8 + j] = (ushort)(r >> (112 - 16 * j));
            }

            int p = 52, q = 0;
            ushort t1 = Inv(_ek[q++]), t2 = Neg(_ek[q++]), t3 = Neg(_ek[q++]);
            _dk[--p] = Inv(_ek[q++]); _dk[--p] = t3; _dk[--p] = t2; _dk[--p] = t1;
            for (int i = 1; i < 8; i++)
            {
                t1 = _ek[q++]; _dk[--p] = _ek[q++]; _dk[--p] = t1;
                t1 = Inv(_ek[q++]); t2 = Neg(_ek[q++]); t3 = Neg(_ek[q++]);
                _dk[--p] = Inv(_ek[q++]); _dk[--p] = t2; _dk[--p] = t3; _dk[--p] = t1;
            }
            t1 = _ek[q++]; _dk[--p] = _ek[q++]; _dk[--p] = t1;
            t1 = Inv(_ek[q++]); t2 = Neg(_ek[q++]); t3 = Neg(_ek[q++]);
            _dk[--p] = Inv(_ek[q++]); _dk[--p] = t3; _dk[--p] = t2; _dk[--p] = t1;
        }

        private static ushort Neg(ushort x) => (ushort)(-x);

        private static ushort Mul(ushort a, ushort b)
        {
            long x = a == 0 ? 65536 : a, y = b == 0 ? 65536 : b;
            long r = x * y % 65537;
            return (ushort)(r == 65536 ? 0 : r);
        }

        private static ushort Inv(ushort x)
        {
            if (x <= 1) return x;                         // 0 y 1 son su propia inversa
            long a = x, m = 65537, x0 = 0, x1 = 1;
            while (a > 1) { long q = a / m, t = m; m = a % m; a = t; t = x0; x0 = x1 - q * x0; x1 = t; }
            if (x1 < 0) x1 += 65537;
            return (ushort)x1;
        }

        private static void Run(ushort[] z, byte[] b, int o)
        {
            ushort x1 = (ushort)(b[o] << 8 | b[o + 1]), x2 = (ushort)(b[o + 2] << 8 | b[o + 3]);
            ushort x3 = (ushort)(b[o + 4] << 8 | b[o + 5]), x4 = (ushort)(b[o + 6] << 8 | b[o + 7]);
            int k = 0;
            for (int r = 0; r < 8; r++)
            {
                x1 = Mul(x1, z[k++]); x2 += z[k++]; x3 += z[k++]; x4 = Mul(x4, z[k++]);
                ushort t2 = Mul((ushort)(x1 ^ x3), z[k++]);
                ushort t1 = Mul((ushort)(t2 + (x2 ^ x4)), z[k++]);
                t2 += t1;
                x1 ^= t1; x4 ^= t2;
                t2 ^= x2; x2 = (ushort)(x3 ^ t1); x3 = t2;
            }
            ushort y1 = Mul(x1, z[k++]), y2 = (ushort)(x3 + z[k++]), y3 = (ushort)(x2 + z[k++]), y4 = Mul(x4, z[k]);
            b[o] = (byte)(y1 >> 8); b[o + 1] = (byte)y1; b[o + 2] = (byte)(y2 >> 8); b[o + 3] = (byte)y2;
            b[o + 4] = (byte)(y3 >> 8); b[o + 5] = (byte)y3; b[o + 6] = (byte)(y4 >> 8); b[o + 7] = (byte)y4;
        }

        public void Encrypt(byte[] b, int o) => Run(_ek, b, o);
        public void Decrypt(byte[] b, int o) => Run(_dk, b, o);
    }

    // ------------------------------------------------------------------ GOST
    private sealed class Gost : IBlockCipher
    {
        private readonly uint[] _k = new uint[8];
        private static readonly uint[,] Table = BuildTable();
        public int BlockSize => 8;

        public Gost(byte[] key) { for (int i = 0; i < 8; i++) _k[i] = LE(key, i * 4); }

        private static uint[,] BuildTable()
        {
            var s = ModulusTables.GostSbox;
            var t = new uint[4, 256];
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 256; j++)
                {
                    uint temp = (uint)(s[(2 * i) * 16 + j % 16] | (s[(2 * i + 1) * 16 + j / 16] << 4));
                    t[i, j] = Rol(temp, 11 + 8 * i);
                }
            return t;
        }

        private static uint F(uint x) =>
            Table[3, x >> 24] ^ Table[2, (x >> 16) & 0xFF] ^ Table[1, (x >> 8) & 0xFF] ^ Table[0, x & 0xFF];

        public void Encrypt(byte[] b, int o)
        {
            uint n1 = LE(b, o), n2 = LE(b, o + 4);
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 8; j += 2) { n2 ^= F(n1 + _k[j]); n1 ^= F(n2 + _k[j + 1]); }
            for (int j = 7; j > 0; j -= 2) { n2 ^= F(n1 + _k[j]); n1 ^= F(n2 + _k[j - 1]); }
            PutLE(b, o, n2); PutLE(b, o + 4, n1);
        }

        public void Decrypt(byte[] b, int o)
        {
            uint n1 = LE(b, o), n2 = LE(b, o + 4);
            for (int j = 0; j < 8; j += 2) { n2 ^= F(n1 + _k[j]); n1 ^= F(n2 + _k[j + 1]); }
            for (int i = 0; i < 3; i++)
                for (int j = 7; j > 0; j -= 2) { n2 ^= F(n1 + _k[j]); n1 ^= F(n2 + _k[j - 1]); }
            PutLE(b, o, n2); PutLE(b, o + 4, n1);
        }
    }
}
