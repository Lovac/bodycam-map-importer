// Adds a map's row to the game's cooked map-list tables.
using System;
using System.Collections.Generic;
using System.IO;

namespace BodycamMapInstaller.Core
{
    public sealed class CookedTable
    {
        public byte[] Uasset;
        public byte[] Uexp;

        public CookedTable() { }
        public CookedTable(byte[] uasset, byte[] uexp) { Uasset = uasset; Uexp = uexp; }
    }

    public sealed class TableRow
    {
        public string Name;
        public bool Active;
        public string ObjectPackage;
        public string ObjectName;
        public byte[] Raw15;
        public int ImportIndex;
    }

    public sealed class LegacyRow
    {
        public string PakName;
        public string TableRelPath;
        public TableRow Row;
    }

    public static class DataTableMerger
    {
        public const string WeatherTable = "GM/WeatherManaer/DT_MapWeatherConfig";
        public const string HardpointTable = "UI/Menus/Play/Cards/Data/DT_UI_HardpointMaps";
        public const string TeamDeathmatchTable = "UI/Menus/Play/Cards/Data/DT_UI_TeamDeathmatchMaps";
        public const string DeathmatchTable = "UI/Menus/Play/Cards/Data/DT_UI_DeathmatchMaps";
        public const string VersusTable = "UI/Menus/Play/Cards/Data/DT_UI_VersusMaps";
        public const string GungameTable = "UI/Menus/Play/Cards/Data/DT_UI_GungameMaps";
        public const string BodybombTable = "UI/Menus/Play/Cards/Data/DT_UI_BodybombMaps";
        public const string WingmanTable = "UI/Menus/Play/Cards/Data/DT_UI_WingmanMaps";
        public static readonly string[] MapTables = { HardpointTable, TeamDeathmatchTable, DeathmatchTable, VersusTable, GungameTable, BodybombTable, WingmanTable };
        public static readonly string[] AllTables = { HardpointTable, TeamDeathmatchTable, DeathmatchTable, VersusTable, GungameTable, BodybombTable, WingmanTable, WeatherTable };
        static readonly byte[] Tag = { 0xC1, 0x83, 0x2A, 0x9E };

        public static string TableFor(string mode)
        {
            switch (mode) { case "HP": return HardpointTable; case "TDM": return TeamDeathmatchTable; case "DM": return DeathmatchTable; case "VS": return VersusTable; case "GG": return GungameTable; case "BB": return BodybombTable; case "WM": return WingmanTable; }
            throw new ArgumentException("mode " + mode);
        }

        public static CookedTable AddRow(CookedTable table, string rowName, string objectPackage, string objectName)
        {
            return AddRow(table, rowName, objectPackage, objectName, true, null);
        }

