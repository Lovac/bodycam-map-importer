// Zip reading, including reading a stored pak in place without extracting it.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BodycamMapInstaller.Core
{
    public sealed class ZipSource : IDisposable
    {
        public readonly string Path;
        readonly Func<Stream> openRaw;
        readonly Stream archiveStream;
        public readonly ZipArchive Archive;
        readonly Dictionary<string, ZipArchiveEntry> byName = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        readonly Dictionary<string, RawItem> raw = new Dictionary<string, RawItem>(StringComparer.Ordinal);
        public readonly List<string> Names = new List<string>();
        public long MemoryLimit = 512L * 1024 * 1024;

        sealed class RawItem { public int Method; public long CompressedSize, UncompressedSize, LocalHeaderOffset; }

        public ZipSource(string path)
        {
            Path = path;
            string p = path;
            openRaw = delegate { return new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16); };
            archiveStream = openRaw();
            Archive = Open(archiveStream);
            Index();
        }

        public ZipSource(byte[] bytes, string name)
        {
            Path = name;
            byte[] b = bytes;
            openRaw = delegate { return new MemoryStream(b, false); };
            archiveStream = openRaw();
            Archive = Open(archiveStream);
            Index();
        }

        static ZipArchive Open(Stream s)
        {
            try { return new ZipArchive(s, ZipArchiveMode.Read, true); }
            catch (InvalidDataException) { s.Dispose(); throw; }
        }

        void Index()
        {
            foreach (ZipArchiveEntry e in Archive.Entries)
            {
                if (e.FullName.EndsWith("/") && e.Length == 0) continue;
                if (byName.ContainsKey(e.FullName)) throw new InvalidDataException("duplicate zip entry " + e.FullName);
                byName[e.FullName] = e;
                Names.Add(e.FullName);
            }
            try { ReadCentralDirectory(); }
            catch (Exception) { raw.Clear(); }
        }

        public bool Has(string name) { return byName.ContainsKey(name); }

        public long Length(string name) { return byName[name].Length; }

        public byte[] Read(string name)
        {
            using (Stream s = byName[name].Open()) return Util.ReadAll(s);
        }

        public void CopyTo(string name, Stream dest)
        {
            using (Stream s = byName[name].Open()) s.CopyTo(dest, 1 << 20);
        }

        public Func<Stream> SeekableOpener(string name)
        {
            RawItem it;
            ZipArchiveEntry e = byName[name];
            if (raw.TryGetValue(name, out it) && it.Method == 0 && it.UncompressedSize == e.Length)
            {
                long dataStart;
                using (Stream s = openRaw()) dataStart = DataStart(s, it.LocalHeaderOffset);
                long len = it.UncompressedSize;
                return delegate { return new SubStream(openRaw(), dataStart, len, true); };
            }
            if (e.Length > MemoryLimit) return null;
            byte[] data = Read(name);
            return delegate { return new MemoryStream(data, false); };
        }

        static long DataStart(Stream s, long localHeader)
        {
            byte[] h = Util.ReadExact(s, localHeader, 30);
            if (BitConverter.ToUInt32(h, 0) != 0x04034b50) throw new InvalidDataException("zip local header");
            int nameLen = BitConverter.ToUInt16(h, 26), extraLen = BitConverter.ToUInt16(h, 28);
            return localHeader + 30 + nameLen + extraLen;
        }

        void ReadCentralDirectory()
        {
            using (Stream s = openRaw())
            {
                long len = s.Length;
                int tail = (int)Math.Min(len, 22 + 65535 + 20);
                byte[] t = Util.ReadExact(s, len - tail, tail);
                int eocd = -1;
                for (int i = t.Length - 22; i >= 0; i--)
                    if (t[i] == 0x50 && t[i + 1] == 0x4b && t[i + 2] == 0x05 && t[i + 3] == 0x06) { eocd = i; break; }
                if (eocd < 0) throw new InvalidDataException("zip end record");
                long entries = BitConverter.ToUInt16(t, eocd + 10);
                long cdSize = BitConverter.ToUInt32(t, eocd + 12);
                long cdOffset = BitConverter.ToUInt32(t, eocd + 16);
                if (entries == 0xFFFF || cdSize == 0xFFFFFFFF || cdOffset == 0xFFFFFFFF)
                {
                    int loc = eocd - 20;
                    if (loc < 0 || BitConverter.ToUInt32(t, loc) != 0x07064b50) throw new InvalidDataException("zip64 locator");
                    long z64 = BitConverter.ToInt64(t, loc + 8);
                    byte[] z = Util.ReadExact(s, z64, 56);
                    if (BitConverter.ToUInt32(z, 0) != 0x06064b50) throw new InvalidDataException("zip64 end record");
                    entries = BitConverter.ToInt64(z, 32);
                    cdSize = BitConverter.ToInt64(z, 40);
                    cdOffset = BitConverter.ToInt64(z, 48);
                }
                byte[] cd = Util.ReadExact(s, cdOffset, cdSize);
                int o = 0;
                for (long k = 0; k < entries; k++)
                {
                    if (BitConverter.ToUInt32(cd, o) != 0x02014b50) throw new InvalidDataException("zip central entry");
                    int flags = BitConverter.ToUInt16(cd, o + 8);
                    RawItem it = new RawItem();
                    it.Method = BitConverter.ToUInt16(cd, o + 10);
                    it.CompressedSize = BitConverter.ToUInt32(cd, o + 20);
                    it.UncompressedSize = BitConverter.ToUInt32(cd, o + 24);
                    int nameLen = BitConverter.ToUInt16(cd, o + 28), extraLen = BitConverter.ToUInt16(cd, o + 30), commentLen = BitConverter.ToUInt16(cd, o + 32);
                    it.LocalHeaderOffset = BitConverter.ToUInt32(cd, o + 42);
                    string name = ((flags & 0x800) != 0 ? Encoding.UTF8 : Encoding.GetEncoding(437)).GetString(cd, o + 46, nameLen);
                    int x = o + 46 + nameLen, xe = x + extraLen;
                    while (x + 4 <= xe)
                    {
                        int id = BitConverter.ToUInt16(cd, x), sz = BitConverter.ToUInt16(cd, x + 2);
                        if (id == 1)
                        {
                            int f = x + 4;
                            if (it.UncompressedSize == 0xFFFFFFFF) { it.UncompressedSize = BitConverter.ToInt64(cd, f); f += 8; }
                            if (it.CompressedSize == 0xFFFFFFFF) { it.CompressedSize = BitConverter.ToInt64(cd, f); f += 8; }
                            if (it.LocalHeaderOffset == 0xFFFFFFFF) { it.LocalHeaderOffset = BitConverter.ToInt64(cd, f); f += 8; }
                        }
                        x += 4 + sz;
                    }
                    raw[name] = it;
                    o += 46 + nameLen + extraLen + commentLen;
                }
            }
        }

        public void Dispose()
        {
            Archive.Dispose();
            archiveStream.Dispose();
        }

        public static bool LooksLikeZip(string path)
        {
            try
            {
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 22) return false;
                    byte[] b = new byte[4];
                    if (fs.Read(b, 0, 4) != 4) return false;
                    return b[0] == 0x50 && b[1] == 0x4b && (b[2] == 3 || b[2] == 5) && (b[3] == 4 || b[3] == 6);
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
