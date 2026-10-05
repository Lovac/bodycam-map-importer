// Shared helpers, log interface and exit codes.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace BodycamMapInstaller.Core
{
    public interface ILog
    {
        void Info(string line);
        void Good(string line);
        void Warn(string line);
        void Block(string line);
        void Dim(string line);
        void Detail(string line);
        void Progress(int step, int total, string what);
    }

    public sealed class PackageException : Exception
    {
        public PackageException(string friendly) : base(friendly) { }
    }

    public sealed class NullLog : ILog
    {
        public void Info(string line) { }
        public void Good(string line) { }
        public void Warn(string line) { }
        public void Block(string line) { }
        public void Dim(string line) { }
        public void Detail(string line) { }
        public void Progress(int step, int total, string what) { }
    }

    public sealed class ListLog : ILog
    {
        public readonly List<string> Lines = new List<string>();
        readonly ILog inner;

        public ListLog(ILog inner) { this.inner = inner; }

        void Add(string level, string line)
        {
            lock (Lines) Lines.Add(level + " " + line);
        }

        public void Info(string line) { Add("INFO", line); if (inner != null) inner.Info(line); }
        public void Good(string line) { Add("GOOD", line); if (inner != null) inner.Good(line); }
        public void Warn(string line) { Add("WARN", line); if (inner != null) inner.Warn(line); }
        public void Block(string line) { Add("BLOCK", line); if (inner != null) inner.Block(line); }
        public void Dim(string line) { Add("DIM", line); if (inner != null) inner.Dim(line); }
        public void Detail(string line) { Add("DETAIL", line); if (inner != null) inner.Detail(line); }
        public void Progress(int step, int total, string what) { if (inner != null) inner.Progress(step, total, what); }

        public bool Has(string level, string fragment)
        {
            lock (Lines)
                foreach (string l in Lines)
                    if (l.StartsWith(level + " ", StringComparison.Ordinal) && l.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public int Count(string level)
        {
            int n = 0;
            lock (Lines) foreach (string l in Lines) if (l.StartsWith(level + " ", StringComparison.Ordinal)) n++;
            return n;
        }

        public bool Contains(string fragment)
        {
            lock (Lines)
                foreach (string l in Lines)
                    if (l.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        public void Clear() { lock (Lines) Lines.Clear(); }
    }

    public sealed class TextLog : ILog
    {
        readonly TextWriter[] outs;

        public TextLog(params TextWriter[] outs) { this.outs = outs; }

        void W(string prefix, string line)
        {
            foreach (TextWriter w in outs)
            {
                if (w == null) continue;
                try { w.WriteLine(prefix + line); w.Flush(); } catch (Exception) { }
            }
        }

        public void Info(string line) { W("", line); }
        public void Good(string line) { W("", line); }
        public void Warn(string line) { W("note: ", line); }
        public void Block(string line) { W("stopped: ", line); }
        public void Dim(string line) { W("", line); }
        public void Detail(string line) { W("  ", line); }
        public void Progress(int step, int total, string what) { }
    }

    public static class Util
    {
        public const string InstallerVersion = "0.1.0";
        public static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        static readonly Regex GamePak = new Regex(@"^pakchunk\d+(_s\d+)?-Windows\.pak$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static byte[] Sha1(byte[] data)
        {
            using (SHA1 s = SHA1.Create()) return s.ComputeHash(data);
        }

        public static string Sha1Hex(byte[] data) { return Hex(Sha1(data)); }

        public static string Sha1File(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (SHA1 s = SHA1.Create()) return Hex(s.ComputeHash(fs));
        }

        public static string Sha256File(string path)
        {
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (SHA256 s = SHA256.Create()) return Hex(s.ComputeHash(fs));
        }

        public static string Md5Hex(byte[] data)
        {
            using (MD5 m = MD5.Create()) return Hex(m.ComputeHash(data));
        }

        public static string Hex(byte[] b)
        {
            StringBuilder sb = new StringBuilder(b.Length * 2);
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        public static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public static byte[] ReadAll(Stream s)
        {
            using (MemoryStream ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
        }

        public static byte[] ReadExact(Stream s, long offset, long length)
        {
            if (length < 0 || length > int.MaxValue) throw new InvalidDataException("length " + length);
            s.Seek(offset, SeekOrigin.Begin);
            byte[] b = new byte[length];
            int got = 0;
            while (got < b.Length) { int n = s.Read(b, got, b.Length - got); if (n <= 0) throw new EndOfStreamException(); got += n; }
            return b;
        }

        public static void WriteNew(string path, byte[] data)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using (FileStream fs = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(data, 0, data.Length);
                fs.Flush(true);
            }
        }

        public static void MoveFile(string from, string to)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { File.Move(from, to); return; }
                catch (IOException)
                {
                    if (attempt >= 3 || File.Exists(to)) throw;
                    Thread.Sleep(500);
                }
            }
        }

        public static void MoveDirectory(string from, string to)
        {
            for (int attempt = 1; ; attempt++)
            {
                try { Directory.Move(from, to); return; }
                catch (IOException)
                {
                    if (attempt >= 3 || Directory.Exists(to)) throw;
                    Thread.Sleep(500);
                }
            }
        }

        public static bool IsGamePakName(string fileName)
        {
            return GamePak.IsMatch(fileName);
        }

        public static string FullDir(string path)
        {
            return Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
        }

        public static bool IsUnder(string path, string root)
        {
            string p = Path.GetFullPath(path).TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return p.StartsWith(FullDir(root), StringComparison.OrdinalIgnoreCase);
        }

        public static string Relative(string root, string full)
        {
            string r = FullDir(root);
            string f = Path.GetFullPath(full);
            if (!f.StartsWith(r, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException(full + " is not under " + root);
            return f.Substring(r.Length).Replace('\\', '/');
        }

        public static string UtcStamp(DateTime utc)
        {
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture);
        }

        public static DateTime ParseUtcStamp(string s)
        {
            DateTime d;
            if (s != null && DateTime.TryParseExact(s, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out d))
                return d;
            return DateTime.MinValue;
        }

        public static List<string> FilesRecursive(string dir, string suffix)
        {
            List<string> list = new List<string>();
            if (!Directory.Exists(dir)) return list;
            foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                if (suffix == null || f.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) list.Add(f);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public static string Plural(int n, string one, string many)
        {
            return n + " " + (n == 1 ? one : many);
        }

        public static string QuoteArg(string s)
        {
            StringBuilder sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in s)
            {
                if (c == '\\') { backslashes++; continue; }
                if (c == '"') { sb.Append('\\', backslashes * 2 + 1); sb.Append('"'); backslashes = 0; continue; }
                sb.Append('\\', backslashes); backslashes = 0; sb.Append(c);
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static string PlainReason(Exception ex)
        {
            if (ex == null) return null;
            if (ex is PathTooLongException) return "A file path in the Bodycam folder is too long for Windows.";
            if (ex is UnauthorizedAccessException) return "Windows did not let the installer write in the Bodycam folder.";
            if (ex is IOException)
            {
                int hr = ex.HResult;
                if (hr == unchecked((int)0x80070020) || hr == unchecked((int)0x80070021))
                    return "A map file is in use (Steam or antivirus may be checking it). Wait a minute and try again.";
                if (hr == unchecked((int)0x80070070) || hr == unchecked((int)0x80070027))
                    return "The disk with Bodycam is full.";
            }
            return null;
        }

        public static string Uuid5UrlHex(string name)
        {
            byte[] ns = { 0x6b, 0xa7, 0xb8, 0x11, 0x9d, 0xad, 0x11, 0xd1, 0x80, 0xb4, 0x00, 0xc0, 0x4f, 0xd4, 0x30, 0xc8 };
            byte[] nb = Encoding.UTF8.GetBytes(name);
            byte[] all = new byte[ns.Length + nb.Length];
            Buffer.BlockCopy(ns, 0, all, 0, ns.Length);
            Buffer.BlockCopy(nb, 0, all, ns.Length, nb.Length);
            byte[] h = Sha1(all);
            byte[] u = new byte[16];
            Buffer.BlockCopy(h, 0, u, 0, 16);
            u[6] = (byte)((u[6] & 0x0F) | 0x50);
            u[8] = (byte)((u[8] & 0x3F) | 0x80);
            return Hex(u).ToUpperInvariant();
        }

        static uint[] crcTable;

        static uint Crc32(byte[] data, int offset, int count)
        {
            if (crcTable == null)
            {
                uint[] t = new uint[256];
                for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : c >> 1; t[i] = c; }
                crcTable = t;
            }
            uint crc = 0xFFFFFFFFu;
            for (int i = offset; i < offset + count; i++) crc = (crc >> 8) ^ crcTable[(crc ^ data[i]) & 0xFF];
            return ~crc;
        }

        static void BE32(Stream s, uint v)
        {
            s.WriteByte((byte)(v >> 24)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)v);
        }

        static void Chunk(Stream s, string type, byte[] data)
        {
            BE32(s, (uint)data.Length);
            byte[] td = new byte[4 + data.Length];
            Encoding.ASCII.GetBytes(type, 0, 4, td, 0);
            Buffer.BlockCopy(data, 0, td, 4, data.Length);
            s.Write(td, 0, td.Length);
            BE32(s, Crc32(td, 0, td.Length));
        }

        public static byte[] MakePng(int width, int height, byte r, byte g, byte b)
        {
            byte[] raw = new byte[(width * 3 + 1) * height];
            int o = 0;
            for (int y = 0; y < height; y++)
            {
                raw[o++] = 0;
                for (int x = 0; x < width; x++)
                {
                    int shade = (x + y) * 64 / (width + height);
                    raw[o++] = (byte)Math.Min(255, r + shade); raw[o++] = (byte)Math.Min(255, g + shade); raw[o++] = (byte)Math.Min(255, b + shade);
                }
            }
            byte[] deflated;
            using (MemoryStream ms = new MemoryStream())
            {
                using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Compress, true)) ds.Write(raw, 0, raw.Length);
                deflated = ms.ToArray();
            }
            uint a = 1, bb = 0;
            foreach (byte x in raw) { a = (a + x) % 65521; bb = (bb + a) % 65521; }
            MemoryStream z = new MemoryStream();
            z.WriteByte(0x78); z.WriteByte(0x01);
            z.Write(deflated, 0, deflated.Length);
            BE32(z, (bb << 16) | a);
            MemoryStream png = new MemoryStream();
            png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
            MemoryStream ihdr = new MemoryStream();
            BE32(ihdr, (uint)width); BE32(ihdr, (uint)height);
            ihdr.WriteByte(8); ihdr.WriteByte(2); ihdr.WriteByte(0); ihdr.WriteByte(0); ihdr.WriteByte(0);
            Chunk(png, "IHDR", ihdr.ToArray());
            Chunk(png, "IDAT", z.ToArray());
            Chunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }

        public static bool PngSize(byte[] png, out int width, out int height)
        {
            width = height = 0;
            if (png == null || png.Length < 33) return false;
            byte[] sig = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            for (int i = 0; i < 8; i++) if (png[i] != sig[i]) return false;
            if (png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R') return false;
            width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
            return width > 0 && height > 0;
        }
    }

    public sealed class SubStream : Stream
    {
        readonly Stream inner;
        readonly long start, length;
        long pos;
        readonly bool ownsInner;

        public SubStream(Stream inner, long start, long length, bool ownsInner)
        {
            this.inner = inner; this.start = start; this.length = length; this.ownsInner = ownsInner;
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { return length; } }
        public override long Position { get { return pos; } set { pos = value; } }
        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            long left = length - pos;
            if (left <= 0) return 0;
            if (count > left) count = (int)left;
            inner.Seek(start + pos, SeekOrigin.Begin);
            int n = inner.Read(buffer, offset, count);
            pos += n;
            return n;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (origin == SeekOrigin.Begin) pos = offset;
            else if (origin == SeekOrigin.Current) pos += offset;
            else pos = length + offset;
            return pos;
        }

        public override void SetLength(long value) { throw new NotSupportedException(); }
        public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing && ownsInner) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
