// Reader for the header of a cooked Unreal 5.5 package (.uasset + .uexp).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BodycamMapInstaller.Core
{
    public sealed class CookedPackageHeader
    {
        public const uint PkgFilterEditorOnly = 0x80000000;

        byte[] d;
        readonly Dictionary<string, int[]> pos = new Dictionary<string, int[]>(StringComparer.Ordinal);
        readonly Dictionary<string, long> v = new Dictionary<string, long>(StringComparer.Ordinal);
        int pkgNamePos, pkgNameEnd;

        public string PackageName;
        public string NewPackageName;
        public List<KeyValuePair<string, byte[]>> Names;
        public List<byte[]> Imports, Exports;
        public byte[] Depends, AssetRegistry;
        public List<int> Preload;
        public int SummaryEnd;
        public bool Strict;

        public long this[string field]
        {
            get { return v[field]; }
            set { v[field] = value; }
        }

        public static CookedPackageHeader Parse(byte[] uasset) { return Parse(uasset, true); }

        public static CookedPackageHeader ParseLenient(byte[] uasset) { return Parse(uasset, false); }

        static CookedPackageHeader Parse(byte[] d, bool strict)
        {
            CookedPackageHeader h = new CookedPackageHeader();
            h.d = d;
            h.Strict = strict;
            try { h.ReadSummary(); }
            catch (ArgumentException) { throw new InvalidDataException("package summary is cut short"); }
            catch (IndexOutOfRangeException) { throw new InvalidDataException("package summary is cut short"); }
            long nameOffset = h.v["NameOffset"], nameCount = h.v["NameCount"];
            if (nameOffset < 0 || nameOffset > d.Length || nameCount < 0 || nameCount > 1000000) throw new InvalidDataException("name table outside the header");
            h.Names = new List<KeyValuePair<string, byte[]>>();
            int o = (int)nameOffset;
            for (int i = 0; i < nameCount; i++)
            {
                int o2;
                string s = FStr(d, o, out o2);
                if (o2 + 4 > d.Length) throw new InvalidDataException("name table cut short");
                byte[] hash = new byte[4];
                Buffer.BlockCopy(d, o2, hash, 0, 4);
                h.Names.Add(new KeyValuePair<string, byte[]>(s, hash));
                o = o2 + 4;
            }
            long importOffset = h.v["ImportOffset"], importCount = h.v["ImportCount"], exportOffset = h.v["ExportOffset"], exportCount = h.v["ExportCount"];
            long dependsOffset = h.v["DependsOffset"];
            if (strict)
            {
                if (o != importOffset) throw new InvalidDataException("gap between names and imports");
                if (h.v["SoftObjectPathsCount"] != 0 || h.v["GatherableTextDataCount"] != 0) throw new InvalidDataException("unexpected header tables");
                if (importOffset + 32 * importCount != exportOffset) throw new InvalidDataException("imports are not 32 bytes / not followed by exports");
                if (exportOffset + 96 * exportCount != dependsOffset) throw new InvalidDataException("exports are not 96 bytes / not followed by depends");
            }
            if (importOffset < 0 || importOffset + 32 * importCount > d.Length || exportOffset < 0 || exportOffset + 96 * exportCount > d.Length)
                throw new InvalidDataException("import or export table outside the header");
            h.Imports = new List<byte[]>();
            for (int k = 0; k < importCount; k++) h.Imports.Add(Slice(d, (int)(importOffset + 32 * k), 32));
            h.Exports = new List<byte[]>();
            for (int k = 0; k < exportCount; k++) h.Exports.Add(Slice(d, (int)(exportOffset + 96 * k), 96));
            if (strict)
            {
                long ar = h.v["AssetRegistryDataOffset"], pdo = h.v["PreloadDependencyOffset"], pdc = h.v["PreloadDependencyCount"];
                h.Depends = Slice(d, (int)dependsOffset, (int)(ar - dependsOffset));
                h.AssetRegistry = Slice(d, (int)ar, (int)(pdo - ar));
                h.Preload = new List<int>();
                for (int k = 0; k < pdc; k++) h.Preload.Add(BitConverter.ToInt32(d, (int)(pdo + 4 * k)));
                if (pdo + 4 * pdc != h.v["TotalHeaderSize"]) throw new InvalidDataException("preload dependencies do not end the header");
                if (h.v["DataResourceOffset"] != -1 || h.v["PayloadTocOffset"] != -1) throw new InvalidDataException("unexpected data resources or payload table");
            }
            return h;
        }

        static byte[] Slice(byte[] d, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > d.Length) throw new InvalidDataException("header slice outside the file");
            byte[] b = new byte[count];
            Buffer.BlockCopy(d, offset, b, 0, count);
            return b;
        }

        int I32(string name, int o) { v[name] = BitConverter.ToInt32(d, o); pos[name] = new int[] { o, 4 }; return o + 4; }
        int U32(string name, int o) { v[name] = BitConverter.ToUInt32(d, o); pos[name] = new int[] { o, 4 }; return o + 4; }
        int I64(string name, int o) { v[name] = BitConverter.ToInt64(d, o); pos[name] = new int[] { o, 8 }; return o + 8; }

        void ReadSummary()
        {
            int o = 24;
            int ncv = BitConverter.ToInt32(d, o);
            o += 4 + ncv * 20;
            o = I32("TotalHeaderSize", o);
            pkgNamePos = o;
            PackageName = FStr(d, o, out o);
            pkgNameEnd = o;
            o = U32("PackageFlags", o);
            o = I32("NameCount", o);
            o = I32("NameOffset", o);
            o = I32("SoftObjectPathsCount", o);
            o = I32("SoftObjectPathsOffset", o);
            bool editorOnly = ((uint)v["PackageFlags"] & PkgFilterEditorOnly) != 0;
            if (!editorOnly) FStr(d, o, out o);
            o = I32("GatherableTextDataCount", o);
            o = I32("GatherableTextDataOffset", o);
            o = I32("ExportCount", o);
            o = I32("ExportOffset", o);
            o = I32("ImportCount", o);
            o = I32("ImportOffset", o);
            o = I32("DependsOffset", o);
            o = I32("SoftPackageReferencesCount", o);
            o = I32("SoftPackageReferencesOffset", o);
            o = I32("SearchableNamesOffset", o);
            o = I32("ThumbnailTableOffset", o);
            o += 16;
            if (!editorOnly) o += 16;
            int gen = BitConverter.ToInt32(d, o);
            o += 4 + gen * 8;
            for (int k = 0; k < 2; k++)
            {
                o += 2 + 2 + 2 + 4;
                FStr(d, o, out o);
            }
            o += 4;
            int chunks = BitConverter.ToInt32(d, o);
            o += 4;
            if (chunks != 0) throw new InvalidDataException("compressed chunks");
            o += 4;
            int extra = BitConverter.ToInt32(d, o);
            o += 4;
            for (int k = 0; k < extra; k++) FStr(d, o, out o);
            int nta = BitConverter.ToInt32(d, o);
            if (nta == 0) o += 4;
            o = I32("AssetRegistryDataOffset", o);
            o = I64("BulkDataStartOffset", o);
            o = I32("WorldTileInfoDataOffset", o);
            int n = BitConverter.ToInt32(d, o);
            o += 4 + 4 * n;
            o = I32("PreloadDependencyCount", o);
            o = I32("PreloadDependencyOffset", o);
            o = I32("NamesReferencedFromExportDataCount", o);
            o = I64("PayloadTocOffset", o);
            o = I32("DataResourceOffset", o);
            SummaryEnd = o;
        }

        public static string FStr(byte[] b, int o, out int end)
        {
            int n = BitConverter.ToInt32(b, o);
            o += 4;
            if (n < 0)
            {
                int bytes = -n * 2;
                if (o + bytes > b.Length) throw new InvalidDataException("string outside the header");
                end = o + bytes;
                return Encoding.Unicode.GetString(b, o, bytes - 2);
            }
            if (o + n > b.Length) throw new InvalidDataException("string outside the header");
            end = o + n;
            if (n == 0) return "";
            return Encoding.GetEncoding(28591).GetString(b, o, n - 1);
        }

        public static byte[] EncName(string s)
        {
            bool ascii = true;
            foreach (char c in s) if (c >= 128) ascii = false;
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            if (ascii)
            {
                byte[] raw = Encoding.GetEncoding(28591).GetBytes(s);
                w.Write(raw.Length + 1); w.Write(raw); w.Write((byte)0);
            }
            else
            {
                byte[] raw = Encoding.Unicode.GetBytes(s);
                w.Write(-(raw.Length / 2 + 1)); w.Write(raw); w.Write((short)0);
            }
            return ms.ToArray();
        }

        public static byte[] NameEntry(string s)
        {
            byte[] e = EncName(s);
            byte[] r = new byte[e.Length + 4];
            Buffer.BlockCopy(e, 0, r, 0, e.Length);
            Buffer.BlockCopy(NameHash(s), 0, r, e.Length, 4);
            return r;
        }

        public static byte[] NameHash(string s)
        {
            ushort a = (ushort)(PakHash.StrihashDeprecated(s) & 0xFFFF), b = (ushort)(PakHash.StrCrc32(s) & 0xFFFF);
            return new byte[] { (byte)a, (byte)(a >> 8), (byte)b, (byte)(b >> 8) };
        }

        public List<string> BadHashes()
        {
            List<string> bad = new List<string>();
            foreach (KeyValuePair<string, byte[]> n in Names)
                if (!Util.BytesEqual(NameHash(n.Key), n.Value)) bad.Add(n.Key);
            return bad;
        }

        public int FindName(string s)
        {
            for (int i = 0; i < Names.Count; i++) if (Names[i].Key == s) return i;
            return -1;
        }

        public string NameAt(int i)
        {
            return i >= 0 && i < Names.Count ? Names[i].Key : null;
        }

        public string ImportClassPackage(int k) { return NameAt(BitConverter.ToInt32(Imports[k], 0)); }
        public string ImportClass(int k) { return NameAt(BitConverter.ToInt32(Imports[k], 8)); }
        public int ImportOuter(int k) { return BitConverter.ToInt32(Imports[k], 16); }
        public string ImportName(int k) { return NameAt(BitConverter.ToInt32(Imports[k], 20)); }

        public long ExportSerialSize(int k) { return BitConverter.ToInt64(Exports[k], 28); }
        public long ExportSerialOffset(int k) { return BitConverter.ToInt64(Exports[k], 36); }

        public void RemapNames(IDictionary<int, int> map)
        {
            foreach (byte[] im in Imports)
                foreach (int off in new int[] { 0, 8, 20 })
                    PutI32(im, off, map[BitConverter.ToInt32(im, off)]);
            foreach (byte[] ex in Exports)
                PutI32(ex, 16, map[BitConverter.ToInt32(ex, 16)]);
        }

        public static void PutI32(byte[] b, int off, int value)
        {
            b[off] = (byte)value; b[off + 1] = (byte)(value >> 8); b[off + 2] = (byte)(value >> 16); b[off + 3] = (byte)(value >> 24);
        }

        public static void PutI64(byte[] b, int off, long value)
        {
            for (int i = 0; i < 8; i++) b[off + i] = (byte)(value >> (8 * i));
        }

        public byte[] Build(int uexpDataLength)
        {
            if (!Strict) throw new InvalidOperationException("Build needs a strictly parsed header");
            MemoryStream outMs = new MemoryStream();
            int pkgDelta = 0;
            if (NewPackageName != null)
            {
                byte[] newStr = EncName(NewPackageName);
                pkgDelta = newStr.Length - (pkgNameEnd - pkgNamePos);
                outMs.Write(d, 0, pkgNamePos);
                outMs.Write(newStr, 0, newStr.Length);
                outMs.Write(d, pkgNameEnd, (int)v["NameOffset"] - pkgNameEnd);
            }
            else outMs.Write(d, 0, (int)v["NameOffset"]);
            Dictionary<string, int> at = new Dictionary<string, int>(StringComparer.Ordinal);
            at["NameOffset"] = (int)outMs.Length;
            foreach (KeyValuePair<string, byte[]> n in Names)
            {
                byte[] e = EncName(n.Key);
                outMs.Write(e, 0, e.Length);
                outMs.Write(n.Value, 0, 4);
            }
            at["ImportOffset"] = (int)outMs.Length;
            foreach (byte[] im in Imports) outMs.Write(im, 0, im.Length);
            at["ExportOffset"] = (int)outMs.Length;
            List<int> exportPos = new List<int>();
            foreach (byte[] ex in Exports) { exportPos.Add((int)outMs.Length); outMs.Write(ex, 0, ex.Length); }
            at["DependsOffset"] = (int)outMs.Length;
            outMs.Write(Depends, 0, Depends.Length);
            at["AssetRegistryDataOffset"] = (int)outMs.Length;
            outMs.Write(AssetRegistry, 0, AssetRegistry.Length);
            at["PreloadDependencyOffset"] = (int)outMs.Length;
            foreach (int p in Preload) outMs.Write(BitConverter.GetBytes(p), 0, 4);
            byte[] o = outMs.ToArray();
            int total = o.Length;

            Put(o, "TotalHeaderSize", pkgDelta, total);
            Put(o, "NameCount", pkgDelta, Names.Count);
            Put(o, "NameOffset", pkgDelta, at["NameOffset"]);
            Put(o, "SoftObjectPathsOffset", pkgDelta, v["SoftObjectPathsOffset"] != 0 ? at["ImportOffset"] : 0);
            Put(o, "ImportCount", pkgDelta, Imports.Count);
            Put(o, "ImportOffset", pkgDelta, at["ImportOffset"]);
            Put(o, "ExportCount", pkgDelta, Exports.Count);
            Put(o, "ExportOffset", pkgDelta, at["ExportOffset"]);
            Put(o, "DependsOffset", pkgDelta, at["DependsOffset"]);
            Put(o, "AssetRegistryDataOffset", pkgDelta, at["AssetRegistryDataOffset"]);
            Put(o, "PreloadDependencyCount", pkgDelta, Preload.Count);
            Put(o, "PreloadDependencyOffset", pkgDelta, at["PreloadDependencyOffset"]);
            Put(o, "NamesReferencedFromExportDataCount", pkgDelta, v["NamesReferencedFromExportDataCount"]);
            Put(o, "BulkDataStartOffset", pkgDelta, (long)total + uexpDataLength);
            long off = total;
            for (int k = 0; k < Exports.Count; k++)
            {
                long size = BitConverter.ToInt64(Exports[k], 28);
                PutI64(o, exportPos[k] + 36, off);
                off += size;
            }
            return o;
        }

        void Put(byte[] o, string field, int pkgDelta, long value)
        {
            int[] p = pos[field];
            int at = p[0] > pkgNamePos ? p[0] + pkgDelta : p[0];
            if (p[1] == 4) PutI32(o, at, (int)value); else PutI64(o, at, value);
        }
    }
}
