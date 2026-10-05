// Unreal pak files (version 11): hashes, writer and reader.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace BodycamMapInstaller.Core
{
    public static class PakHash
    {
        static readonly uint[] Refl = MakeRefl();
        static readonly uint[] NonRefl = MakeNonRefl();

        static uint[] MakeRefl()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; i++) { uint c = i; for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? (c >> 1) ^ 0xEDB88320u : c >> 1; t[i] = c; }
            return t;
        }

        static uint[] MakeNonRefl()
        {
            uint[] t = new uint[256];
            for (uint i = 0; i < 256; i++) { uint c = i << 24; for (int k = 0; k < 8; k++) c = (c & 0x80000000u) != 0 ? (c << 1) ^ 0x04C11DB7u : c << 1; t[i] = c; }
            return t;
        }

        public static uint StrCrc32(string s)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (char ch in s)
            {
                uint c = ch;
                for (int k = 0; k < 4; k++) { crc = (crc >> 8) ^ Refl[(crc ^ c) & 0xFF]; c >>= 8; }
            }
            return ~crc;
        }

        public static uint StrihashDeprecated(string s)
        {
            uint h = 0;
            foreach (char ch in s)
            {
                uint c = char.ToUpperInvariant(ch);
                h = ((h >> 8) & 0x00FFFFFFu) ^ NonRefl[(h ^ c) & 0xFF];
            }
            return h;
        }

        public static ulong PathHash(string relPath, ulong seed)
        {
            ulong h = unchecked(0xCBF29CE484222325UL + seed);
            byte[] b = Encoding.Unicode.GetBytes(relPath.ToLowerInvariant());
            foreach (byte x in b) { h ^= x; h = unchecked(h * 0x00000100000001B3UL); }
            return h;
        }
    }

    public sealed class PakEntryInfo
    {
        public string Path;
        public long Offset, Size, UncompressedSize, BlockSize;
        public int Method;
        public bool Encrypted;
        public byte[] Sha1;
        public ulong PathHash;
        public List<long[]> Blocks;
    }

    public static class PakWriter
    {
        public const uint Magic = 0x5A6F12E1;
        public const int Version = 11;
        public const string ContentMount = "../../../Bodycam/Content/";

        public static void Write(string outPath, string mountPoint, IList<KeyValuePair<string, byte[]>> files)
        {
            string name = System.IO.Path.GetFileName(outPath);
            if (name.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 5);
            using (FileStream fs = new FileStream(outPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                WriteTo(fs, name, mountPoint, files);
                fs.Flush(true);
            }
        }

        public static byte[] Build(string pakFileName, string mountPoint, IList<KeyValuePair<string, byte[]>> files)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                WriteTo(ms, pakFileName, mountPoint, files);
                return ms.ToArray();
            }
        }

        public static void WriteTo(Stream fs, string pakFileName, string mount, IList<KeyValuePair<string, byte[]>> files)
        {
            ulong seed = PakHash.StrCrc32(pakFileName.ToLowerInvariant());
            List<long> offsets = new List<long>();
            long start = fs.Position;
            BinaryWriter w = new BinaryWriter(fs, Encoding.ASCII, true);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, byte[]> kv in files)
            {
                if (!PackagePaths.IsSafeEntryName(kv.Key) || kv.Key.EndsWith("/")) throw new ArgumentException("bad pak path " + kv.Key);
                if (!seen.Add(kv.Key)) throw new ArgumentException("duplicate pak path " + kv.Key);
                offsets.Add(fs.Position - start);
                w.Write(0L);
                w.Write((long)kv.Value.Length);
                w.Write((long)kv.Value.Length);
                w.Write(0u);
                w.Write(Util.Sha1(kv.Value));
                w.Write((byte)0);
                w.Write(0u);
                w.Write(kv.Value);
            }
            long indexOffset = fs.Position - start;

            MemoryStream enc = new MemoryStream();
            BinaryWriter ew = new BinaryWriter(enc);
            List<int> encOff = new List<int>();
            for (int i = 0; i < files.Count; i++)
            {
                long off = offsets[i], size = files[i].Value.Length;
                bool off32 = off <= uint.MaxValue, size32 = size <= uint.MaxValue;
                encOff.Add((int)enc.Position);
                uint flags = (off32 ? 1u << 31 : 0u) | (size32 ? 1u << 30 : 0u) | (size32 ? 1u << 29 : 0u);
                ew.Write(flags);
                if (off32) ew.Write((uint)off); else ew.Write(off);
                if (size32) ew.Write((uint)size); else ew.Write(size);
            }
            byte[] encoded = enc.ToArray();

            MemoryStream ph = new MemoryStream();
            BinaryWriter pw = new BinaryWriter(ph);
            pw.Write(files.Count);
            for (int i = 0; i < files.Count; i++) { pw.Write(PakHash.PathHash(files[i].Key, seed)); pw.Write(encOff[i]); }
            pw.Write(0);
            byte[] phBytes = ph.ToArray();

            SortedDictionary<string, List<KeyValuePair<string, int>>> dirs = new SortedDictionary<string, List<KeyValuePair<string, int>>>(StringComparer.Ordinal);
            dirs["/"] = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < files.Count; i++)
            {
                string p = files[i].Key;
                int cut = p.LastIndexOf('/');
                string dir = cut < 0 ? "/" : p.Substring(0, cut + 1);
                string name = p.Substring(cut + 1);
                for (int k = 0; k < dir.Length; k++)
                    if (dir[k] == '/' && !dirs.ContainsKey(dir.Substring(0, k + 1)) && k > 0) dirs[dir.Substring(0, k + 1)] = new List<KeyValuePair<string, int>>();
                if (!dirs.ContainsKey(dir)) dirs[dir] = new List<KeyValuePair<string, int>>();
                dirs[dir].Add(new KeyValuePair<string, int>(name, encOff[i]));
            }
            MemoryStream fd = new MemoryStream();
            BinaryWriter fw = new BinaryWriter(fd);
            fw.Write(dirs.Count);
            foreach (KeyValuePair<string, List<KeyValuePair<string, int>>> d in dirs)
            {
                FString(fw, d.Key);
                fw.Write(d.Value.Count);
                foreach (KeyValuePair<string, int> f in d.Value) { FString(fw, f.Key); fw.Write(f.Value); }
            }
            byte[] fdBytes = fd.ToArray();

            byte[] primary = Primary(mount, files.Count, seed, 0, phBytes, 0, fdBytes, encoded);
            long phOffset = indexOffset + primary.Length;
            long fdOffset = phOffset + phBytes.Length;
            primary = Primary(mount, files.Count, seed, phOffset, phBytes, fdOffset, fdBytes, encoded);

            w.Write(primary);
            w.Write(phBytes);
            w.Write(fdBytes);

            w.Write(new byte[16]);
            w.Write((byte)0);
            w.Write(Magic);
            w.Write(Version);
            w.Write(indexOffset);
            w.Write((long)primary.Length);
            w.Write(Util.Sha1(primary));
            w.Write(new byte[5 * 32]);
            w.Flush();
        }

        static byte[] Primary(string mount, int count, ulong seed, long phOff, byte[] ph, long fdOff, byte[] fd, byte[] encoded)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            FString(w, mount);
            w.Write(count);
            w.Write(seed);
            w.Write(1); w.Write(phOff); w.Write((long)ph.Length); w.Write(Util.Sha1(ph));
            w.Write(1); w.Write(fdOff); w.Write((long)fd.Length); w.Write(Util.Sha1(fd));
            w.Write(encoded.Length); w.Write(encoded);
            w.Write(0);
            return ms.ToArray();
        }

        public static void FString(BinaryWriter w, string s)
        {
            foreach (char c in s) if (c < 0x20 || c > 0x7E) throw new ArgumentException("non-ASCII pak string " + s);
            w.Write(s.Length + 1);
            w.Write(Encoding.ASCII.GetBytes(s));
            w.Write((byte)0);
        }
    }

    public sealed class PakReader
    {
        public string MountPoint;
        public int Version;
        public ulong PathHashSeed;
        public string[] Methods = new string[0];
        public Dictionary<string, PakEntryInfo> Entries = new Dictionary<string, PakEntryInfo>(StringComparer.Ordinal);
        Func<Stream> open;

        public static PakReader Open(string path)
        {
            return Open(path, true);
        }

        public static PakReader Open(string path, bool readEntryHeaders)
        {
            string p = path;
            return Open(delegate { return new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16); }, readEntryHeaders);
        }

        public static PakReader Open(Func<Stream> open, bool readEntryHeaders)
        {
            PakReader r = new PakReader();
            r.open = open;
            using (Stream fs = open())
            {
                if (fs.Length < 221) throw new InvalidDataException("too small for a pak");
                BinaryReader br = new BinaryReader(fs);
                fs.Seek(-221, SeekOrigin.End);
                br.ReadBytes(16);
                byte encIndex = br.ReadByte();
                if (br.ReadUInt32() != PakWriter.Magic) throw new InvalidDataException("no pak footer (magic)");
                r.Version = br.ReadInt32();
                if (r.Version != PakWriter.Version) throw new NotSupportedException("pak version " + r.Version);
                if (encIndex != 0) throw new NotSupportedException("encrypted pak index");
                long indexOff = br.ReadInt64(), indexSize = br.ReadInt64();
                byte[] indexSha = br.ReadBytes(20);
                r.Methods = new string[5];
                for (int m = 0; m < 5; m++) r.Methods[m] = Encoding.ASCII.GetString(br.ReadBytes(32)).TrimEnd('\0');
                if (indexOff < 0 || indexSize <= 0 || indexOff + indexSize > fs.Length) throw new InvalidDataException("index outside the file");
                byte[] primary = Util.ReadExact(fs, indexOff, indexSize);
                if (Util.Hex(Util.Sha1(primary)) != Util.Hex(indexSha)) throw new InvalidDataException("primary index sha1");
                BinaryReader p = new BinaryReader(new MemoryStream(primary));
                r.MountPoint = ReadFString(p);
                int count = p.ReadInt32();
                r.PathHashSeed = p.ReadUInt64();
                bool hasPh = p.ReadInt32() != 0;
                long phOff = 0, phSize = 0; byte[] phSha = null;
                if (hasPh) { phOff = p.ReadInt64(); phSize = p.ReadInt64(); phSha = p.ReadBytes(20); }
                if (p.ReadInt32() == 0) throw new NotSupportedException("pak without a full directory index");
                long fdOff = p.ReadInt64(), fdSize = p.ReadInt64(); byte[] fdSha = p.ReadBytes(20);
                byte[] encoded = p.ReadBytes(p.ReadInt32());
                int nonEncodedCount = p.ReadInt32();
                List<PakEntryInfo> nonEncoded = new List<PakEntryInfo>();
                for (int i = 0; i < nonEncodedCount; i++) nonEncoded.Add(ReadLegacyEntry(p));

                Dictionary<int, ulong> hashByLocation = new Dictionary<int, ulong>();
                int nh = -1;
                if (hasPh)
                {
                    byte[] ph = Util.ReadExact(fs, phOff, phSize);
                    if (Util.Hex(Util.Sha1(ph)) != Util.Hex(phSha)) throw new InvalidDataException("path hash index sha1");
                    BinaryReader h = new BinaryReader(new MemoryStream(ph));
                    nh = h.ReadInt32();
                    for (int i = 0; i < nh; i++) { ulong hv = h.ReadUInt64(); hashByLocation[h.ReadInt32()] = hv; }
                }
                byte[] fd = Util.ReadExact(fs, fdOff, fdSize);
                if (Util.Hex(Util.Sha1(fd)) != Util.Hex(fdSha)) throw new InvalidDataException("full directory index sha1");

                BinaryReader d = new BinaryReader(new MemoryStream(fd));
                int ndirs = d.ReadInt32();
                for (int i = 0; i < ndirs; i++)
                {
                    string dir = ReadFString(d);
                    int nf = d.ReadInt32();
                    for (int k = 0; k < nf; k++)
                    {
                        string name = ReadFString(d);
                        int location = d.ReadInt32();
                        PakEntryInfo e;
                        if (location >= 0) e = Decode(encoded, location);
                        else
                        {
                            int listIndex = -location - 1;
                            if (listIndex < 0 || listIndex >= nonEncoded.Count) throw new InvalidDataException("entry location " + location);
                            PakEntryInfo src = nonEncoded[listIndex];
                            e = new PakEntryInfo();
                            e.Offset = src.Offset; e.Size = src.Size; e.UncompressedSize = src.UncompressedSize; e.Method = src.Method;
                            e.Encrypted = src.Encrypted; e.BlockSize = src.BlockSize; e.Blocks = src.Blocks; e.Sha1 = src.Sha1;
                        }
                        e.Path = (dir == "/" ? "" : dir) + name;
                        ulong hv;
                        e.PathHash = hashByLocation.TryGetValue(location, out hv) ? hv : 0;
                        if (readEntryHeaders)
                        {
                            fs.Seek(e.Offset, SeekOrigin.Begin);
                            br.ReadInt64(); long size = br.ReadInt64(); long usize = br.ReadInt64(); int method = br.ReadInt32();
                            e.Sha1 = br.ReadBytes(20);
                            if (size != e.Size || usize != e.UncompressedSize || method != e.Method) throw new InvalidDataException("in-place header differs from index for " + e.Path);
                        }
                        if (r.Entries.ContainsKey(e.Path)) throw new InvalidDataException("duplicate entry " + e.Path);
                        r.Entries.Add(e.Path, e);
                    }
                }
                if (r.Entries.Count != count || (hasPh && nh != count)) throw new InvalidDataException("entry count " + r.Entries.Count + "/" + nh + "/" + count);
            }
            return r;
        }

        static PakEntryInfo ReadLegacyEntry(BinaryReader p)
        {
            PakEntryInfo e = new PakEntryInfo();
            e.Offset = p.ReadInt64(); e.Size = p.ReadInt64(); e.UncompressedSize = p.ReadInt64(); e.Method = (int)p.ReadUInt32();
            e.Sha1 = p.ReadBytes(20);
            e.Blocks = new List<long[]>();
            if (e.Method != 0)
            {
                int n = p.ReadInt32();
                for (int i = 0; i < n; i++) { long a = p.ReadInt64(), b = p.ReadInt64(); e.Blocks.Add(new long[] { a, b }); }
            }
            byte flags = p.ReadByte();
            e.Encrypted = (flags & 1) != 0;
            e.BlockSize = p.ReadUInt32();
            if (e.Method == 0) e.Blocks.Add(new long[] { 53, 53 + e.Size });
            return e;
        }

        static PakEntryInfo Decode(byte[] enc, int o)
        {
            uint flags = BitConverter.ToUInt32(enc, o); o += 4;
            PakEntryInfo e = new PakEntryInfo();
            long blockSize;
            if ((flags & 0x3F) == 0x3F) { blockSize = BitConverter.ToUInt32(enc, o); o += 4; } else blockSize = (flags & 0x3F) << 11;
            e.Method = (int)((flags >> 23) & 0x3F);
            if ((flags & (1u << 31)) != 0) { e.Offset = BitConverter.ToUInt32(enc, o); o += 4; } else { e.Offset = BitConverter.ToInt64(enc, o); o += 8; }
            if ((flags & (1u << 30)) != 0) { e.UncompressedSize = BitConverter.ToUInt32(enc, o); o += 4; } else { e.UncompressedSize = BitConverter.ToInt64(enc, o); o += 8; }
            e.Size = e.UncompressedSize;
            if (e.Method != 0)
            {
                if ((flags & (1u << 29)) != 0) { e.Size = BitConverter.ToUInt32(enc, o); o += 4; } else { e.Size = BitConverter.ToInt64(enc, o); o += 8; }
            }
            e.Encrypted = ((flags >> 22) & 1) != 0;
            int blocks = (int)((flags >> 6) & 0xFFFF);
            e.BlockSize = blocks == 1 ? e.UncompressedSize : blockSize;
            long start = 53 + (e.Method != 0 ? 4 + 16 * blocks : 0);
            e.Blocks = new List<long[]>();

            if (blocks == 1 && !e.Encrypted) e.Blocks.Add(new long[] { start, start + e.Size });
            else
                for (int i = 0; i < blocks; i++) { long len = BitConverter.ToUInt32(enc, o); o += 4; e.Blocks.Add(new long[] { start, start + len }); start += len; }
            return e;
        }

        public void ReadSha1(PakEntryInfo e)
        {
            using (Stream fs = open())
            {
                byte[] hdr = Util.ReadExact(fs, e.Offset, 48);
                e.Sha1 = new byte[20];
                Buffer.BlockCopy(hdr, 28, e.Sha1, 0, 20);
            }
        }

        public byte[] Read(PakEntryInfo e)
        {
            if (e.Encrypted) throw new NotSupportedException("encrypted entry " + e.Path);
            using (Stream fs = open())
            {
                if (e.Method == 0) return Util.ReadExact(fs, e.Offset + 53, e.Size);
                string method = e.Method <= Methods.Length ? Methods[e.Method - 1] : "?";
                if (!string.Equals(method, "Zlib", StringComparison.OrdinalIgnoreCase)) throw new NotSupportedException("compression " + method + " for " + e.Path);
                MemoryStream outMs = new MemoryStream((int)e.UncompressedSize);
                foreach (long[] b in e.Blocks)
                {
                    byte[] comp = Util.ReadExact(fs, e.Offset + b[0], b[1] - b[0]);

                    using (DeflateStream ds = new DeflateStream(new MemoryStream(comp, 2, comp.Length - 2), CompressionMode.Decompress))
                        ds.CopyTo(outMs);
                }
                if (outMs.Length != e.UncompressedSize) throw new InvalidDataException("zlib size " + outMs.Length + " != " + e.UncompressedSize + " for " + e.Path);
                return outMs.ToArray();
            }
        }

        public string ContentPath(PakEntryInfo e)
        {
            return ContentRelative(MountPoint, e.Path);
        }

        public static string ContentRelative(string mount, string path)
        {
            string full = (mount ?? "") + path;
            full = full.Replace('\\', '/');
            while (full.StartsWith("../")) full = full.Substring(3);
            while (full.StartsWith("/")) full = full.Substring(1);
            const string root = "Bodycam/Content/";
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return full.Substring(root.Length);
            return null;
        }

        public List<string> ContentPaths()
        {
            List<string> list = new List<string>();
            foreach (PakEntryInfo e in Entries.Values)
            {
                string c = ContentPath(e);
                if (c != null) list.Add(c);
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        public PakEntryInfo FindContent(string relPath)
        {
            foreach (PakEntryInfo e in Entries.Values)
            {
                string c = ContentPath(e);
                if (c != null && string.Equals(c, relPath, StringComparison.OrdinalIgnoreCase)) return e;
            }
            return null;
        }

        static string ReadFString(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n == 0) return "";
            if (n < 0) { byte[] u = r.ReadBytes(-n * 2); return Encoding.Unicode.GetString(u, 0, u.Length - 2); }
            byte[] a = r.ReadBytes(n);
            return Encoding.ASCII.GetString(a, 0, n - 1);
        }

        public static int ReadOrder(string pakFileName)
        {
            int order = 3;
            if (!pakFileName.EndsWith("_P.pak", StringComparison.OrdinalIgnoreCase)) return order;
            int chunk = 1;
            int end = pakFileName.LastIndexOf('_');
            if (end > 0)
            {
                int start = pakFileName.LastIndexOf('_', end - 1);
                if (start >= 0)
                {
                    string v = pakFileName.Substring(start + 1, end - start - 1);
                    int n;
                    if (v.Length > 0 && int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out n) && n >= 1) chunk = n + 1;
                }
            }
            return order + 100 * chunk;
        }
    }
}
