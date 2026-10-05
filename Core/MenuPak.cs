// Builds zzMapCards_19_P.pak: the shared menu file with one card per installed map.
using System;
using System.Collections.Generic;
using System.IO;

namespace BodycamMapInstaller.Core
{
    public static class LegacyRows
    {
        public sealed class Scan
        {
            public string PakName;
            public bool HasTables;
            public bool HasOwnDa;
            public bool HasWrappers;
            public List<LegacyRow> Rows = new List<LegacyRow>();
            public List<string> Skipped = new List<string>();
            public List<string> ContentPaths = new List<string>();
        }

        public static IList<LegacyRow> Read(string pakPath, BaseTables baseTables, ILog log)
        {
            PakReader r = PakReader.Open(pakPath, false);
            return ScanPak(r, Path.GetFileName(pakPath), baseTables, log).Rows;
        }

        public static Scan ScanPak(PakReader r, string pakName, BaseTables baseTables, ILog log)
        {
            Scan s = new Scan();
            s.PakName = pakName;
            Dictionary<string, PakEntryInfo> byRel = new Dictionary<string, PakEntryInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (PakEntryInfo e in r.Entries.Values)
            {
                string rel = r.ContentPath(e);
                if (rel == null) continue;
                byRel[rel] = e;
                s.ContentPaths.Add(rel);
                if (rel.StartsWith("GM_Maps/", StringComparison.OrdinalIgnoreCase)) s.HasWrappers = true;
                if (rel.StartsWith("UI/MetaData/DA_", StringComparison.OrdinalIgnoreCase) && rel.EndsWith(".uasset", StringComparison.OrdinalIgnoreCase)) s.HasOwnDa = true;
            }
            s.ContentPaths.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (string rel in BaseTables.RelPaths) if (byRel.ContainsKey(rel)) s.HasTables = true;
            if (pakName.StartsWith("pakchunk", StringComparison.OrdinalIgnoreCase)) return s;

            HashSet<string> keptMapRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string stem in DataTableMerger.AllTables)
            {
                PakEntryInfo ua, ue;
                if (!byRel.TryGetValue(stem + ".uasset", out ua) || !byRel.TryGetValue(stem + ".uexp", out ue)) continue;
                IList<TableRow> rows;
                try { rows = DataTableMerger.ReadRows(new CookedTable(r.Read(ua), r.Read(ue))); }
                catch (Exception ex)
                {
                    s.Skipped.Add(stem);
                    if (log != null) log.Detail(pakName + ": the installer cannot read its " + Short(stem) + " (" + ex.Message + "); its rows there are not carried.");
                    continue;
                }
                HashSet<string> baseNames = baseTables.RowNames(stem);
                bool weather = stem == DataTableMerger.WeatherTable;
                foreach (TableRow row in rows)
                {
                    if (baseNames.Contains(row.Name)) continue;
                    bool keep;
                    if (!weather)
                    {
                        keep = row.ObjectPackage != null && row.ObjectPackage.StartsWith("/Game/", StringComparison.Ordinal)
                               && byRel.ContainsKey(row.ObjectPackage.Substring(6) + ".uasset");
                        if (keep) keptMapRows.Add(row.Name);
                    }
                    else keep = keptMapRows.Contains(row.Name);
                    if (keep)
                    {
                        LegacyRow lr = new LegacyRow();
                        lr.PakName = pakName; lr.TableRelPath = stem; lr.Row = row;
                        s.Rows.Add(lr);
                    }
                    else
                    {
                        s.Skipped.Add(stem + ":" + row.Name);
                        if (log != null) log.Detail(pakName + ": the row " + row.Name + " in " + Short(stem) + " points at " + row.ObjectPackage + ", which is not in that pak. Not carried.");
                    }
                }
            }
            return s;
        }