        public static CookedTable AddRow(CookedTable table, string rowName, string objectPackage, string objectName, bool active, Action<string> log)
        {
            if (log == null) log = delegate { };
            byte[] d = table.Uasset, uexp = table.Uexp;
            CookedPackageHeader H = CookedPackageHeader.Parse(d);
            if (H["ExportCount"] != 1) throw new InvalidDataException("expected a single DataTable export");
            List<string> bad = H.BadHashes();
            if (bad.Count > 0) throw new InvalidDataException("hash formula mismatch on " + string.Join(", ", bad.ToArray()));

            int nExp = (int)H["NamesReferencedFromExportDataCount"];
            int rowNameIdx;
            if (H.FindName(rowName) < 0)
            {
                int ins = nExp;
                int oldCount = H.Names.Count;
                H.Names.Insert(ins, new KeyValuePair<string, byte[]>(rowName, CookedPackageHeader.NameHash(rowName)));
                Dictionary<int, int> remap = new Dictionary<int, int>();
                for (int i = 0; i < oldCount; i++) remap[i] = i < ins ? i : i + 1;
                H.RemapNames(remap);
                H["NamesReferencedFromExportDataCount"] = nExp + 1;
                rowNameIdx = ins;
                log("  name '" + rowName + "' inserted at index " + ins + " (NamesReferencedFromExportDataCount " + nExp + " -> " + (nExp + 1) + ")");
            }
            else
            {
                rowNameIdx = H.FindName(rowName);
                log("  name '" + rowName + "' already present at index " + rowNameIdx);
            }

            int? objImport = null;
            for (int k = 0; k < H.Imports.Count; k++)
            {
                int outer = H.ImportOuter(k);
                if (H.ImportName(k) == objectName && outer < 0 && -outer - 1 < H.Imports.Count && H.ImportName(-outer - 1) == objectPackage)
                    objImport = -(k + 1);
            }
            if (objImport == null)
            {
                int proto = -1, protoPkg = -1;
                for (int k = 0; k < H.Imports.Count; k++)
                {
                    string cp = H.ImportClassPackage(k);
                    if (H.ImportOuter(k) < 0 && cp != null && cp.StartsWith("/Game/", StringComparison.Ordinal))
                    {
                        proto = k;
                        protoPkg = -H.ImportOuter(k) - 1;
                        break;
                    }
                }
                if (proto < 0) throw new InvalidDataException("no prototype object import found");
                log("  prototype imports: package " + H.ImportName(protoPkg) + " (class " + H.ImportClassPackage(protoPkg) + "." + H.ImportClass(protoPkg) + "), object " + H.ImportName(proto) + " (class " + H.ImportClassPackage(proto) + "." + H.ImportClass(proto) + ")");
                int pkgNameIdx = NameIdx(H, objectPackage, log);
                byte[] pkgEntry = (byte[])H.Imports[protoPkg].Clone();
                CookedPackageHeader.PutI32(pkgEntry, 20, pkgNameIdx);
                CookedPackageHeader.PutI32(pkgEntry, 24, 0);
                H.Imports.Add(pkgEntry);
                int pkgImport = -H.Imports.Count;
                int objNameIdx = NameIdx(H, objectName, log);
                byte[] objEntry = (byte[])H.Imports[proto].Clone();
                CookedPackageHeader.PutI32(objEntry, 16, pkgImport);
                CookedPackageHeader.PutI32(objEntry, 20, objNameIdx);
                CookedPackageHeader.PutI32(objEntry, 24, 0);
                H.Imports.Add(objEntry);
                objImport = -H.Imports.Count;
                log("  imports appended: " + pkgImport + " = Package " + objectPackage + ", " + objImport + " = " + H.ImportClass(proto) + " " + objectName + " (outer " + pkgImport + ")");

                byte[] ex0 = H.Exports[0];
                int first = BitConverter.ToInt32(ex0, 76), sbs = BitConverter.ToInt32(ex0, 80), cbs = BitConverter.ToInt32(ex0, 84);
                int insertAt = first + sbs + cbs;
                H.Preload.Insert(insertAt, objImport.Value);
                CookedPackageHeader.PutI32(ex0, 84, cbs + 1);
                log("  preload dependency " + objImport + " inserted at position " + insertAt + " (CreateBeforeSerialization " + cbs + " -> " + (cbs + 1) + ")");
            }
            else log("  object import already present: " + objImport);

            byte[] ex = H.Exports[0];
            long size = BitConverter.ToInt64(ex, 28), off = BitConverter.ToInt64(ex, 36);
            if (off != H["TotalHeaderSize"] || size + 4 != uexp.Length || !EndsWithTag(uexp)) throw new InvalidDataException("unexpected uexp layout");
            if (uexp.Length < 18 || BitConverter.ToUInt16(uexp, 0) != 0x0300) throw new InvalidDataException("DataTable header not 00 03");
            const int countPos = 2 + 4 + 4;
            int count = BitConverter.ToInt32(uexp, countPos);
            byte[] row = new byte[15];
            CookedPackageHeader.PutI32(row, 0, rowNameIdx);
            CookedPackageHeader.PutI32(row, 4, 0);
            row[8] = active ? (byte)0x00 : (byte)0x80; row[9] = 0x05; row[10] = 0x01;
            CookedPackageHeader.PutI32(row, 11, objImport.Value);
            byte[] data = new byte[uexp.Length - 4 + 15];
            Buffer.BlockCopy(uexp, 0, data, 0, uexp.Length - 4);
            Buffer.BlockCopy(row, 0, data, uexp.Length - 4, 15);
            CookedPackageHeader.PutI32(data, countPos, count + 1);
            CookedPackageHeader.PutI64(ex, 28, data.Length);
            log("  row " + count + " appended: " + SpacedHex(row) + " (15 bytes), row count " + count + " -> " + (count + 1) + ", SerialSize " + size + " -> " + data.Length);
            byte[] newHdr = H.Build(data.Length);
            byte[] newUexp = new byte[data.Length + 4];
            Buffer.BlockCopy(data, 0, newUexp, 0, data.Length);
            Buffer.BlockCopy(Tag, 0, newUexp, data.Length, 4);
            log("  uasset " + d.Length + " -> " + newHdr.Length + " bytes, uexp " + uexp.Length + " -> " + newUexp.Length + " bytes");
            return new CookedTable(newHdr, newUexp);
        }

        static int NameIdx(CookedPackageHeader H, string s, Action<string> log)
        {
            int i = H.FindName(s);
            if (i < 0)
            {
                H.Names.Add(new KeyValuePair<string, byte[]>(s, CookedPackageHeader.NameHash(s)));
                i = H.Names.Count - 1;
                log("  name '" + s + "' appended at index " + i);
            }
            return i;
        }

        static bool EndsWithTag(byte[] b)
        {
            int n = b.Length;
            return n >= 4 && b[n - 4] == Tag[0] && b[n - 3] == Tag[1] && b[n - 2] == Tag[2] && b[n - 1] == Tag[3];
        }

        public static string SpacedHex(byte[] b)
        {
            List<string> p = new List<string>();
            foreach (byte x in b) p.Add(x.ToString("x2"));
            return string.Join(" ", p.ToArray());
        }

