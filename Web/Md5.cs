// MD5 (RFC 1321) in plain C#: the browser build of .NET has no MD5, and the core uses it only to fingerprint the game's
// menu tables it carries. Checked against the RFC 1321 test vectors at start (Md5.SelfCheck).
using System;

namespace BodycamMapImporterWeb
{
    public static class Md5
    {
        static readonly int[] S =
        {
            7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22, 7, 12, 17, 22,
            5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20, 5, 9, 14, 20,
            4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23, 4, 11, 16, 23,
            6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21, 6, 10, 15, 21
        };

        static readonly uint[] K = MakeK();

        static uint[] MakeK()
        {
            uint[] k = new uint[64];
            for (int i = 0; i < 64; i++) k[i] = (uint)(long)Math.Floor(Math.Abs(Math.Sin(i + 1)) * 4294967296.0);
            return k;
        }

        public static byte[] Hash(byte[] data)
        {
            long bitLen = (long)data.Length * 8;
            int padLen = ((data.Length + 8) / 64 + 1) * 64;
            byte[] m = new byte[padLen];
            Buffer.BlockCopy(data, 0, m, 0, data.Length);
            m[data.Length] = 0x80;
            for (int i = 0; i < 8; i++) m[padLen - 8 + i] = (byte)(bitLen >> (8 * i));

            uint a0 = 0x67452301, b0 = 0xefcdab89, c0 = 0x98badcfe, d0 = 0x10325476;
            uint[] w = new uint[16];
            for (int off = 0; off < padLen; off += 64)
            {
                for (int i = 0; i < 16; i++) w[i] = BitConverter.ToUInt32(m, off + 4 * i);
                uint a = a0, b = b0, c = c0, d = d0;
                for (int i = 0; i < 64; i++)
                {
                    uint f; int g;
                    if (i < 16) { f = (b & c) | (~b & d); g = i; }
                    else if (i < 32) { f = (d & b) | (~d & c); g = (5 * i + 1) % 16; }
                    else if (i < 48) { f = b ^ c ^ d; g = (3 * i + 5) % 16; }
                    else { f = c ^ (b | ~d); g = (7 * i) % 16; }
                    f = f + a + K[i] + w[g];
                    a = d; d = c; c = b;
                    b = b + ((f << S[i]) | (f >> (32 - S[i])));
                }
                a0 += a; b0 += b; c0 += c; d0 += d;
            }
            byte[] o = new byte[16];
            Buffer.BlockCopy(BitConverter.GetBytes(a0), 0, o, 0, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(b0), 0, o, 4, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(c0), 0, o, 8, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(d0), 0, o, 12, 4);
            return o;
        }

        /// <summary>RFC 1321 test vectors; true when all match.</summary>
        public static bool SelfCheck()
        {
            return Hex(Hash(new byte[0])) == "d41d8cd98f00b204e9800998ecf8427e"
                && Hex(Hash(System.Text.Encoding.ASCII.GetBytes("abc"))) == "900150983cd24fb0d6963f7d28e17f72"
                && Hex(Hash(System.Text.Encoding.ASCII.GetBytes("12345678901234567890123456789012345678901234567890123456789012345678901234567890"))) == "57edf4a22be3c955ac49da2e2107b67a";
        }

        static string Hex(byte[] b)
        {
            return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }
    }
}