        static string Short(string stem)
        {
            return stem.Substring(stem.LastIndexOf('/') + 1);
        }
    }

    public sealed class MenuPakBuilder
    {
        public const string PakName = "zzMapCards_19_P.pak";

        readonly BaseTables baseTables;
        readonly IList<LegacyRow> legacy;
        readonly IList<InstalledMap> cards;
        readonly ILog log;

        public readonly Dictionary<string, List<KeyValuePair<string, string>>> ExpectedAddedRows = new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);
        public readonly Dictionary<string, CookedTable> Tables = new Dictionary<string, CookedTable>(StringComparer.Ordinal);
        public int CustomMapCount;

        public MenuPakBuilder(BaseTables baseTables, IList<LegacyRow> legacy, IList<InstalledMap> cards, ILog log)
        {
            this.baseTables = baseTables;
            this.legacy = legacy ?? new List<LegacyRow>();
            this.cards = cards ?? new List<InstalledMap>();
            this.log = log ?? new NullLog();
        }

        void Add(string stem, string rowName, string pkg, string obj)
        {
            Tables[stem] = DataTableMerger.AddRow(Tables[stem], rowName, pkg, obj);
            ExpectedAddedRows[stem].Add(new KeyValuePair<string, string>(rowName, pkg + "." + obj));
        }

        public IList<KeyValuePair<string, byte[]>> BuildEntries()
        {
            foreach (string stem in DataTableMerger.AllTables)
            {
                Tables[stem] = baseTables.Table(stem);
                ExpectedAddedRows[stem] = new List<KeyValuePair<string, string>>();
            }
            HashSet<string> seenRows = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> customNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LegacyRow lr in legacy)
            {
                string key = lr.TableRelPath + "|" + lr.Row.Name;
                if (!seenRows.Add(key))
                {
                    log.Detail(lr.PakName + ": the row " + lr.Row.Name + " is already carried from another pak. Not carried twice.");
                    continue;
                }
                Add(lr.TableRelPath, lr.Row.Name, lr.Row.ObjectPackage, lr.Row.ObjectName);
                if (lr.TableRelPath != DataTableMerger.WeatherTable) customNames.Add(lr.Row.Name);
            }
            List<KeyValuePair<string, byte[]>> entries = new List<KeyValuePair<string, byte[]>>();
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (InstalledMap m in cards)
            {
                CardInfo c = m.Card;
                foreach (string mode in c.Modes)
                {
                    string stem = DataTableMerger.TableFor(mode);
                    if (!seenRows.Add(stem + "|" + c.Id)) throw new InvalidOperationException("row " + c.Id + " twice in " + stem);
                    Add(stem, c.Id, PackagePaths.DaPackage(c.Id, mode), PackagePaths.DaObject(c.Id, mode));
                }
                if (c.WeatherDa != null)
                {
                    if (!seenRows.Add(DataTableMerger.WeatherTable + "|" + c.Id)) throw new InvalidOperationException("weather row " + c.Id + " twice");
                    Add(DataTableMerger.WeatherTable, c.Id, PackagePaths.WeatherPackage(c.WeatherDa), PackagePaths.WeatherObjectName(c.WeatherDa));
                }
                customNames.Add(c.Id);
                string menuDir = Path.Combine(m.CardDir, "menu");
                foreach (string f in Util.FilesRecursive(menuDir, null))
                {
                    string rel = Util.Relative(menuDir, f);
                    if (!paths.Add(rel)) throw new InvalidOperationException("menu file " + rel + " belongs to two cards");
                    entries.Add(new KeyValuePair<string, byte[]>(rel, File.ReadAllBytes(f)));
                }
            }
            foreach (string stem in DataTableMerger.AllTables)
            {
                IList<string> bad = DataTableMerger.Verify(Tables[stem]);
                if (bad.Count > 0) throw new InvalidDataException(stem + ": " + string.Join("; ", new List<string>(bad).ToArray()));
                foreach (string ext in new string[] { ".uasset", ".uexp" })
                {
                    string rel = stem + ext;
                    if (!paths.Add(rel)) throw new InvalidOperationException("a card ships the table file " + rel);
                    entries.Add(new KeyValuePair<string, byte[]>(rel, ext == ".uasset" ? Tables[stem].Uasset : Tables[stem].Uexp));
                }
            }
            entries.Sort(delegate(KeyValuePair<string, byte[]> a, KeyValuePair<string, byte[]> b) { return StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key); });
            CustomMapCount = customNames.Count;
            return entries;
        }

        public IList<string> CheckWrittenTables(PakReader r)
        {
            List<string> bad = new List<string>();
            foreach (string stem in DataTableMerger.AllTables)
            {
                PakEntryInfo ua = r.FindContent(stem + ".uasset"), ue = r.FindContent(stem + ".uexp");
                if (ua == null || ue == null) { bad.Add(stem + " missing"); continue; }
                IList<TableRow> got = DataTableMerger.ReadRows(new CookedTable(r.Read(ua), r.Read(ue)));
                IList<TableRow> baseRows = DataTableMerger.ReadRows(baseTables.Table(stem));
                List<KeyValuePair<string, string>> added = ExpectedAddedRows[stem];
                if (got.Count != baseRows.Count + added.Count) { bad.Add(stem + ": " + got.Count + " rows, expected " + (baseRows.Count + added.Count)); continue; }
                for (int i = 0; i < baseRows.Count; i++)
                    if (!Util.BytesEqual(got[i].Raw15, baseRows[i].Raw15) || got[i].Name != baseRows[i].Name) bad.Add(stem + ": game row " + i + " (" + baseRows[i].Name + ") changed");
                for (int i = 0; i < added.Count; i++)
                {
                    TableRow g = got[baseRows.Count + i];
                    if (g.Name != added[i].Key || (g.ObjectPackage + "." + g.ObjectName) != added[i].Value || !g.Active)
                        bad.Add(stem + ": row " + (baseRows.Count + i) + " is " + g.Name + " -> " + g.ObjectPackage + "." + g.ObjectName + ", expected " + added[i].Key + " -> " + added[i].Value);
                }
            }
            return bad;
        }
    }
}