        public static IList<TableRow> ReadRows(CookedTable table)
        {
            CookedPackageHeader H = CookedPackageHeader.ParseLenient(table.Uasset);
            if (H.Exports.Count < 1) throw new InvalidDataException("no export");
            byte[] uexp = table.Uexp;
            long size = H.ExportSerialSize(0);
            if (size + 4 != uexp.Length || !EndsWithTag(uexp)) throw new InvalidDataException("unexpected uexp layout");
            if (size < 14 || BitConverter.ToUInt16(uexp, 0) != 0x0300) throw new InvalidDataException("DataTable header not 00 03");
            int count = BitConverter.ToInt32(uexp, 10);
            if (count < 0 || 14 + 15L * count != size) throw new InvalidDataException("rows are not the 15-byte map-row layout");
            List<TableRow> rows = new List<TableRow>();
            for (int k = 0; k < count; k++)
            {
                int o = 14 + 15 * k;
                TableRow r = new TableRow();
                r.Raw15 = new byte[15];
                Buffer.BlockCopy(uexp, o, r.Raw15, 0, 15);
                int nameIdx = BitConverter.ToInt32(uexp, o), number = BitConverter.ToInt32(uexp, o + 4);
                string name = H.NameAt(nameIdx);
                if (name == null) throw new InvalidDataException("row " + k + " name index " + nameIdx);
                r.Name = number == 0 ? name : name + "_" + (number - 1);
                if (uexp[o + 9] != 0x05 || uexp[o + 10] != 0x01 || (uexp[o + 8] != 0x00 && uexp[o + 8] != 0x80)) throw new InvalidDataException("row " + k + " fragment header");
                r.Active = uexp[o + 8] == 0x00;
                r.ImportIndex = BitConverter.ToInt32(uexp, o + 11);
                if (r.ImportIndex < 0 && -r.ImportIndex - 1 < H.Imports.Count)
                {
                    int ki = -r.ImportIndex - 1;
                    r.ObjectName = H.ImportName(ki);
                    int outer = H.ImportOuter(ki);
                    if (outer < 0 && -outer - 1 < H.Imports.Count) r.ObjectPackage = H.ImportName(-outer - 1);
                }
                rows.Add(r);
            }
            return rows;
        }

        public static IList<string> Verify(CookedTable table)
        {
            List<string> bad = new List<string>();
            CookedPackageHeader H;
            try { H = CookedPackageHeader.Parse(table.Uasset); }
            catch (InvalidDataException hx) { bad.Add("header: " + hx.Message); return bad; }
            byte[] d = table.Uasset, uexp = table.Uexp;
            if (H["TotalHeaderSize"] != d.Length) bad.Add("TotalHeaderSize " + H["TotalHeaderSize"] + " != file size " + d.Length);
            List<string> hashes = H.BadHashes();
            if (hashes.Count > 0) bad.Add("name hashes differ for " + string.Join(", ", hashes.ToArray()));
            for (int k = 0; k < H.Imports.Count; k++)
            {
                int outer = H.ImportOuter(k);
                if (!(-H.Imports.Count <= outer && outer <= 0)) bad.Add("import " + (-(k + 1)) + " outer " + outer + " out of range");
                if (H.ImportName(k) == null || H.ImportClass(k) == null || H.ImportClassPackage(k) == null) bad.Add("import " + (-(k + 1)) + " name index out of range");
            }
            if (H.Exports.Count < 1) { bad.Add("no export"); return bad; }
            long size = H.ExportSerialSize(0), off = H.ExportSerialOffset(0);
            if (off != H["TotalHeaderSize"]) bad.Add("export SerialOffset " + off + " != TotalHeaderSize");
            if (!(size + 4 == uexp.Length && EndsWithTag(uexp))) bad.Add("export SerialSize " + size + " + tag != uexp " + uexp.Length + " bytes");
            if (H["BulkDataStartOffset"] != H["TotalHeaderSize"] + size) bad.Add("BulkDataStartOffset " + H["BulkDataStartOffset"] + " != header + export data");
            byte[] ex = H.Exports[0];
            int first = BitConverter.ToInt32(ex, 76), sbs = BitConverter.ToInt32(ex, 80), cbs = BitConverter.ToInt32(ex, 84), sbc = BitConverter.ToInt32(ex, 88), cbc = BitConverter.ToInt32(ex, 92);
            if (first + sbs + cbs + sbc + cbc != H.Preload.Count) bad.Add("preload groups " + sbs + "+" + cbs + "+" + sbc + "+" + cbc + " != PreloadDependencyCount " + H.Preload.Count);
            foreach (int p in H.Preload)
                if (!((-H.Imports.Count <= p && p < 0) || (0 < p && p <= H.Exports.Count))) bad.Add("preload dependency " + p + " does not resolve");

            CookedPackageHeader L = CookedPackageHeader.ParseLenient(d);
            if (L.Names.Count != H.Names.Count) bad.Add("lenient reader sees " + L.Names.Count + " names");
            else for (int i = 0; i < L.Names.Count; i++) if (L.Names[i].Key != H.Names[i].Key) { bad.Add("lenient reader name " + i + " differs"); break; }
            if (L.PackageName != H.PackageName) bad.Add("summary PackageName differs between readers");
            try { ReadRows(table); }
            catch (InvalidDataException e2) { bad.Add("rows: " + e2.Message); }
            return bad;
        }
    }
}
