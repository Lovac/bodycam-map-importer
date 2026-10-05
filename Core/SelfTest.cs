// --selftest: runs the whole installer against a fake game folder in %TEMP%.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace BodycamMapInstaller.Core
{
    public sealed class SelfTestOptions
    {
        public string FixturesDir, LegacyPak, ReportPath, PakPy, UnrealPak, BashExe, Python;
    }

    public static class SelfTest
    {
        public static int Run(string fakeGameDir, string fixturesDir, string legacyPak, string reportPath, ILog log)
        {
            SelfTestOptions o = new SelfTestOptions();
            o.FixturesDir = fixturesDir; o.LegacyPak = legacyPak; o.ReportPath = reportPath;
            return Run(fakeGameDir, o, log);
        }

        public static int Run(string fakeGameDir, SelfTestOptions options, ILog log)
        {
            return new SelfTestRun(options ?? new SelfTestOptions(), log ?? new NullLog()).Go(fakeGameDir);
        }
    }

    sealed class SkipStep : Exception
    {
        public SkipStep(string why) : base(why) { }
    }

    sealed class SelfTestRun
    {
        readonly SelfTestOptions opt;
        readonly ILog outLog;
        StreamWriter report;
        int fails, passes, skips;
        string root, work, fixtures, legacy;
        GameInstall game;
        Dictionary<string, byte[]> fx;

        static readonly string[] TableMd5 = { "ba88f936", "a0d88a77", "1d1d365b", "b421ae35", "1d84e9ac", "7fd39275",
            "2e8ba79e", "1d02c86f", "414426c4", "35ab7448", "0bde9a8b", "a63bf6fe", "0b421955", "a0ba617e",   // 22 Sep: VS/GG/BB/WM, untouched by the 3-mode Prison card = base md5s
            "d01e7126", "8b6bb745" };
        const string PrisonLevel = "/Game/Sandbox/Trenches/Prison_BP";
        const string PaintballWeather = "/Game/GM/WeatherManaer/DataAssets/DA_PaintballWeatherConfig";

        public SelfTestRun(SelfTestOptions opt, ILog outLog) { this.opt = opt; this.outLog = outLog; }

        void Say(string line)
        {
            if (report != null) { report.WriteLine(line); report.Flush(); }
            outLog.Info(line);
        }

        delegate string StepFn();

        void Step(string name, StepFn fn)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                string detail = fn();
                passes++;
                Say("PASS " + name + "  (" + sw.ElapsedMilliseconds + " ms)  " + detail);
            }
            catch (SkipStep s)
            {
                skips++;
                Say("SKIP " + name + "  " + s.Message);
            }
            catch (Exception ex)
            {
                fails++;
                Say("FAIL " + name + "  (" + sw.ElapsedMilliseconds + " ms)  " + ex.GetType().Name + ": " + ex.Message.Replace("\r", " ").Replace("\n", " | "));
            }
        }

        static void Check(bool ok, string what)
        {
            if (!ok) throw new InvalidOperationException(what);
        }

        void Detail(ListLog l)
        {
            if (report == null) return;
            lock (l.Lines) foreach (string s in l.Lines) report.WriteLine("      | " + s);
            report.Flush();
        }

        static string FindUp(string start, string rel)
        {
            string dir = start;
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string p = Path.Combine(dir, rel);
                if (File.Exists(p) || Directory.Exists(p)) return p;
                DirectoryInfo parent = Directory.GetParent(dir);
                dir = parent == null ? null : parent.FullName;
            }
            return null;
        }

        int Refuse(string why)
        {
            outLog.Block("selftest refused: " + why);
            return 2;
        }

        public int Go(string fakeGameDir)
        {
            if (string.IsNullOrEmpty(fakeGameDir)) return Refuse("give a new folder under %TEMP%");
            string full = Path.GetFullPath(fakeGameDir).TrimEnd('\\', '/');
            string temp = Path.GetFullPath(Path.GetTempPath());
            if (!Util.IsUnder(full, temp) || string.Equals(Util.FullDir(full), Util.FullDir(temp), StringComparison.OrdinalIgnoreCase))
                return Refuse(full + " is not a folder under " + temp);
            if (full.IndexOf("\\steamapps\\", StringComparison.OrdinalIgnoreCase) >= 0) return Refuse(full + " is inside a Steam library");
            foreach (string lib in GameLocator.SteamLibraries())
            {
                try { if (Util.IsUnder(full, lib)) return Refuse(full + " is inside the Steam library " + lib); }
                catch (Exception) { }
            }
            root = full;
            if (Directory.Exists(root) && Directory.GetFileSystemEntries(root).Length > 0)
            {
                if (!File.Exists(Path.Combine(root, GameInstall.SelftestMarker))) return Refuse(root + " is not empty and was not made by an earlier selftest");
                root = Path.Combine(root, "rerun_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture));
                if (Directory.Exists(root)) return Refuse(root + " already exists");
            }
            string exe = Path.Combine(root, @"Bodycam\Binaries\Win64\" + GameLocator.ShippingExe + ".exe");
            if (File.Exists(exe) && new FileInfo(exe).Length != 0) return Refuse(exe + " is not a 0-byte fake");

            string here = AppDomain.CurrentDomain.BaseDirectory;
            fixtures = opt.FixturesDir ?? FindUp(here, @"Saved\menu_test\menu_active_thumb\Bodycam\Content");
            legacy = opt.LegacyPak ?? FindUp(here, @"Saved\menu_test\PanopticonPrison_P.pak");
            if (fixtures == null || !Directory.Exists(fixtures)) return Refuse("the Prison menu fixtures were not found; pass --fixtures <.../menu_active_thumb/Bodycam/Content>");
            if (legacy == null || !File.Exists(legacy)) return Refuse("the legacy pak was not found; pass --legacy <.../PanopticonPrison_P.pak>");

            Directory.CreateDirectory(root);
            work = Path.Combine(root, "work");
            Directory.CreateDirectory(work);
            string reportPath = opt.ReportPath ?? Path.Combine(root, "SELFTEST_REPORT.txt");
            try { report = new StreamWriter(new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write), Util.Utf8); }
            catch (IOException) { return Refuse(reportPath + " already exists"); }
            report.NewLine = "\n";
            using (report)
            {
                Say("Bodycam Map Installer selftest  v" + Util.InstallerVersion + "  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture) + "  clr " + Environment.Version + "  " + (IntPtr.Size * 8) + "-bit");
                Say("fake game " + root);
                Say("fixtures  " + fixtures);
                Say("legacy    " + legacy + " (read only)");
                Steps();
                Say("summary: " + passes + " PASS, " + fails + " FAIL, " + skips + " SKIP; the fake folder stays at " + root + " (nothing deleted)");
                Say(fails == 0 ? "RESULT PASS" : "RESULT FAIL " + fails);
            }
            return fails == 0 ? 0 : 1;
        }

        Dictionary<string, byte[]> Fixtures()
        {
            if (fx != null) return fx;
            fx = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (string f in Util.FilesRecursive(fixtures, null)) fx[Util.Relative(fixtures, f)] = File.ReadAllBytes(f);
            return fx;
        }

        byte[] Fx(string rel) { return Fixtures()[rel]; }

        static bool IsNewModeRel(string rel)
        {
            foreach (string stem in new string[] { DataTableMerger.VersusTable, DataTableMerger.GungameTable, DataTableMerger.BodybombTable, DataTableMerger.WingmanTable })
                if (rel.StartsWith(stem)) return true;
            return false;
        }

        static bool IsNewMode(string stem)
        {
            return stem == DataTableMerger.VersusTable || stem == DataTableMerger.GungameTable || stem == DataTableMerger.BodybombTable || stem == DataTableMerger.WingmanTable;
        }

        Installer NewInstaller(GameInstall g, ListLog l, bool yes)
        {
            Installer i = new Installer(g, BaseTables.Embedded(), l, delegate(string t, string b, string y, string n) { l.Info("ASK " + t + " -> " + (yes ? y : n)); return yes; });
            i.IsGameRunning = delegate { return false; };
            return i;
        }

        static byte[] Dummy(string what, int size)
        {
            byte[] b = new byte[size];
            byte[] t = Encoding.ASCII.GetBytes("selftest dummy " + what + " ");
            for (int i = 0; i < size; i++) b[i] = t[i % t.Length];
            return b;
        }

        static byte[] DummyPak(string pakName, IEnumerable<string> paths)
        {
            List<KeyValuePair<string, byte[]>> files = new List<KeyValuePair<string, byte[]>>();
            foreach (string p in paths) files.Add(new KeyValuePair<string, byte[]>(p, Dummy(p, 700)));
            files.Sort(delegate(KeyValuePair<string, byte[]> a, KeyValuePair<string, byte[]> b) { return StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key); });
            return PakWriter.Build(pakName, PakWriter.ContentMount, files);
        }

        static CardInfo PrisonCard(string version)
        {
            CardInfo c = new CardInfo();
            c.Format = 1; c.Id = "Prison"; c.DisplayName = "Prison"; c.Description = "Custom map: the panopticon"; c.Version = version; c.Author = "Lovac";
            c.Modes = new string[] { "HP", "TDM", "DM" }; c.LevelPackage = PrisonLevel; c.Thumbnail = "thumb.png";
            c.MenuTexture = "/Game/UI/Textures/Maps/T_UI_Map_Prison_Thumb0"; c.WeatherDa = PaintballWeather;
            return c;
        }

        Dictionary<string, byte[]> PrisonFiles(string modesPakExtraPath, string contentExtraPath)
        {
            Dictionary<string, byte[]> f = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            f["thumb.png"] = Util.MakePng(512, 256, 0x26, 0x28, 0x2F);
            List<string> content = new List<string> { "Sandbox/Trenches/Prison_BP.umap", "Sandbox/Trenches/Prison_BP.uexp" };
            if (contentExtraPath != null) content.Add(contentExtraPath);
            f["paks/zPrison_9_P.pak"] = DummyPak("zPrison_9_P.pak", content);
            List<string> wrappers = new List<string>();
            foreach (string m in new string[] { "HP", "TDM", "DM" }) { wrappers.Add(PackagePaths.WrapperPath("Prison", m) + ".umap"); wrappers.Add(PackagePaths.WrapperPath("Prison", m) + ".uexp"); }
            if (modesPakExtraPath != null) wrappers.Add(modesPakExtraPath);
            f["paks/zPrison_Modes_9_P.pak"] = DummyPak("zPrison_Modes_9_P.pak", wrappers);
            foreach (string m in new string[] { "HP", "TDM", "DM" })
                foreach (string ext in new string[] { ".uasset", ".uexp" })
                    f["menu/UI/MetaData/DA_Prison_" + m + ext] = Fx("UI/MetaData/DA_Prison_" + m + ext);
            foreach (string ext in new string[] { ".uasset", ".uexp" })
                f["menu/UI/Textures/Maps/T_UI_Map_Prison_Thumb0" + ext] = Fx("UI/Textures/Maps/T_UI_Map_Prison_Thumb0" + ext);
            return f;
        }

        string MakeZip(string fileName, CardInfo card, IDictionary<string, byte[]> files)
        {
            string p = Path.Combine(work, fileName);
            CardPackage.Create(p, card, files);
            return p;
        }

        GameInstall MakeFakeGame(string dir)
        {
            Directory.CreateDirectory(Path.Combine(dir, @"Bodycam\Binaries\Win64"));
            Directory.CreateDirectory(Path.Combine(dir, @"Bodycam\Content\Paks"));
            Util.WriteNew(Path.Combine(dir, @"Bodycam\Binaries\Win64\" + GameLocator.ShippingExe + ".exe"), new byte[0]);
            Util.WriteNew(Path.Combine(dir, GameInstall.SelftestMarker), Encoding.ASCII.GetBytes("Fake Bodycam folder made by BodycamMapInstaller --selftest. Safe to inspect.\n"));
            return GameLocator.FromUserPath(dir);
        }

        static List<string> PaksIn(string dir, bool subfoldersOnly)
        {
            List<string> list = new List<string>();
            foreach (string f in Util.FilesRecursive(dir, ".pak"))
            {
                string rel = Util.Relative(dir, f);
                if (!subfoldersOnly || rel.IndexOf('/') >= 0) list.Add(rel);
            }
            return list;
        }

        List<string> BackupDirs(GameInstall g)
        {
            List<string> d = new List<string>();
            if (Directory.Exists(g.BackupRoot)) foreach (string x in Directory.GetDirectories(g.BackupRoot)) d.Add(Path.GetFileName(x));
            d.Sort(StringComparer.Ordinal);
            return d;
        }

        string NewestBackup(GameInstall g)
        {
            List<string> d = BackupDirs(g);
            return d.Count == 0 ? null : Path.Combine(g.BackupRoot, d[d.Count - 1]);
        }

        static Dictionary<string, byte[]> ReadPakFiles(string pak)
        {
            PakReader r = PakReader.Open(pak);
            Dictionary<string, byte[]> m = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (PakEntryInfo e in r.Entries.Values) m[r.ContentPath(e)] = r.Read(e);
            return m;
        }

        string TablesEqualFixtures(Dictionary<string, byte[]> files)
        {
            for (int i = 0; i < BaseTables.RelPaths.Length; i++)
            {
                string rel = BaseTables.RelPaths[i];
                Check(files.ContainsKey(rel), "menu pak lacks " + rel);
                Check(Util.BytesEqual(files[rel], Fx(rel)), rel + " differs from the fixture (md5 " + Util.Md5Hex(files[rel]) + ")");
                Check(Util.Md5Hex(files[rel]).StartsWith(TableMd5[i]), rel + " md5 prefix " + Util.Md5Hex(files[rel]).Substring(0, 8) + " expected " + TableMd5[i]);
            }
            return "16 tables byte-identical to the fixtures (md5 " + string.Join(" ", TableMd5) + ")";
        }

        static string Snapshot(string dir)
        {
            StringBuilder sb = new StringBuilder();
            foreach (string f in Util.FilesRecursive(dir, null)) sb.Append(Util.Relative(dir, f)).Append('|').Append(new FileInfo(f).Length).Append('\n');
            return sb.ToString();
        }

        void Steps()
        {
            string zip100 = null, zip101 = null, zip101r = null, zip102 = null, zipTower = null;
            string[] menuShaAfterInstall = new string[1];

            Step("01.layout", delegate
            {
                game = MakeFakeGame(root);
                Check(game != null, "FromUserPath did not accept the fake root");
                Check(game.IsSelftestFake && game.Source == "Selftest", "fake game not recognised as a selftest folder");
                GameInstall viaPaks = GameLocator.FromUserPath(game.PaksDir), viaExe = GameLocator.FromUserPath(game.ExePath), viaWin64 = GameLocator.FromUserPath(Path.GetDirectoryName(game.ExePath));
                Check(viaPaks != null && viaExe != null && viaWin64 != null && viaPaks.Root == game.Root && viaExe.Root == game.Root && viaWin64.Root == game.Root, "Paks folder / exe / Win64 folder do not normalise to the root");
                Check(GameLocator.FromUserPath(Directory.GetParent(root).FullName) == null, "a folder that is not Bodycam was accepted");
                BaseCheck bc = BaseTables.Embedded().CheckAgainst(GamePakIndex.Load(game, null));
                Check(bc.Result == BaseCheck.Status.PaksMissing, "base check " + bc.Result);
                return "fake exe 0 bytes, Paks folder, marker; FromUserPath accepts root, Paks, Win64 and the exe; BaseTables.CheckAgainst = PaksMissing";
            });

            Step("02.hash.sha1_strcrc32_strihash_fnv64", Hashes);
            Step("02.base.embedded_tables", delegate
            {
                BaseTables bt = BaseTables.Embedded();
                Check(bt.FingerprintProblems().Count == 0, "fingerprints");
                long total = 0;
                foreach (string rel in BaseTables.RelPaths) total += bt.Get(rel).Length;
                Check(total == 25438, "embedded size " + total);
                foreach (string stem in DataTableMerger.AllTables) Check(DataTableMerger.Verify(bt.Table(stem)).Count == 0, "verify " + stem);
                Check(bt.RowNames(DataTableMerger.TeamDeathmatchTable).Count == 15 && bt.RowNames(DataTableMerger.DeathmatchTable).Count == 16 && bt.RowNames(DataTableMerger.HardpointTable).Count == 9 && bt.RowNames(DataTableMerger.WeatherTable).Count == 11
                    && bt.RowNames(DataTableMerger.VersusTable).Count == 14 && bt.RowNames(DataTableMerger.GungameTable).Count == 14 && bt.RowNames(DataTableMerger.BodybombTable).Count == 13 && bt.RowNames(DataTableMerger.WingmanTable).Count == 10, "row counts");
                Check(bt.NameTablesContain("Paintball") && !bt.NameTablesContain("Prison") && !bt.NameTablesContain("TowerTest"), "name-table rule");
                return "16 files, 25438 bytes, md5s of BCMAP 5.1, dt_add_row.verify port clean, rows HP 9 / TDM 15 / DM 16 / VS 14 / GG 14 / BB 13 / WM 10 / weather 11";
            });
            string probePak = null;
            Step("02.pak.write_v11", delegate
            {
                string dir = Path.Combine(work, @"probe\paks out (x86)");
                Directory.CreateDirectory(dir);
                probePak = Path.Combine(dir, MenuPakBuilder.PakName);
                List<KeyValuePair<string, byte[]>> files = FixtureList();
                PakWriter.Write(probePak, PakWriter.ContentMount, files);
                byte[] mem = PakWriter.Build(MenuPakBuilder.PakName, PakWriter.ContentMount, files);
                Check(Util.BytesEqual(mem, File.ReadAllBytes(probePak)), "stream writer and file writer differ");
                bool refused = false;
                try { PakWriter.Write(probePak, PakWriter.ContentMount, files); } catch (IOException) { refused = true; }
                Check(refused, "PakWriter overwrote an existing file");
                return "16 fixture files -> " + new FileInfo(probePak).Length + " bytes in a folder named 'paks out (x86)'; in-memory build identical; a second write over it refused";
            });
            Step("02.pak.read_back", delegate
            {
                PakReader r = PakReader.Open(probePak);
                Check(r.Version == 11 && r.MountPoint == PakWriter.ContentMount, "version/mount");
                List<KeyValuePair<string, byte[]>> want = FixtureList();
                Check(r.Entries.Count == want.Count, "entries " + r.Entries.Count);
                foreach (KeyValuePair<string, byte[]> kv in want)
                {
                    PakEntryInfo e;
                    Check(r.Entries.TryGetValue(kv.Key, out e), "missing " + kv.Key);
                    Check(Util.BytesEqual(r.Read(e), kv.Value), "bytes differ " + kv.Key);
                    Check(Util.BytesEqual(e.Sha1, Util.Sha1(kv.Value)), "stored sha1 " + kv.Key);
                    Check(PakHash.PathHash(kv.Key, r.PathHashSeed) == e.PathHash, "path hash " + kv.Key);
                }
                Check(r.PathHashSeed == 0x69D81802UL, "seed " + r.PathHashSeed.ToString("X8"));
                Check(PakReader.ReadOrder(MenuPakBuilder.PakName) == 2003 && PakReader.ReadOrder("zPrison_Modes_9_P.pak") == 1003 && PakReader.ReadOrder("PanopticonPrison_P.pak") == 103, "ReadOrder");
                byte[] corrupt = File.ReadAllBytes(probePak);
                corrupt[corrupt.Length - 221 - 40] ^= 0xFF;
                string cpath = Path.Combine(work, @"probe\corrupt_index.pak");
                Util.WriteNew(cpath, corrupt);
                bool refused = false;
                try { PakReader.Open(cpath); } catch (InvalidDataException) { refused = true; }
                Check(refused, "a pak with one index byte flipped was read");
                return "16 entries byte-identical, 3 index SHA1s valid, seed 0x69D81802, ReadOrder 2003 / 1003 / 103; one flipped index byte refused";
            });
            Step("02.pak.read_zlib_legacy", delegate
            {
                PakReader r = PakReader.Open(legacy);
                int zlib = 0, blocks = 0;
                foreach (KeyValuePair<string, byte[]> kv in FixtureList())
                {
                    PakEntryInfo e = r.FindContent(kv.Key);
                    if (e == null && IsNewModeRel(kv.Key)) continue;   // 22 Sep: the legacy pak predates the VS/GG/BB/WM tables
                    Check(e != null, "legacy pak lacks " + kv.Key);
                    Check(Util.BytesEqual(r.Read(e), kv.Value), "legacy bytes differ " + kv.Key);
                    if (e.Method != 0) { zlib++; blocks += e.Blocks.Count; }
                }
                return "16 menu files of " + Path.GetFileName(legacy) + " equal the fixtures (" + zlib + " Zlib, " + blocks + " blocks), " + r.Entries.Count + " entries";
            });
            Step("02.json.card", delegate
            {
                byte[] text = Encoding.UTF8.GetBytes("{\n  \"format\": 1, \"id\": \"Prison\", \"display_name\": \"Prison\", \"description\": \"Custom map: the panopticon\", \"version\": \"1.0.0\", \"author\": \"Lovac\",\n  \"modes\": [\"HP\", \"TDM\", \"DM\"], \"level_package\": \"/Game/Sandbox/Trenches/Prison_BP\", \"thumbnail\": \"thumb.png\",\n  \"menu_texture\": \"/Game/UI/Textures/Maps/T_UI_Map_Prison_Thumb0\", \"weather_da\": null, \"unknown_future_key\": {\"a\": [1, 2, {\"b\": \"\\u00e9\"}]}\n}\n");
                CardInfo c = CardInfo.Parse(text);
                Check(c.Id == "Prison" && c.Modes.Length == 3 && c.WeatherDa == null && c.FieldProblems().Count == 0, "parse");
                CardInfo back = CardInfo.Parse(PrisonCard("1.0.0").ToJson());
                Check(back.WeatherDa == PaintballWeather && back.Version == "1.0.0", "round trip");
                string noWeather = Encoding.UTF8.GetString(text).Replace("\"weather_da\": null, ", "");
                string m1 = null, m2 = null;
                try { CardInfo.Parse(Encoding.UTF8.GetBytes(noWeather)); } catch (PackageException ex) { m1 = ex.Message; }
                try { CardInfo.Parse(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(text).Replace("\"format\": 1", "\"format\": 2"))); } catch (PackageException ex) { m2 = ex.Message; }
                Check(m1 != null && m1.Contains("weather_da"), "missing weather_da accepted");
                Check(m2 == "needs a newer installer (package format 2)", "format 2: " + m2);
                return "unknown nested key ignored; weather_da null kept apart from absent; format 2 -> '" + m2 + "'";
            });
            Step("02.zip.entry_name_safety", delegate
            {
                string[] bad = { "../evil.pak", "paks/../../evil.pak", "C:/evil.pak", "/abs.pak", "paks\\x.pak", "menu/\u00e9.uasset", "./card.json" };
                string[] good = { "card.json", "paks/zPrison_9_P.pak", "menu/UI/MetaData/DA_Prison_TDM.uasset" };
                foreach (string b in bad) Check(!PackagePaths.IsSafeEntryName(b), "accepted " + b);
                foreach (string g in good) Check(PackagePaths.IsSafeEntryName(g), "refused " + g);
                return bad.Length + " unsafe names refused, " + good.Length + " package names accepted";
            });
            Step("02.zip.stored_entry_in_place", delegate
            {
                string z = Path.Combine(work, "stored_probe.zip");
                byte[] big = Dummy("stored", 300000);
                using (FileStream fs = new FileStream(z, FileMode.CreateNew))
                using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    using (Stream s = za.CreateEntry("a/stored.bin", CompressionLevel.NoCompression).Open()) s.Write(big, 0, big.Length);
                    using (Stream s = za.CreateEntry("b/deflated.bin", CompressionLevel.Optimal).Open()) s.Write(big, 0, big.Length);
                }
                using (ZipSource src = new ZipSource(z))
                {
                    foreach (string n in new string[] { "a/stored.bin", "b/deflated.bin" })
                    {
                        Func<Stream> open = src.SeekableOpener(n);
                        Check(open != null, "no opener " + n);
                        using (Stream s = open())
                        {
                            Check(s.CanSeek && s.Length == big.Length, "length " + n);
                            s.Seek(123456, SeekOrigin.Begin);
                            byte[] part = new byte[1000];
                            int got = 0; while (got < 1000) { int k = s.Read(part, got, 1000 - got); if (k <= 0) break; got += k; }
                            for (int i = 0; i < 1000; i++) Check(part[i] == big[123456 + i], "byte " + (123456 + i) + " of " + n);
                        }
                    }
                }
                return "a Stored entry is read in place through the central directory (seek 123456, 1000 bytes equal); a deflated one through memory";
            });
            Step("02.steam.vdf_fake_tree", delegate
            {
                string steam = Path.Combine(work, "fake steam (x86)");
                string lib = Path.Combine(work, "Lib (D) & co");
                Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
                string g = Path.Combine(lib, @"steamapps\common\Bodycam");
                Directory.CreateDirectory(Path.Combine(g, @"Bodycam\Binaries\Win64"));
                Directory.CreateDirectory(Path.Combine(g, @"Bodycam\Content\Paks"));
                Util.WriteNew(Path.Combine(g, @"Bodycam\Binaries\Win64\Bodycam-Win64-Shipping.exe"), new byte[0]);
                string vdf = "\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"" + steam.Replace("\\", "\\\\") + "\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"228980\"\t\t\"1\"\n\t\t}\n\t}\n" +
                             "\t\"1\"\n\t{\n\t\t\"path\"\t\t\"" + lib.Replace("\\", "\\\\") + "\"\n\t\t\"label\"\t\t\"\"\n\t\t\"apps\"\n\t\t{\n\t\t\t\"2406770\"\t\t\"59912047798\"\n\t\t}\n\t}\n}\n";
                Util.WriteNew(Path.Combine(steam, @"steamapps\libraryfolders.vdf"), Util.Utf8.GetBytes(vdf));
                Util.WriteNew(Path.Combine(lib, @"steamapps\appmanifest_2406770.acf"), Util.Utf8.GetBytes("\"AppState\"\n{\n\t\"appid\"\t\t\"2406770\"\n\t\"name\"\t\t\"Bodycam\"\n\t\"installdir\"\t\t\"Bodycam\"\n\t\"buildid\"\t\t\"25228199\"\n}\n"));
                string buildId;
                string found = SteamLocator.FindInSteamRoot(steam, out buildId);
                Check(found != null && string.Equals(Path.GetFullPath(found), Path.GetFullPath(g), StringComparison.OrdinalIgnoreCase), "found " + found);
                Check(buildId == "25228199", "buildid " + buildId);
                GameInstall manual = GameLocator.FromUserPath(Path.Combine(g, @"Bodycam\Content\Paks"));
                Check(manual != null && manual.BuildId == "25228199" && manual.Source == "Manual", "manual pick with appmanifest");
                return "found " + found + " buildid " + buildId + "; a folder picked by hand gets the same buildid";
            });
            Step("02.registry.steam_read_only", delegate
            {
                IList<string> roots = GameLocator.SteamRoots();
                return "Steam roots from the registry (read only): " + (roots.Count == 0 ? "(none)" : string.Join(" ; ", new List<string>(roots).ToArray()));
            });
            Step("02.process.lookup", delegate
            {
                return "Bodycam-Win64-Shipping running now: " + GameLocator.IsGameRunning() + " (the selftest itself uses a stub that says no)";
            });
            Step("02.bash.hidden_stream", BashHidden);
            Step("02.png_and_uuid5", delegate
            {
                int w, h;
                Check(Util.PngSize(Util.MakePng(512, 256, 1, 2, 3), out w, out h) && w == 512 && h == 256, "png");
                Check(Util.Uuid5UrlHex("bcmap/TowerTest/TDM/0") == "1B455DC985FF5C11BD41FFAFB09BA586" && Util.Uuid5UrlHex("bcmap/Prison/HP/1") == "02E5B458F54A5B93B6C7362F1E958031", "uuid5 vectors (python uuid.uuid5)");
                return "PNG 512x256 written and read without System.Drawing; uuid5 text keys equal python's";
            });

            Step("03.package.create", delegate
            {
                zip100 = MakeZip("Prison-1.0.0.zip", PrisonCard("1.0.0"), PrisonFiles(null, null));
                using (CardPackage p = CardPackage.Open(zip100))
                {
                    Check(p.Entries.Count == 12, "entries " + p.Entries.Count);
                    Check(p.Card.Id == "Prison" && p.Card.Version == "1.0.0", "card");
                }
                List<string> inner;
                Check(CardPackage.Classify(zip100, out inner) == DropKind.Package, "classify");
                return "Prison-1.0.0.zip: card.json, thumb.png 512x256, 2 dummy paks, 6 DA + 2 texture files of the fixtures (12 entries)";
            });

            Step("04.validate", delegate
            {
                ListLog l = new ListLog(null);
                using (CardPackage p = CardPackage.Open(zip100))
                {
                    ValidationReport r = p.Validate(NewInstaller(game, l, true).Context());
                    Check(r.Ok, "refused: " + string.Join(" | ", r.Problems.ToArray()));
                    Check(r.Notes.Count == 1 && r.Notes[0].Contains("Hardpoint"), "notes: " + string.Join(" | ", r.Notes.ToArray()));
                }
                StringWriter sw = new StringWriter();
                int code = CommandLine.Run(new string[] { "--validate", zip100 }, sw, delegate { return false; });
                Check(code == 0 && sw.ToString().Contains("is a valid map package"), "--validate exit " + code + ": " + sw);

                Dictionary<string, byte[]> evil = PrisonFiles(null, null);
                evil["../evil.pak"] = Dummy("evil", 100);
                string zEvil = MakeZip("Prison-evil.zip", PrisonCard("1.0.0"), evil);
                CardInfo rome = PrisonCard("1.0.0"); rome.Id = "RomeNight";
                string zRome = MakeZip("RomeNight-1.0.0.zip", rome, PrisonFiles(null, null));
                string zHijack = MakeZip("Prison-hijack.zip", PrisonCard("1.0.0"), PrisonFiles("GM_Maps/TeamDeathmatch/TDM_Paintball.umap", null));
                List<string> why = new List<string>();
                foreach (string z in new string[] { zEvil, zRome, zHijack })
                    using (CardPackage p = CardPackage.Open(z))
                    {
                        ValidationReport r = p.Validate(null);
                        Check(!r.Ok, Path.GetFileName(z) + " was accepted");
                        why.Add(Path.GetFileName(z) + ": " + r.Problems[0]);
                        if (z == zRome) Check(string.Join(" ", r.Problems.ToArray()).Contains("\"Rome\""), "RomeNight refused for another reason: " + string.Join(" | ", r.Problems.ToArray()));
                        if (z == zHijack) Check(string.Join(" ", r.Problems.ToArray()).Contains("TDM_Paintball"), "hijack refused for another reason");
                        if (z == zEvil) Check(string.Join(" ", r.Problems.ToArray()).Contains("../evil.pak"), "evil refused for another reason");
                    }
                StringWriter sw2 = new StringWriter();
                int code2 = CommandLine.Run(new string[] { "--validate", zHijack }, sw2, delegate { return false; });
                Check(code2 == 5, "--validate on the hijack exit " + code2);
                return "valid with 1 note (Hardpoint); --validate exit 0; refused: " + string.Join(" || ", why.ToArray()) + "; --validate exit 5";
            });

            Step("05.install", delegate
            {
                ListLog l = new ListLog(null);
                Installer inst = NewInstaller(game, l, true);
                OperationResult r = inst.InstallDropped(zip100);
                Detail(l);
                Check(r.ExitCode == 0 && r.Menu != null && r.Menu.Written, "exit " + r.ExitCode + " " + r.Message);
                string p = game.PaksDir;
                foreach (string f in new string[] { "zPrison_9_P.pak", "zPrison_Modes_9_P.pak", "zzMapCards_19_P.pak", @"_cards\Prison\card.json", @"_cards\Prison\thumb.png", @"_cards\Prison\installed.json",
                    @"_cards\Prison\menu\UI\MetaData\DA_Prison_HP.uasset", @"_cards\Prison\menu\UI\MetaData\DA_Prison_HP.uexp", @"_cards\Prison\menu\UI\MetaData\DA_Prison_TDM.uasset", @"_cards\Prison\menu\UI\MetaData\DA_Prison_TDM.uexp",
                    @"_cards\Prison\menu\UI\MetaData\DA_Prison_DM.uasset", @"_cards\Prison\menu\UI\MetaData\DA_Prison_DM.uexp",
                    @"_cards\Prison\menu\UI\Textures\Maps\T_UI_Map_Prison_Thumb0.uasset", @"_cards\Prison\menu\UI\Textures\Maps\T_UI_Map_Prison_Thumb0.uexp", @"_cards\_installer.json" })
                    Check(File.Exists(Path.Combine(p, f)), "missing " + f);
                MenuState st = MenuState.Load(game);
                Check(st.MenuPakSha1 == Util.Sha1File(game.MenuPakPath) && st.Check() == MenuState.Owner.Installer, "_installer.json menu sha1");
                menuShaAfterInstall[0] = st.MenuPakSha1;
                Check(Directory.GetFiles(p, "*.part", SearchOption.AllDirectories).Length == 0, "*.part left");
                Check(PaksIn(p, true).Count == 0, "*.pak under a subfolder: " + string.Join(",", PaksIn(p, true).ToArray()));
                Check(l.Has("GOOD", "Installed Prison 1.0.0. Start Bodycam and look in Play > Custom > Team Deathmatch."), "final line (Team Deathmatch is preferred over Hardpoint, the first mode listed)");
                Check(l.Has("DETAIL", "also offers Hardpoint") && !l.Has("WARN", "also offers Hardpoint"), "the Hardpoint note should be a detail line (the window shows it once in its own dialog)");
                Check(l.Has("DETAIL", "Copied zPrison_9_P.pak") && !l.Has("INFO", "Copied "), "Copied lines should be detail lines");
                Check(!Directory.Exists(game.BackupRoot) || BackupDirs(game).Count == 0, "a first install made a backup folder");
                return "exit 0; 15 files in place incl. _installer.json with the menu SHA1 " + st.MenuPakSha1.Substring(0, 12) + "; no *.part; no *.pak under a subfolder; no backup folder";
            });

            Step("06.oracle_menu_pak", delegate
            {
                PakReader r = PakReader.Open(game.MenuPakPath);
                Check(r.Entries.Count == 24, "entries " + r.Entries.Count);
                Dictionary<string, byte[]> files = ReadPakFiles(game.MenuPakPath);
                string tables = TablesEqualFixtures(files);
                int menu = 0;
                foreach (KeyValuePair<string, byte[]> kv in Fixtures())
                    if (kv.Key.StartsWith("UI/MetaData/") || kv.Key.StartsWith("UI/Textures/"))
                    {
                        Check(Util.BytesEqual(files[kv.Key], kv.Value), "menu file differs " + kv.Key);
                        menu++;
                    }
                Check(menu == 8, "menu files " + menu);

                BaseTables bt = BaseTables.Embedded();
                foreach (string stem in DataTableMerger.AllTables)
                {
                    IList<TableRow> got = DataTableMerger.ReadRows(new CookedTable(files[stem + ".uasset"], files[stem + ".uexp"]));
                    IList<TableRow> baseRows = DataTableMerger.ReadRows(bt.Table(stem));
                    int add = IsNewMode(stem) ? 0 : 1;   // 22 Sep: VS/GG/BB/WM tables are written untouched for a 3-mode card
                    Check(got.Count == baseRows.Count + add, stem + " rows " + got.Count);
                    for (int i = 0; i < baseRows.Count; i++) Check(Util.BytesEqual(got[i].Raw15, baseRows[i].Raw15), stem + " game row " + i + " changed");
                    if (add > 0) Check(got[got.Count - 1].Name == "Prison" && got[got.Count - 1].Active, stem + " last row " + got[got.Count - 1].Name);
                }
                return "16 entries; " + tables + "; 8 menu files byte-identical; row gate: every game row in place with its 15 bytes, then Prison";
            });

            Step("07.reader.pak_py", delegate { return PakPyReadBack(game.MenuPakPath); });
            Step("07.reader.unrealpak_verify", delegate { return UnrealPakVerify(game.MenuPakPath, 24); });

            Step("08.install_again", delegate
            {
                int before = BackupDirs(game).Count;
                ListLog l = new ListLog(null);
                OperationResult r = NewInstaller(game, l, true).InstallDropped(zip100);
                Detail(l);
                Check(r.ExitCode == 0 && r.NothingChanged, "exit " + r.ExitCode + " nothingChanged " + r.NothingChanged);
                Check(l.Contains("Prison 1.0.0 is already installed. Nothing changed."), "line");
                Check(BackupDirs(game).Count == before, "a backup folder appeared");
                Check(Util.Sha1File(game.MenuPakPath) == menuShaAfterInstall[0], "menu pak changed");
                return "\"Prison 1.0.0 is already installed. Nothing changed.\"; no new backup folder; menu pak unchanged";
            });

            Step("09.remove", delegate
            {
                int before = BackupDirs(game).Count;
                ListLog l = new ListLog(null);
                Installer inst = NewInstaller(game, l, true);
                OperationResult r = inst.Remove("Prison");
                Detail(l);
                Check(r.ExitCode == 0, "exit " + r.ExitCode);
                Check(BackupDirs(game).Count == before + 1, "backup folders " + BackupDirs(game).Count);
                string b = NewestBackup(game);
                foreach (string f in new string[] { "zPrison_9_P.pak.bak", "zPrison_Modes_9_P.pak.bak", "zzMapCards_19_P.pak.bak", @"_cards\Prison\card.json", "manifest.json", "RESTORE.txt" })
                    Check(File.Exists(Path.Combine(b, f)), "backup lacks " + f);
                JsonObject man = Json.ParseObject(File.ReadAllBytes(Path.Combine(b, "manifest.json")));
                List<object> moves = man["moves"] as List<object>;
                Check(moves != null && moves.Count == 4, "manifest moves " + (moves == null ? -1 : moves.Count));
                Check(inst.ListInstalled().Count == 0, "still listed");
                Check(PaksIn(game.PaksDir, false).Count == 0, "*.pak left: " + string.Join(",", PaksIn(game.PaksDir, false).ToArray()));
                Check(l.Has("GOOD", "Prison removed. A copy is in Backups.") && l.Has("DETAIL", "Backup folder: " + Path.GetFileName(b)), "line");
                Check(!l.Has("INFO", "Moved ") && l.Has("DETAIL", "Moved zPrison_9_P.pak to the backup folder"), "Moved lines should be detail lines");
                return "one backup folder " + Path.GetFileName(b) + " with manifest.json listing 4 moves (2 paks as .pak.bak, _cards/Prison, zzMapCards_19_P.pak.bak); nothing listed; no *.pak left in Paks";
            });

            Step("10.legacy_older_pak", delegate
            {
                string dest = Path.Combine(game.PaksDir, Path.GetFileName(legacy));
                File.Copy(legacy, dest, false);
                ListLog l = new ListLog(null);
                Installer inst = NewInstaller(game, l, true);
                IList<InstalledMap> list = inst.ListInstalled();
                Check(list.Count == 1 && list[0].Kind == MapKind.OlderPak && list[0].Id == "PanopticonPrison_P.pak", "listed as " + (list.Count > 0 ? list[0].Kind + " " + list[0].Id : "nothing"));
                IList<LegacyRow> rows = LegacyRows.Read(dest, BaseTables.Embedded(), l);
                Check(rows.Count == 4, "legacy rows " + rows.Count);
                MenuResult m = inst.RegenerateMenu(true);
                Detail(l);
                Check(m.Written && m.Entries == 16, "menu written " + m.Written + " entries " + m.Entries + " " + m.Why);
                string tables = TablesEqualFixtures(ReadPakFiles(game.MenuPakPath));
                return "PanopticonPrison_P.pak listed as older pak; 4 rows carried (HP, TDM, DM, weather); forced rebuild with zero cards: " + tables + " (BCMAP 5.3 oracle)";
            });

            Step("11.migration_replaces", delegate
            {
                zip101 = MakeZip("Prison-1.0.1.zip", PrisonCard("1.0.1"), PrisonFiles(null, null));
                CardInfo withReplaces = PrisonCard("1.0.1");
                withReplaces.Replaces = new string[] { "PanopticonPrison_P.pak" };
                zip101r = MakeZip("Prison-1.0.1-replaces.zip", withReplaces, PrisonFiles(null, null));
                ListLog l1 = new ListLog(null);
                OperationResult r1 = NewInstaller(game, l1, true).InstallDropped(zip101);
                Detail(l1);
                Check(r1.ExitCode == 5 && l1.Has("BLOCK", "uses the map name Prison, which Panopticon Prison already has. Remove Panopticon Prison first."), "without replaces: exit " + r1.ExitCode);
                Check(File.Exists(Path.Combine(game.PaksDir, "PanopticonPrison_P.pak")) && !File.Exists(Path.Combine(game.PaksDir, "zPrison_9_P.pak")), "the refusal changed files");
                ListLog l2 = new ListLog(null);
                OperationResult r2 = NewInstaller(game, l2, true).InstallDropped(zip101r);
                Detail(l2);
                Check(r2.ExitCode == 0, "with replaces: exit " + r2.ExitCode + " " + r2.Message);
                Check(l2.Contains("ASK Prison replaces an older file -> Replace"), "no Replace question");
                Check(!File.Exists(Path.Combine(game.PaksDir, "PanopticonPrison_P.pak")), "legacy pak still in Paks");
                Check(File.Exists(Path.Combine(NewestBackup(game), "PanopticonPrison_P.pak.bak")), "legacy pak not in the newest backup as .pak.bak");
                Dictionary<string, byte[]> files = ReadPakFiles(game.MenuPakPath);
                Check(files.Count == 24, "menu entries " + files.Count);
                return "1.0.1 without replaces refused (\"already has\", nothing moved); with replaces: installed, PanopticonPrison_P.pak.bak in " + Path.GetFileName(NewestBackup(game)) + ", " + TablesEqualFixtures(files);
            });

            Step("12.pause_resume", delegate
            {
                Dictionary<string, string> before = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in PaksIn(game.PaksDir, false)) before[f] = Util.Sha1File(Path.Combine(game.PaksDir, f));
                string menuSha = Util.Sha1File(game.MenuPakPath);
                ListLog l = new ListLog(null);
                Installer inst = NewInstaller(game, l, true);
                OperationResult p = inst.Pause();
                Check(p.ExitCode == 0, "pause exit " + p.ExitCode);
                Check(PaksIn(game.PaksDir, false).Count == 0, "*.pak while paused: " + string.Join(",", PaksIn(game.PaksDir, false).ToArray()));
                Check(Util.FilesRecursive(game.PausedDir, ".pak.off").Count == before.Count, "paused files " + Util.FilesRecursive(game.PausedDir, ".pak.off").Count);
                Check(MenuState.Load(game).Paused, "state not paused");
                OperationResult blocked = inst.InstallDropped(zip101r);
                Check(blocked.ExitCode == 5 && l.Contains(Installer.PausedLine), "install while paused exit " + blocked.ExitCode);
                OperationResult rs = inst.Resume();
                Detail(l);
                Check(rs.ExitCode == 0, "resume exit " + rs.ExitCode);
                Dictionary<string, string> after = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string f in PaksIn(game.PaksDir, false)) after[f] = Util.Sha1File(Path.Combine(game.PaksDir, f));
                Check(after.Count == before.Count, "files after resume " + after.Count);
                foreach (KeyValuePair<string, string> kv in before) Check(after.ContainsKey(kv.Key) && after[kv.Key] == kv.Value, "changed " + kv.Key);
                Check(Util.Sha1File(game.MenuPakPath) == menuSha && MenuState.Load(game).Check() == MenuState.Owner.Installer && !MenuState.Load(game).Paused, "menu sha after resume");
                return "pause: " + before.Count + " paks -> _paused/*.pak.off, no *.pak in Paks, install refused while paused; resume: same " + before.Count + " files with the same SHA1s, same menu SHA1 " + menuSha.Substring(0, 12);
            });

            Step("13.crash_recovery", delegate
            {
                string part = Path.Combine(game.PaksDir, "zzMapCards_19_P.pak.part");
                Util.WriteNew(part, Dummy("half written menu pak", 5000));
                string stagingX = Path.Combine(game.CardsDir, @".staging\X");
                Util.WriteNew(Path.Combine(stagingX, "card.json"), Encoding.ASCII.GetBytes("{}"));
                ListLog l = new ListLog(null);
                int n = NewInstaller(game, l, true).RecoverStaleFiles();
                Detail(l);
                Check(n == 2, "recovered " + n);
                Check(!File.Exists(part) && !Directory.Exists(stagingX), "stale files still in place");
                string b = NewestBackup(game);
                Check(File.Exists(Path.Combine(b, "zzMapCards_19_P.pak.part")) && File.Exists(Path.Combine(b, @"_cards\.staging\X\card.json")), "not in the backup " + b);
                Check(l.Contains("Found an unfinished step from last time"), "line");
                return "zzMapCards_19_P.pak.part and _cards/.staging/X moved to " + Path.GetFileName(b) + "; nothing else changed";
            });

            Step("14.foreign_map_list", delegate
            {
                string menu = game.MenuPakPath;
                string keep = Path.Combine(work, "menu_before_foreign_copy.pak");
                Util.MoveFile(menu, keep);
                byte[] b = File.ReadAllBytes(keep);
                b[b.Length / 2] ^= 0x01;
                Util.WriteNew(menu, b);
                string foreignSha = Util.Sha1File(menu);
                Check(MenuState.Load(game).Check() == MenuState.Owner.Foreign, "not Foreign");
                zip102 = MakeZip("Prison-1.0.2.zip", PrisonCard("1.0.2"), PrisonFiles(null, null));
                StringWriter no = new StringWriter();
                int c1 = CommandLine.Run(new string[] { "--game", root, "--install", zip102 }, no, delegate { return false; });
                Check(c1 == 5 && Util.Sha1File(menu) == foreignSha, "without --yes: exit " + c1 + ", map list changed " + (Util.Sha1File(menu) != foreignSha));
                StringWriter yes = new StringWriter();
                int c2 = CommandLine.Run(new string[] { "--game", root, "--install", zip102, "--yes" }, yes, delegate { return false; });
                if (report != null) foreach (string line in yes.ToString().Split('\n')) if (line.Trim().Length > 0) report.WriteLine("      | " + line.TrimEnd('\r'));
                Check(c2 == 0, "--install --yes exit " + c2 + ": " + yes);
                Check(yes.ToString().Contains("Replace the map list?"), "no map-list question");
                bool foundForeign = false;
                foreach (string f in Util.FilesRecursive(game.BackupRoot, ".pak.bak"))
                    if (Path.GetFileName(f).StartsWith("zzMapCards_19_P.pak") && Util.Sha1File(f) == foreignSha) foundForeign = true;
                Check(foundForeign, "the other tool's map list is not in the backup");
                Check(MenuState.Load(game).Check() == MenuState.Owner.Installer, "owner after install");
                string tables = TablesEqualFixtures(ReadPakFiles(menu));
                return "one byte changed -> Foreign; --install without --yes: exit 5, nothing changed; --install --yes: exit 0, the other map list backed up as .pak.bak, new menu owned by the installer, " + tables;
            });

            Step("15.nothing_deleted", delegate
            {
                int backups = BackupDirs(game).Count;
                int baks = Util.FilesRecursive(game.BackupRoot, ".bak").Count;
                return backups + " backup folders with " + baks + " .bak files kept; the fake folder stays at " + root;
            });

            Step("16.locked_game_running", delegate
            {
                string before = Snapshot(game.PaksDir);
                ListLog l = new ListLog(null);
                Installer inst = NewInstaller(game, l, true);
                inst.IsGameRunning = delegate { return true; };
                OperationResult a = inst.InstallDropped(zip101r);
                OperationResult b = inst.Remove("Prison");
                OperationResult c = inst.Pause();
                Detail(l);
                Check(a.ExitCode == 4 && b.ExitCode == 4 && c.ExitCode == 4, "exits " + a.ExitCode + "/" + b.ExitCode + "/" + c.ExitCode);
                Check(Snapshot(game.PaksDir) == before, "files changed while the game was running");
                Check(l.Contains("Bodycam is open. Close it and drop the file again."), "line");
                return "install, remove and pause answer exit 4 with the game running; Paks unchanged file by file";
            });

            Step("17.second_card_tower_test", delegate
            {
                CardInfo t = new CardInfo();
                t.Format = 1; t.Id = "TowerTest"; t.DisplayName = "Tower Test"; t.Description = "Custom map: the tower"; t.Version = "0.3.0"; t.Author = "Lovac";
                t.Modes = new string[] { "TDM", "DM" }; t.LevelPackage = "/Game/Sandbox/TowerTest/TowerTest_BP"; t.Thumbnail = "thumb.png";
                t.MenuTexture = "/Game/UI/Textures/Maps/" + PackagePaths.ThumbName("TowerTest"); t.WeatherDa = null;
                Check(PackagePaths.ThumbName("TowerTest") == "T_UI_Map_TowerTest0000", "thumb name policy");
                Dictionary<string, byte[]> f = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                f["thumb.png"] = Util.MakePng(256, 128, 0x30, 0x50, 0x40);
                f["paks/zTowerTest_9_P.pak"] = DummyPak("zTowerTest_9_P.pak", new string[] { "Sandbox/TowerTest/TowerTest_BP.umap", "Sandbox/TowerTest/TowerTest_BP.uexp" });
                f["paks/zTowerTest_Modes_9_P.pak"] = DummyPak("zTowerTest_Modes_9_P.pak", new string[] { "GM_Maps/TeamDeathmatch/TDM_TowerTest.umap", "GM_Maps/TeamDeathmatch/TDM_TowerTest.uexp", "GM_Maps/DeathMatch/DM_TowerTest.umap", "GM_Maps/DeathMatch/DM_TowerTest.uexp" });
                foreach (string mode in t.Modes)
                {
                    byte[][] da = DevAssets.DeriveDa(Fx("UI/MetaData/DA_Prison_" + mode + ".uasset"), Fx("UI/MetaData/DA_Prison_" + mode + ".uexp"), mode, "TowerTest", t.DisplayName, t.Description, t.MenuTexture);
                    f["menu/UI/MetaData/DA_TowerTest_" + mode + ".uasset"] = da[0];
                    f["menu/UI/MetaData/DA_TowerTest_" + mode + ".uexp"] = da[1];
                }
                f["menu/UI/Textures/Maps/T_UI_Map_TowerTest0000.uasset"] = DevAssets.RenameTextureSameLength(Fx("UI/Textures/Maps/T_UI_Map_Prison_Thumb0.uasset"), "T_UI_Map_Prison_Thumb0", "T_UI_Map_TowerTest0000");
                f["menu/UI/Textures/Maps/T_UI_Map_TowerTest0000.uexp"] = Fx("UI/Textures/Maps/T_UI_Map_Prison_Thumb0.uexp");
                zipTower = MakeZip("TowerTest-0.3.0.zip", t, f);
                ListLog l = new ListLog(null);
                Installer inst = NewInstaller(game, l, true);
                using (CardPackage p = CardPackage.Open(zipTower))
                {
                    ValidationReport v = p.Validate(inst.Context());
                    Check(v.Ok && v.Notes.Count == 0, "TowerTest validation: " + string.Join(" | ", v.Problems.ToArray()) + " notes " + v.Notes.Count);
                }
                OperationResult r = inst.InstallDropped(zipTower);
                Detail(l);
                Check(r.ExitCode == 0 && r.Menu != null && r.Menu.Written && r.Menu.Entries == 30 && r.Menu.Cards == 2, "install exit " + r.ExitCode + " entries " + (r.Menu == null ? -1 : r.Menu.Entries));
                Dictionary<string, byte[]> files = ReadPakFiles(game.MenuPakPath);
                BaseTables bt = BaseTables.Embedded();
                StringBuilder md5s = new StringBuilder();
                foreach (string stem in DataTableMerger.AllTables)
                {
                    CookedTable tab = new CookedTable(files[stem + ".uasset"], files[stem + ".uexp"]);
                    Check(DataTableMerger.Verify(tab).Count == 0, "verify " + stem);
                    IList<TableRow> rows = DataTableMerger.ReadRows(tab);
                    int baseCount = DataTableMerger.ReadRows(bt.Table(stem)).Count;
                    bool tower = stem == DataTableMerger.TeamDeathmatchTable || stem == DataTableMerger.DeathmatchTable;
                    int addRows = IsNewMode(stem) ? 0 : (tower ? 2 : 1);
                    Check(rows.Count == baseCount + addRows, stem + " rows " + rows.Count);
                    if (addRows > 0) Check(rows[baseCount].Name == "Prison", stem + " first custom row " + rows[baseCount].Name);
                    if (tower)
                    {
                        string mode = stem == DataTableMerger.TeamDeathmatchTable ? "TDM" : "DM";
                        Check(rows[baseCount + 1].Name == "TowerTest" && rows[baseCount + 1].ObjectPackage == "/Game/UI/MetaData/DA_TowerTest_" + mode, stem + " second custom row");
                        md5s.Append(" ").Append(stem.Substring(stem.LastIndexOf('/') + 1)).Append(" uasset ").Append(Util.Md5Hex(files[stem + ".uasset"])).Append(" uexp ").Append(Util.Md5Hex(files[stem + ".uexp"]));
                    }
                    else Check(Util.BytesEqual(files[stem + ".uasset"], Fx(stem + ".uasset")) && Util.BytesEqual(files[stem + ".uexp"], Fx(stem + ".uexp")), stem + " should still equal the fixture");
                }
                Say("      tables with Prison + TowerTest (compare with merge_proof chain B):" + md5s);
                string expectDir = Path.Combine(work, "expected_menu_22");
                foreach (KeyValuePair<string, byte[]> kv in files) Util.WriteNew(Path.Combine(expectDir, kv.Key.Replace('/', '\\')), kv.Value);
                string readers = PakPyOrSkipText(game.MenuPakPath, expectDir) + "; " + UnrealPakVerifyOrSkipText(game.MenuPakPath, 30);
                ListLog l2 = new ListLog(null);
                OperationResult rm = NewInstaller(game, l2, true).Remove("TowerTest");
                Detail(l2);
                Check(rm.ExitCode == 0, "remove TowerTest exit " + rm.ExitCode);
                Dictionary<string, byte[]> back = ReadPakFiles(game.MenuPakPath);
                Check(back.Count == 24, "entries after removing TowerTest " + back.Count);
                return "TowerTest 0.3.0 (TDM, DM, weather null, DAs derived with fixed uuid5 text keys, 22-character photo) validates and installs: 22 entries, TDM/DM rows = game rows + Prison + TowerTest, HP and weather unchanged; " + readers + "; removing TowerTest gives " + TablesEqualFixtures(back);
            });

            Step("18.base_check_with_game_paks", delegate
            {
                BaseTables bt = BaseTables.Embedded();
                GameInstall match = MakeFakeGame(Path.Combine(root, "game_with_paks"));
                GameInstall changed = MakeFakeGame(Path.Combine(root, "game_changed_map_list"));
                foreach (GameInstall g in new GameInstall[] { match, changed })
                {
                    List<KeyValuePair<string, byte[]>> chunk24 = new List<KeyValuePair<string, byte[]>>(), chunk26 = new List<KeyValuePair<string, byte[]>>();
                    foreach (string rel in BaseTables.RelPaths)
                    {
                        byte[] data = (byte[])bt.Get(rel).Clone();
                        if (g == changed && rel.EndsWith("DT_UI_TeamDeathmatchMaps.uexp")) data[20] ^= 0x01;
                        (rel.StartsWith("GM/") ? chunk26 : chunk24).Add(new KeyValuePair<string, byte[]>(rel, data));
                    }
                    chunk24.Add(new KeyValuePair<string, byte[]>("Maps/Trenches/Main_Trenches.umap", Dummy("game level", 900)));
                    PakWriter.Write(Path.Combine(g.PaksDir, "pakchunk24-Windows.pak"), PakWriter.ContentMount, chunk24);
                    PakWriter.Write(Path.Combine(g.PaksDir, "pakchunk26-Windows.pak"), PakWriter.ContentMount, chunk26);
                }
                BaseCheck m1 = bt.CheckAgainst(GamePakIndex.Load(match, null));
                BaseCheck m2 = bt.CheckAgainst(GamePakIndex.Load(changed, null));
                Check(m1.Result == BaseCheck.Status.Match, "match: " + m1.Result + " " + string.Join(";", m1.Details.ToArray()));
                Check(m2.Result == BaseCheck.Status.Mismatch && m2.Details.Count == 1 && m2.Details[0].Contains("DT_UI_TeamDeathmatchMaps.uexp"), "changed: " + m2.Result + " " + string.Join(";", m2.Details.ToArray()));
                Check(GamePakIndex.Load(match, null).Contains("maps/trenches/main_trenches.umap"), "game path lookup ignore-case");

                ListLog l1 = new ListLog(null);
                OperationResult ok = NewInstaller(match, l1, true).InstallDropped(zip100);
                Detail(l1);
                Check(ok.ExitCode == 0 && ok.Menu.Written && File.Exists(match.MenuPakPath), "install with matching game paks exit " + ok.ExitCode);
                Check(TablesEqualFixtures(ReadPakFiles(match.MenuPakPath)) != null, "tables");
                Check(PaksIn(match.PaksDir, false).Count == 5, "pak files " + PaksIn(match.PaksDir, false).Count);

                string zClash = MakeZip("Prison-clash-1.0.3.zip", PrisonCard("1.0.3"), PrisonFiles(null, "Maps/Trenches/Main_Trenches.umap"));
                ListLog l2 = new ListLog(null);
                OperationResult clash = NewInstaller(match, l2, true).InstallDropped(zClash);
                Detail(l2);
                Check(clash.ExitCode == 5 && l2.Contains("would replace files of Bodycam itself (Maps/Trenches/Main_Trenches.umap)"), "game-path clash exit " + clash.ExitCode);

                ListLog l3 = new ListLog(null);
                OperationResult waits = NewInstaller(changed, l3, true).InstallDropped(zip100);
                Detail(l3);
                Check(waits.ExitCode == 6, "changed map list exit " + waits.ExitCode);
                Check(File.Exists(Path.Combine(changed.PaksDir, "zPrison_9_P.pak")) && !File.Exists(changed.MenuPakPath), "content installed / no menu pak");
                Check(l3.Contains("Bodycam changed its map list since this installer was made. Prison is installed; its card waits for a newer installer."), "line");
                StringWriter cli = new StringWriter();
                int c = CommandLine.Run(new string[] { "--game", Path.Combine(root, "game_changed_map_list"), "--list" }, cli, delegate { return false; });
                Check(c == 0 && cli.ToString().Contains("Prison\t1.0.0\tHP,TDM,DM\tcard"), "--list: " + cli);
                return "fake pakchunk24/26: Match; one byte changed in DT_UI_TeamDeathmatchMaps.uexp: Mismatch naming that file; install on the matching game writes the fixture tables; a package holding a game path refused; on the changed game: exit 6, content installed, no menu pak; --list shows the card";
            });

            Step("19.other_mods_and_refused_paks", delegate
            {
                string other = Path.Combine(game.PaksDir, "zSomeWeaponMod_P.pak");
                Util.WriteNew(other, DummyPak("zSomeWeaponMod_P.pak", new string[] { "Weapons/Rifle/SK_Rifle.uasset", "Weapons/Rifle/SK_Rifle.uexp" }));
                ListLog l = new ListLog(null);
                Installer inst = NewInstaller(game, l, true);
                IList<InstalledMap> list = inst.ListInstalled();
                InstalledMap om = null;
                foreach (InstalledMap m in list) if (m.Id == "zSomeWeaponMod_P.pak") om = m;
                Check(om != null && om.Kind == MapKind.OtherMod, "weapon mod not listed as other mod");
                string menuSha = Util.Sha1File(game.MenuPakPath);
                MenuResult mr = inst.RegenerateMenu(false);
                Check(!mr.Written && Util.Sha1File(game.MenuPakPath) == menuSha, "an other mod changed the menu pak");

                string chunk = Path.Combine(work, "pakchunk99-Windows_P.pak");
                Util.WriteNew(chunk, DummyPak("pakchunk99-Windows_P.pak", new string[] { "GM_Maps/TeamDeathmatch/TDM_Paintball.umap" }));
                List<KeyValuePair<string, byte[]>> tablesOnly = new List<KeyValuePair<string, byte[]>>();
                foreach (string rel in BaseTables.RelPaths) tablesOnly.Add(new KeyValuePair<string, byte[]>(rel, Fx(rel)));
                string menuOnly = Path.Combine(work, "zzOtherToolCards_19_P.pak");
                PakWriter.Write(menuOnly, PakWriter.ContentMount, tablesOnly);
                string notMap = Path.Combine(work, "zSkins_P.pak");
                Util.WriteNew(notMap, DummyPak("zSkins_P.pak", new string[] { "Characters/Skin.uasset" }));
                OperationResult a = inst.InstallDropped(chunk), b = inst.InstallDropped(menuOnly), c = inst.InstallDropped(notMap);
                string textFile = Path.Combine(work, "notes.txt");
                Util.WriteNew(textFile, Encoding.ASCII.GetBytes("hello"));
                OperationResult d = inst.InstallDropped(textFile);
                Check(a.ExitCode == 5 && l.Contains("pakchunk99-Windows_P.pak has the name of a Bodycam game file. Not installed."), "pakchunk exit " + a.ExitCode);
                Check(b.ExitCode == 5 && l.Contains("zzOtherToolCards_19_P.pak brings only a map list and no map. Not installed."), "tables-only exit " + b.ExitCode);
                Check(c.ExitCode == 5 && l.Contains("zSkins_P.pak is not a Bodycam map."), "not a map exit " + c.ExitCode);
                Check(d.ExitCode == 5 && l.Has("BLOCK", "notes.txt" + Installer.NoMapInside), "text file exit " + d.ExitCode);
                OperationResult rm = inst.Remove("zSomeWeaponMod_P.pak");
                Detail(l);
                Check(rm.ExitCode == 0 && !File.Exists(other) && File.Exists(Path.Combine(NewestBackup(game), "zSomeWeaponMod_P.pak.bak")), "remove other mod");
                Check(Util.Sha1File(game.MenuPakPath) == menuSha, "removing an other mod changed the menu pak");
                return "a weapon mod pak is listed as other mod and never touches the menu; refused: pakchunk-named pak, tables-only pak, non-map pak, a text file; Remove moves the other mod to the backup";
            });

            Step("20.drop_forms", delegate
            {
                string folderZip = Path.Combine(work, "Prison-1.0.2-folder.zip");
                Dictionary<string, byte[]> files = PrisonFiles(null, null);
                using (FileStream fs = new FileStream(folderZip, FileMode.CreateNew))
                using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    using (Stream s = za.CreateEntry("Prison-1.0.2/card.json").Open()) { byte[] cj = PrisonCard("1.0.2").ToJson(); s.Write(cj, 0, cj.Length); }
                    foreach (KeyValuePair<string, byte[]> kv in files) using (Stream s = za.CreateEntry("Prison-1.0.2/" + kv.Key).Open()) s.Write(kv.Value, 0, kv.Value.Length);
                }
                using (CardPackage p = CardPackage.Open(folderZip)) Check(p.Validate(null).Ok, "one-folder zip refused");

                string folder = Path.Combine(work, "Prison-folder");
                Util.WriteNew(Path.Combine(folder, "card.json"), PrisonCard("1.0.2").ToJson());
                foreach (KeyValuePair<string, byte[]> kv in files) Util.WriteNew(Path.Combine(folder, kv.Key.Replace('/', '\\')), kv.Value);
                using (CardPackage p = CardPackage.Open(folder)) Check(p.Validate(null).Ok, "folder refused");

                string multi = Path.Combine(work, "two-maps.zip");
                using (FileStream fs = new FileStream(multi, FileMode.CreateNew))
                using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (string z in new string[] { zip102, zipTower })
                    {
                        byte[] zb = File.ReadAllBytes(z);
                        using (Stream s = za.CreateEntry(Path.GetFileNameWithoutExtension(z) + ".bcmap", CompressionLevel.NoCompression).Open()) s.Write(zb, 0, zb.Length);
                    }
                }
                List<string> inner;
                Check(CardPackage.Classify(multi, out inner) == DropKind.Packages && inner.Count == 2, "multi classify");
                IList<CardPackage> pk = CardPackage.OpenAll(multi);
                Check(pk.Count == 2 && pk[0].Card.Id == "Prison" && pk[1].Card.Id == "TowerTest", "OpenAll");
                foreach (CardPackage p in pk) { Check(p.Validate(null).Ok, p.SourceName + " inside the zip refused"); p.Dispose(); }

                string legacyZip = Path.Combine(work, "PanopticonPrison-1.0.0.zip");
                using (FileStream fs = new FileStream(legacyZip, FileMode.CreateNew))
                using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    byte[] lb = File.ReadAllBytes(legacy);
                    using (Stream s = za.CreateEntry("PanopticonPrison_P.pak", CompressionLevel.NoCompression).Open()) s.Write(lb, 0, lb.Length);
                    using (Stream s = za.CreateEntry("README.txt").Open()) s.Write(new byte[] { 0x68, 0x69 }, 0, 2);
                }
                Check(CardPackage.Classify(legacyZip, out inner) == DropKind.LegacyPaks && inner.Count == 1, "legacy zip classify");
                List<string> none;
                string proj = Path.Combine(work, "MyMaps.uproject");
                Util.WriteNew(proj, Encoding.ASCII.GetBytes("{}"));
                Check(CardPackage.Classify(proj, out none) == DropKind.Project, "uproject");
#if BCMI_DEV
                Check(!DevRunner.IsDevProject(proj), "a uproject without the build script counted as the dev project");
#endif
                return "accepted: a zip with one folder, a dropped folder, a zip of two .bcmap (Prison, TowerTest); classified: zip of an older pak + README as older paks, .uproject as project";
            });

            Step("21.legacy_zip_install_and_refusal_while_card", delegate
            {
                string legacyZip = Path.Combine(work, "PanopticonPrison-1.0.0.zip");
                ListLog l = new ListLog(null);
                string before = Snapshot(game.PaksDir);
                OperationResult r = NewInstaller(game, l, true).InstallDropped(legacyZip);
                Detail(l);
                Check(r.ExitCode == 5 && l.Contains("PanopticonPrison_P.pak changes wrappers that belong to Prison. Not installed."), "exit " + r.ExitCode);
                Check(Snapshot(game.PaksDir) == before, "files changed");

                GameInstall clean = MakeFakeGame(Path.Combine(root, "game_legacy_only"));
                ListLog l2 = new ListLog(null);
                OperationResult r2 = NewInstaller(clean, l2, true).InstallDropped(legacyZip);
                Detail(l2);
                Check(r2.ExitCode == 0 && File.Exists(Path.Combine(clean.PaksDir, "PanopticonPrison_P.pak")) && !File.Exists(clean.MenuPakPath), "legacy install exit " + r2.ExitCode);
                Check(Util.Sha1File(Path.Combine(clean.PaksDir, "PanopticonPrison_P.pak")) == Util.Sha1File(legacy), "legacy pak not copied unchanged");
                Check(l2.Has("GOOD", "Installed Panopticon Prison. Start Bodycam and look in Play > Custom > Team Deathmatch."), "line");
                Check(r2.Outcomes.Count == 1 && r2.Outcomes[0].Name == "Panopticon Prison" && r2.Outcomes[0].Kind == MapKind.OlderPak, "outcome");
                IList<InstalledMap> cleanList = NewInstaller(clean, new ListLog(null), true).ListInstalled();
                Check(cleanList.Count == 1 && cleanList[0].Label == "Panopticon Prison" && cleanList[0].Modes.Length == 3, "listed as " + (cleanList.Count > 0 ? cleanList[0].Label : "nothing"));
                return "with the Prison card installed the older pak is refused (wrappers belong to Prison), nothing changed; on a clean game the zip installs it unchanged, no menu pak (one older pak shows its own tables)";
            });

            Step("22.names_and_modes", delegate
            {
                string[][] names = {
                    new string[] { "PanopticonPrison_P.pak", "Panopticon Prison" }, new string[] { "zTowerTest_9_P.pak", "Tower Test" },
                    new string[] { "zzMapCards_19_P.pak", "Map Cards" }, new string[] { "CQBArena_P.pak", "CQB Arena" },
                    new string[] { "Airsoft1_P.pak", "Airsoft 1" }, new string[] { "weird.pak", "weird" }, new string[] { "my-map_v2_P.pak", "my map v 2" } };
                foreach (string[] t in names) Check(PackagePaths.FriendlyPakName(t[0]) == t[1], "FriendlyPakName(" + t[0] + ") = '" + PackagePaths.FriendlyPakName(t[0]) + "'");
                Check(PackagePaths.PreferredModeLongName(new string[] { "HP", "TDM", "DM" }) == "Team Deathmatch" && PackagePaths.PreferredModeLongName(new string[] { "HP", "DM" }) == "Deathmatch"
                      && PackagePaths.PreferredModeLongName(new string[] { "HP" }) == "Hardpoint" && PrisonCard("1.0.0").FirstModeLongName == "Team Deathmatch", "preferred mode");
                return names.Length + " file names read as map names (PanopticonPrison_P.pak -> Panopticon Prison); modes HP,TDM,DM -> Team Deathmatch, HP,DM -> Deathmatch, HP -> Hardpoint";
            });

            Step("23.drop_the_download", delegate
            {
                string top = "PanopticonPrison_v1.0.0";
                string dl = Path.Combine(work, top + "_download.zip");
                byte[] lb = File.ReadAllBytes(legacy);
                using (FileStream fs = new FileStream(dl, FileMode.CreateNew))
                using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
                {
                    foreach (string t in new string[] { "README.txt", "CHANGELOG.txt", "CREDITS.txt", "CHECKSUMS.txt" })
                        using (Stream s = za.CreateEntry(top + "/" + t).Open()) s.Write(new byte[] { 0x68, 0x69 }, 0, 2);
                    using (Stream s = za.CreateEntry(top + "/Installer/BodycamMapInstaller.exe").Open()) s.Write(Dummy("exe", 4000), 0, 4000);
                    using (Stream s = za.CreateEntry(top + "/Installer/BodycamMapInstaller.exe.config").Open()) s.Write(Dummy("config", 170), 0, 170);
                    using (Stream s = za.CreateEntry(top + "/Maps/PanopticonPrison_P.pak", CompressionLevel.NoCompression).Open()) s.Write(lb, 0, lb.Length);
                }
                List<string> inner;
                Check(CardPackage.Classify(dl, out inner) == DropKind.LegacyPaks && inner.Count == 1, "download zip classified " + CardPackage.Classify(dl, out inner));

                string unz = Path.Combine(work, @"unzipped download\" + top);
                foreach (string t in new string[] { "README.txt", "CHANGELOG.txt" }) Util.WriteNew(Path.Combine(unz, t), new byte[] { 0x68, 0x69 });
                Util.WriteNew(Path.Combine(unz, @"Installer\BodycamMapInstaller.exe"), Dummy("exe", 4000));
                Util.WriteNew(Path.Combine(unz, @"Maps\PanopticonPrison_P.pak"), lb);
                Check(CardPackage.Classify(unz, out inner) == DropKind.Folder && inner.Count == 1, "unzipped folder classified with " + inner.Count + " maps");
                Check(CardPackage.Classify(Path.Combine(unz, "Maps"), out inner) == DropKind.Folder && inner.Count == 1, "Maps folder");
                string emptyish = Path.Combine(work, "folder without maps");
                Util.WriteNew(Path.Combine(emptyish, @"docs\readme.txt"), new byte[] { 0x68 });
                Check(CardPackage.Classify(emptyish, out inner) == DropKind.NotAMap, "a folder without maps");
                string exeOnly = Path.Combine(work, "installer_only.zip");
                using (FileStream fs = new FileStream(exeOnly, FileMode.CreateNew))
                using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
                    using (Stream s = za.CreateEntry(top + "/Installer/BodycamMapInstaller.exe").Open()) s.Write(Dummy("exe", 100), 0, 100);
                string what = "";
                string[] drops = { dl, unz, Path.Combine(unz, "Maps") };
                for (int i = 0; i < drops.Length; i++)
                {
                    GameInstall g = MakeFakeGame(Path.Combine(root, "game_download_" + (i + 1)));
                    ListLog l = new ListLog(null);
                    OperationResult r = NewInstaller(g, l, true).InstallDropped(drops[i]);
                    Detail(l);
                    Check(r.ExitCode == 0 && l.Count("BLOCK") == 0, Path.GetFileName(drops[i]) + " exit " + r.ExitCode);
                    Check(Util.Sha1File(Path.Combine(g.PaksDir, "PanopticonPrison_P.pak")) == Util.Sha1File(legacy), "pak not installed unchanged from " + drops[i]);
                    Check(r.Outcomes.Count == 1 && r.Outcomes[0].Name == "Panopticon Prison", "outcome");
                    Check(l.Has("GOOD", "Installed Panopticon Prison. Start Bodycam and look in Play > Custom > Team Deathmatch."), "success line");
                    what += (i == 0 ? "" : ", ") + Path.GetFileName(drops[i]);
                }
                GameInstall g4 = MakeFakeGame(Path.Combine(root, "game_download_refused"));
                ListLog l4 = new ListLog(null);
                OperationResult r4 = NewInstaller(g4, l4, true).InstallDropped(exeOnly);
                OperationResult r5 = NewInstaller(g4, l4, true).InstallDropped(emptyish);
                Detail(l4);
                Check(r4.ExitCode == 5 && l4.Has("BLOCK", "installer_only.zip" + Installer.NoMapInside), "installer-only zip");
                Check(r5.ExitCode == 5 && l4.Has("BLOCK", "folder without maps" + Installer.NoMapInside), "folder without maps");
                return "installed from the download zip (README, Installer\\*.exe, Maps\\*.pak), the unzipped folder and its Maps folder: " + what + "; a zip with only the installer and a folder without maps get '" + Installer.NoMapInside.Trim() + "'";
            });

            Step("24.single_file_named_like_a_card_pak", delegate
            {
                GameInstall g = MakeFakeGame(Path.Combine(root, "game_name_clash"));
                ListLog l = new ListLog(null);
                Installer inst = NewInstaller(g, l, true);
                Check(inst.InstallDropped(zipTower).ExitCode == 0, "TowerTest install");
                string towerPak = Path.Combine(g.PaksDir, "zTowerTest_9_P.pak");
                string towerSha = Util.Sha1File(towerPak);
                string menuSha = Util.Sha1File(g.MenuPakPath);
                string impostor = Path.Combine(work, @"impostor\zTowerTest_9_P.pak");
                Util.WriteNew(impostor, File.ReadAllBytes(legacy));
                string before = Snapshot(g.PaksDir);
                OperationResult r = inst.InstallDropped(impostor);
                Check(r.ExitCode == 5 && l.Has("BLOCK", "zTowerTest_9_P.pak has the same file name as a file of Tower Test. Not installed"), "exit " + r.ExitCode);
                Check(Snapshot(g.PaksDir) == before && Util.Sha1File(towerPak) == towerSha && Util.Sha1File(g.MenuPakPath) == menuSha, "files changed");
                StringWriter cli = new StringWriter();
                Check(CommandLine.Run(new string[] { "--game", g.Root, "--list" }, cli, delegate { return false; }) == 0, "--list exit");
                bool towerLineClean = false;
                foreach (string line in cli.ToString().Replace("\r", "").Split('\n')) if (line.StartsWith("TowerTest\t0.3.0\tTDM,DM\tcard") && line.EndsWith("\t-")) towerLineClean = true;
                Check(towerLineClean, "--list does not show TowerTest as a clean card: " + cli);

                GameInstall g2 = MakeFakeGame(Path.Combine(root, "game_name_clash_loose"));
                Util.WriteNew(Path.Combine(g2.PaksDir, "zTowerTest_9_P.pak"), File.ReadAllBytes(legacy));
                ListLog l2 = new ListLog(null);
                string before2 = Snapshot(g2.PaksDir);
                OperationResult r2 = NewInstaller(g2, l2, true).InstallDropped(zipTower);
                Detail(l);
                Detail(l2);
                Check(r2.ExitCode == 5 && l2.Has("BLOCK", "Tower Test needs the file name zTowerTest_9_P.pak, which the single-file map Tower Test already uses. Remove it first."), "loose pak with the card's name: exit " + r2.ExitCode);
                Check(Snapshot(g2.PaksDir) == before2, "files changed on the second game");
                return "a single-file pak named zTowerTest_9_P.pak is refused while the Tower Test card owns that name (card pak and menu unchanged); a card whose pak name a loose pak already has is refused with 'Remove ... first'";
            });

            Step("25.refused_package_one_red_line", delegate
            {
                GameInstall g = MakeFakeGame(Path.Combine(root, "game_refused_package"));
                ListLog l = new ListLog(null);
                OperationResult r = NewInstaller(g, l, true).InstallDropped(Path.Combine(work, "Prison-hijack.zip"));
                Detail(l);
                Check(r.ExitCode == 5 && l.Count("BLOCK") == 1 && l.Has("BLOCK", "Prison-hijack.zip was not installed: the map is packed wrong. Nothing changed. Tell its author"), "red lines " + l.Count("BLOCK"));
                Check(l.Has("DETAIL", "TDM_Paintball"), "the author detail is not in the details");
                StringWriter sw = new StringWriter();
                Check(CommandLine.Run(new string[] { "--validate", Path.Combine(work, "Prison-hijack.zip") }, sw, delegate { return false; }) == 5 && sw.ToString().Contains("stopped: Prison: zPrison_Modes_9_P.pak holds GM_Maps/TeamDeathmatch/TDM_Paintball.umap"), "--validate keeps the full list: " + sw);
                return "a package packed wrong gives one red line for the player, the reasons as details; --validate still prints every reason";
            });

            Step("26.remove_after_base_mismatch", delegate
            {
                GameInstall g = MakeFakeGame(Path.Combine(root, "game_remove_base_changed"));
                WriteFakeGamePaks(g, false);
                ListLog l = new ListLog(null);
                Installer inst = NewInstaller(g, l, true);
                Check(inst.InstallDropped(zip100).ExitCode == 0 && inst.InstallDropped(zipTower).ExitCode == 0 && File.Exists(g.MenuPakPath), "two cards on a matching game");
                NoDanglingRows(g);

                Util.MoveFile(Path.Combine(g.PaksDir, "pakchunk24-Windows.pak"), Path.Combine(work, "game_remove_base_changed_pakchunk24_before.pak"));
                WriteFakeGamePaks(g, true);
                Check(BaseTables.Embedded().CheckAgainst(GamePakIndex.Load(g, null)).Result == BaseCheck.Status.Mismatch, "not a mismatch");
                ListLog l2 = new ListLog(null);
                OperationResult r = NewInstaller(g, l2, true).Remove("TowerTest");
                Detail(l2);
                Check(r.ExitCode == 6, "exit " + r.ExitCode);
                Check(!File.Exists(g.MenuPakPath) && !File.Exists(Path.Combine(g.PaksDir, "zTowerTest_Modes_9_P.pak")) && File.Exists(Path.Combine(g.PaksDir, "zPrison_Modes_9_P.pak")), "files after remove");
                string b = NewestBackup(g);
                Check(File.Exists(Path.Combine(b, "zzMapCards_19_P.pak.bak")) && File.Exists(Path.Combine(b, "zTowerTest_Modes_9_P.pak.bak")), "backup " + b);
                Check(l2.Has("WARN", "Tower Test removed. Bodycam changed its map list since this installer was made, so the other custom cards leave the menu") && !l2.Has("GOOD", "removed"), "no green line when the map list could not be rebuilt");
                string dangling = NoDanglingRows(g);
                return "Bodycam changed its map list after two cards were installed: Remove TowerTest exits 6, the old map list goes to the backup with TowerTest's paks (" + Path.GetFileName(b) + "), amber line, " + dangling;
            });

            Step("27.remove_with_other_tools_list_kept", delegate
            {
                GameInstall g = MakeFakeGame(Path.Combine(root, "game_remove_foreign"));
                ListLog l = new ListLog(null);
                Installer yes = NewInstaller(g, l, true);
                Check(yes.InstallDropped(zip100).ExitCode == 0 && yes.InstallDropped(zipTower).ExitCode == 0, "two cards");
                MenuState st = MenuState.Load(g);
                st.MenuPakSha1 = "0000000000000000000000000000000000000000";
                st.Save();
                Check(MenuState.Load(g).Check() == MenuState.Owner.Foreign, "not foreign");
                string before = Snapshot(g.PaksDir);
                string menuSha = Util.Sha1File(g.MenuPakPath);
                ListLog l2 = new ListLog(null);
                OperationResult no = NewInstaller(g, l2, false).Remove("TowerTest");
                Detail(l2);
                Check(no.ExitCode == 5 && l2.Has("BLOCK", "Tower Test was not removed") && !l2.Has("GOOD", "removed"), "declined: exit " + no.ExitCode);
                Check(File.Exists(Path.Combine(g.PaksDir, "zTowerTest_Modes_9_P.pak")) && Directory.Exists(Path.Combine(g.CardsDir, "TowerTest")) && Util.Sha1File(g.MenuPakPath) == menuSha, "files moved although the map list was kept");
                Check(Snapshot(g.PaksDir) == before, "the refused Remove changed files in Paks");
                string d1 = NoDanglingRows(g);
                ListLog l3 = new ListLog(null);
                OperationResult ok = NewInstaller(g, l3, true).Remove("TowerTest");
                Detail(l3);
                Check(ok.ExitCode == 0 && !File.Exists(Path.Combine(g.PaksDir, "zTowerTest_Modes_9_P.pak")) && MenuState.Load(g).Check() == MenuState.Owner.Installer, "accepted: exit " + ok.ExitCode);
                string d2 = NoDanglingRows(g);
                return "another tool's map list kept: Remove refused (exit 5), TowerTest files and the map list unchanged, " + d1 + "; replacing it: removed, " + d2;
            });

            Step("28.failed_step_puts_the_old_version_back", delegate
            {
                GameInstall g = MakeFakeGame(Path.Combine(root, "game_rollback"));
                ListLog l = new ListLog(null);
                Check(NewInstaller(g, l, true).InstallDropped(zip100).ExitCode == 0, "Prison 1.0.0");
                string menuSha = Util.Sha1File(g.MenuPakPath);
                string results = "";
                foreach (string point in new string[] { "install.moves", "install.menu" })
                {
                    ListLog lf = new ListLog(null);
                    Installer inst = NewInstaller(g, lf, true);
                    string p = point;
                    inst.TestFault = delegate(string at) { if (at == p) throw new IOException("selftest fault at " + at, unchecked((int)0x80070020)); };
                    OperationResult r = inst.InstallDropped(zip101);
                    Detail(lf);
                    Check(r.ExitCode == 5 && lf.Has("BLOCK", "Prison was not installed. A map file is in use (Steam or antivirus may be checking it). Wait a minute and try again. The previous version was put back."), point + ": exit " + r.ExitCode);
                    IList<InstalledMap> list = NewInstaller(g, new ListLog(null), true).ListInstalled();
                    Check(list.Count == 1 && list[0].Version == "1.0.0" && list[0].Problems.Count == 0, point + ": listed " + (list.Count > 0 ? list[0].Version + " " + string.Join(";", list[0].Problems.ToArray()) : "nothing"));
                    Check(Util.Sha1File(g.MenuPakPath) == menuSha && MenuState.Load(g).Check() == MenuState.Owner.Installer, point + ": map list changed");
                    Check(Directory.GetFiles(g.PaksDir, "*.part").Length == 0 && PaksIn(g.PaksDir, true).Count == 0 && !Directory.Exists(Path.Combine(g.CardsDir, @".staging\Prison")), point + ": leftovers");
                    NoDanglingRows(g);
                    results += (results.Length > 0 ? "; " : "") + point + ": 1.0.0 back";
                }

                ListLog ln = new ListLog(null);
                Installer fresh = NewInstaller(g, ln, true);
                fresh.TestFault = delegate(string at) { if (at == "install.moves") throw new IOException("selftest fault", unchecked((int)0x80070070)); };
                OperationResult rn = fresh.InstallDropped(zipTower);
                Detail(ln);
                Check(rn.ExitCode == 5 && ln.Has("BLOCK", "Tower Test was not installed. The disk with Bodycam is full. Nothing else changed.") && !File.Exists(Path.Combine(g.PaksDir, "zTowerTest_9_P.pak")) && !Directory.Exists(Path.Combine(g.CardsDir, "TowerTest")), "new map: exit " + rn.ExitCode);

                string one = Path.Combine(work, @"rollback\zLegacyTwo_P.pak");
                Util.WriteNew(one, DummyPak("zLegacyTwo_P.pak", new string[] { "GM_Maps/TeamDeathmatch/TDM_LegacyTwo.umap", "GM_Maps/TeamDeathmatch/TDM_LegacyTwo.uexp" }));
                string two = Path.Combine(work, @"rollback2\zLegacyTwo_P.pak");
                Util.WriteNew(two, DummyPak("zLegacyTwo_P.pak", new string[] { "GM_Maps/TeamDeathmatch/TDM_LegacyTwo.umap", "GM_Maps/TeamDeathmatch/TDM_LegacyTwo.uexp", "Sandbox/LegacyTwo/Extra.uasset" }));
                GameInstall g2 = MakeFakeGame(Path.Combine(root, "game_rollback_single"));
                Check(NewInstaller(g2, new ListLog(null), true).InstallDropped(one).ExitCode == 0, "first single-file install");
                string liveSha = Util.Sha1File(Path.Combine(g2.PaksDir, "zLegacyTwo_P.pak"));
                ListLog ls = new ListLog(null);
                Installer si = NewInstaller(g2, ls, true);
                si.TestFault = delegate(string at) { if (at == "legacy.moves") throw new IOException("selftest fault", unchecked((int)0x80070021)); };
                OperationResult rs = si.InstallDropped(two);
                Detail(ls);
                Check(rs.ExitCode == 5 && ls.Has("BLOCK", "Legacy Two was not installed.") && ls.Has("BLOCK", "The previous copy was put back.") && Util.Sha1File(Path.Combine(g2.PaksDir, "zLegacyTwo_P.pak")) == liveSha && Directory.GetFiles(g2.PaksDir, "*.part").Length == 0, "single file: exit " + rs.ExitCode);
                bool manifestSaysPutBack = false;
                foreach (string d in BackupDirs(g2)) { string mj = Path.Combine(g2.BackupRoot, d + @"\manifest.json"); if (File.Exists(mj) && Encoding.UTF8.GetString(File.ReadAllBytes(mj)).Contains("\"put_back\": true")) manifestSaysPutBack = true; }
                Check(manifestSaysPutBack, "the backup manifest does not record the put-back move");
                return results + "; a new map failing half way: nothing left in Paks; a single-file update failing half way: the previous copy back, manifest.json records put_back";
            });

            Step("29.menu_missing_rebuilt", delegate
            {
                GameInstall g = MakeFakeGame(Path.Combine(root, "game_menu_missing"));
                ListLog l = new ListLog(null);
                Installer inst = NewInstaller(g, l, true);
                Check(inst.InstallDropped(zipTower).ExitCode == 0, "install");
                Util.MoveFile(g.MenuPakPath, Path.Combine(work, "menu_missing_moved_by_test.pak"));
                Check(MenuState.Load(g).Check() == MenuState.Owner.None, "owner");
                MenuResult m = inst.RegenerateMenu(false);
                Check(m.Written && File.Exists(g.MenuPakPath) && MenuState.Load(g).Check() == MenuState.Owner.Installer, "rebuilt " + m.Written + " " + m.Why);
                return "zzMapCards_19_P.pak taken away with a card installed: RegenerateMenu writes it again (the window does this by itself at start, once)";
            });
        }

        void WriteFakeGamePaks(GameInstall g, bool changed)
        {
            BaseTables bt = BaseTables.Embedded();
            List<KeyValuePair<string, byte[]>> chunk24 = new List<KeyValuePair<string, byte[]>>(), chunk26 = new List<KeyValuePair<string, byte[]>>();
            foreach (string rel in BaseTables.RelPaths)
            {
                byte[] data = (byte[])bt.Get(rel).Clone();
                if (changed && rel.EndsWith("DT_UI_TeamDeathmatchMaps.uexp")) data[20] ^= 0x01;
                (rel.StartsWith("GM/") ? chunk26 : chunk24).Add(new KeyValuePair<string, byte[]>(rel, data));
            }
            chunk24.Add(new KeyValuePair<string, byte[]>("Maps/Trenches/Main_Trenches.umap", Dummy("game level", 900)));
            PakWriter.Write(Path.Combine(g.PaksDir, "pakchunk24-Windows.pak"), PakWriter.ContentMount, chunk24);
            if (!File.Exists(Path.Combine(g.PaksDir, "pakchunk26-Windows.pak"))) PakWriter.Write(Path.Combine(g.PaksDir, "pakchunk26-Windows.pak"), PakWriter.ContentMount, chunk26);
        }

        string NoDanglingRows(GameInstall g)
        {
            if (!File.Exists(g.MenuPakPath)) return "no map list pak, so no row can dangle";
            Dictionary<string, byte[]> files = ReadPakFiles(g.MenuPakPath);
            HashSet<string> held = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pak in Directory.GetFiles(g.PaksDir, "*.pak", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(pak), MenuPakBuilder.PakName, StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string p in PakReader.Open(pak, false).ContentPaths()) held.Add(p);
            }
            BaseTables bt = BaseTables.Embedded();
            int custom = 0;
            foreach (string mode in PackagePaths.AllModes)
            {
                string stem = DataTableMerger.TableFor(mode);
                HashSet<string> baseNames = bt.RowNames(stem);
                foreach (TableRow row in DataTableMerger.ReadRows(new CookedTable(files[stem + ".uasset"], files[stem + ".uexp"])))
                {
                    if (baseNames.Contains(row.Name)) continue;
                    custom++;
                    string w = PackagePaths.WrapperPath(row.Name, mode) + ".umap";
                    Check(held.Contains(w), "the " + mode + " row " + row.Name + " points at " + w + ", which no pak in Paks holds");
                }
            }
            return custom + " custom rows in the live map list, each with its wrapper in Paks";
        }

        List<KeyValuePair<string, byte[]>> FixtureList()
        {
            List<KeyValuePair<string, byte[]>> list = new List<KeyValuePair<string, byte[]>>(Fixtures());
            list.Sort(delegate(KeyValuePair<string, byte[]> a, KeyValuePair<string, byte[]> b) { return StringComparer.OrdinalIgnoreCase.Compare(a.Key, b.Key); });
            return list;
        }

        string Hashes()
        {
            Check(Util.Sha1Hex(Encoding.ASCII.GetBytes("abc")) == "a9993e364706816aba3e25717850c26c9cd0d89d", "sha1(abc)");
            string[][] crc = {
                new string[] { "zzmapcards_19_p.pak", "69D81802" }, new string[] { "panopticonprison_p.pak", "8F15FCDB" },
                new string[] { "Prison", "9AA68066" }, new string[] { "TowerTest", "8FBE6778" },
                new string[] { "/Game/UI/MetaData/DA_Prison_HP", "A4D6263A" } };
            foreach (string[] t in crc) Check(PakHash.StrCrc32(t[0]).ToString("X8") == t[1], "StrCrc32(" + t[0] + ")");
            string[][] ne = {
                new string[] { "Prison", "DF623507", "07356680" }, new string[] { "TowerTest", "CC1C28B0", "b0287867" },
                new string[] { "DA_Prison_HP", "123295CB", "cb95905f" }, new string[] { "/Game/UI/MetaData/DA_Prison_HP", "0FB2095C", "5c093a26" },
                new string[] { "DA_PaintballWeatherConfig", "0538EBEA", "eaeb44c2" } };
            foreach (string[] t in ne)
            {
                Check(PakHash.StrihashDeprecated(t[0]).ToString("X8") == t[1], "Strihash(" + t[0] + ")");
                Check(Util.Hex(CookedPackageHeader.NameHash(t[0])) == t[2].ToLowerInvariant(), "name entry tail(" + t[0] + ")");
            }
            object[][] fnv = {
                new object[] { 0x004477BEUL, "UI/MetaData/DA_Prison_HP.uasset", 0xCDA95C74CB1BC4F5UL },
                new object[] { 0x004477BEUL, "GM_Maps/Hardpoint/HP_Prison.umap", 0x6AD3D1287979979BUL },
                new object[] { 0x08BF4782UL, "MetaData/DA_Test_TDM.uasset", 0xD0247AC5B97548E0UL },
                new object[] { 0x00000000UL, "UI/Menus/Play/Cards/Data/DT_UI_HardpointMaps.uasset", 0x1A884A850E238EC6UL },
                new object[] { 0xFFFFFFFFUL, "Sandbox/TowerTest/TowerTest_BP.uexp", 0xD2823D8AC668EF02UL } };
            foreach (object[] t in fnv) Check(PakHash.PathHash((string)t[1], (ulong)t[0]) == (ulong)t[2], "PathHash(" + t[1] + ")");
            return "sha1(abc), 5 StrCrc32, 5 name-entry tails, 5 FNV-1a-64 path hashes (vectors of evidence/fnv_vectors.txt)";
        }

        string BashHidden()
        {
#if !BCMI_DEV
            throw new SkipStep("dev build only: the player build has no dev mode");
#else
            string bash = opt.BashExe ?? DevRunner.FindGitBash();
            if (bash == null || !File.Exists(bash)) throw new SkipStep("Git Bash not found");
            string script = Path.Combine(work, "stub oneclick (x86).sh");
            Util.WriteNew(script, Util.Utf8.GetBytes(
                "#!/bin/bash\n" +
                "echo \"stub: arg=[$1] MSYS_NO_PATHCONV=${MSYS_NO_PATHCONV:-unset} ONECLICK_DRY=${ONECLICK_DRY:-unset}\"\n" +
                "echo \"stub: acci\u00f3n con acento, \u00f1\"\n" +
                "for i in 1 2 3; do echo \"stub line $i\"; done\n" +
                "echo \"stub: to stderr\" 1>&2\n" +
                "( sleep 8; echo \"late grandchild line\" ) &\n" +
                "sleep 2\n" +
                "echo \"stub: exiting 98\"\n" +
                "exit 98\n"));
            List<string> lines = new List<string>();
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["ONECLICK_DRY"] = "1";
            Stopwatch sw = Stopwatch.StartNew();
            int code = DevRunner.Run(bash, script, @"C:\fake path (x86)\BodycamSandbox.uproject", work, env, delegate(string line) { lock (lines) lines.Add(line); }, null);
            long ms = sw.ElapsedMilliseconds;
            string all;
            lock (lines) all = string.Join(" | ", lines.ToArray());
            Check(code == 98, "exit code " + code + " output: " + all);
            Check(all.Contains("arg=[C:\\fake path (x86)\\BodycamSandbox.uproject]"), "argument not passed intact: " + all);
            Check(all.Contains("MSYS_NO_PATHCONV=1") && all.Contains("ONECLICK_DRY=1"), "environment not passed: " + all);
            Check(all.Contains("acci\u00f3n con acento, \u00f1"), "UTF-8 decoding: " + all);
            Check(all.Contains("stub: to stderr") && all.Contains("stub line 3") && all.Contains("stub: exiting 98"), "missing lines: " + all);
            Check(!all.Contains("late grandchild line"), "waited for the grandchild");
            Check(ms < 6000, "took " + ms + " ms");
            Check(DevRunner.Explain(98).StartsWith("Bodycam, the editor or another build is busy"), "Explain(98)");
            return "exit 98 in " + ms + " ms without waiting for a grandchild, " + lines.Count + " lines incl. stderr and UTF-8, MSYS_NO_PATHCONV and ONECLICK_DRY passed; bash " + bash;
#endif
        }

        static string FindPython(string given)
        {
            if (given != null) return File.Exists(given) ? given : null;
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string py = Path.Combine(win, "py.exe");
            if (File.Exists(py)) return py;
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string d in path.Split(';'))
            {
                try { string c = Path.Combine(d.Trim(), "py.exe"); if (File.Exists(c)) return c; } catch (Exception) { }
            }
            return null;
        }

        static int RunHidden(string exe, string args, int timeoutMs, out string output)
        {
            ProcessStartInfo psi = new ProcessStartInfo(exe, args);
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.RedirectStandardOutput = true; psi.RedirectStandardError = true; psi.RedirectStandardInput = true;
            psi.StandardOutputEncoding = Util.Utf8; psi.StandardErrorEncoding = Util.Utf8;
            StringBuilder sb = new StringBuilder();
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.Append(e.Data).Append('\n'); };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.Append(e.Data).Append('\n'); };
                p.Start();
                p.StandardInput.Close();
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch (Exception) { } lock (sb) output = sb.ToString(); return -1; }
                p.WaitForExit();
                lock (sb) output = sb.ToString();
                return p.ExitCode;
            }
        }

        const string PakPyScript =
            "import sys, os\n" +
            "sys.dont_write_bytecode = True\n" +
            "sys.path.insert(0, os.path.dirname(os.path.abspath(sys.argv[1])))\n" +
            "import pak\n" +
            "p = pak.Pak(sys.argv[2])\n" +
            "fx = sys.argv[3]\n" +
            "want = {}\n" +
            "for root, _, names in os.walk(fx):\n" +
            "    for n in names:\n" +
            "        full = os.path.join(root, n)\n" +
            "        want['Bodycam/Content/' + os.path.relpath(full, fx).replace(os.sep, '/')] = open(full, 'rb').read()\n" +
            "same = sum(1 for k in want if k in p.files and p.read(k) == want[k])\n" +
            "readable = sum(1 for k in p.files if len(p.read(k)) >= 0)\n" +
            "ok = same == len(want) and sorted(p.files) == sorted(want)\n" +
            "print('pak.py version %d mount %s files %d readable %d fixture files identical %d of %d' % (p.version, p.mount, len(p.files), readable, same, len(want)))\n" +
            "print('RESULT', 'PASS' if ok else 'FAIL')\n";

        string PakPyReadBack(string pak)
        {
            return PakPyCore(pak, fixtures);
        }

        string PakPyOrSkipText(string pak, string expectedDir)
        {
            try { return PakPyCore(pak, expectedDir); }
            catch (SkipStep s) { return "pak.py SKIP (" + s.Message + ")"; }
        }

        string PakPyCore(string pak, string expectedDir)
        {
            string py = FindPython(opt.Python);
            string pakpy = opt.PakPy ?? Environment.GetEnvironmentVariable("BCMI_PAKPY");
            if (py == null) throw new SkipStep("py.exe not found");
            if (pakpy == null || !File.Exists(pakpy)) throw new SkipStep("pak.py not given (--pakpy)");
            string script = Path.Combine(work, "readback_pakpy_" + DateTime.UtcNow.Ticks + ".py");
            Util.WriteNew(script, Encoding.ASCII.GetBytes(PakPyScript));
            string output;
            int code = RunHidden(py, "-3 -B " + Util.QuoteArg(script) + " " + Util.QuoteArg(pakpy) + " " + Util.QuoteArg(pak) + " " + Util.QuoteArg(expectedDir), 120000, out output);
            string summary = output.Replace("\r", "").Replace("\n", " | ").Trim();
            Check(code == 0 && output.Contains("RESULT PASS"), "pak.py exit " + code + ": " + summary);
            return "pak.py (" + pakpy + ", md5 " + Util.Md5Hex(File.ReadAllBytes(pakpy)).Substring(0, 8) + "): " + summary;
        }

        string UnrealPakVerify(string pak, int files)
        {
            string up = opt.UnrealPak ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Epic Games\UE_5.5\Engine\Binaries\Win64\UnrealPak.exe");
            if (!File.Exists(up)) throw new SkipStep("UnrealPak.exe not found");
            string output;
            int code = RunHidden(up, Util.QuoteArg(pak) + " -Verify", 180000, out output);
            string want = "healthy, " + files + " files checked";
            string line = "";
            foreach (string l in output.Split('\n')) if (l.Contains("healthy") || l.Contains("corrupt") || l.Contains("Error")) line += l.Trim() + " ";
            Check(code == 0 && output.Contains(want), "UnrealPak -Verify exit " + code + ": " + line);
            return "UnrealPak -Verify exit 0: " + line.Trim();
        }

        string UnrealPakVerifyOrSkipText(string pak, int files)
        {
            try { return UnrealPakVerify(pak, files); }
            catch (SkipStep s) { return "UnrealPak SKIP (" + s.Message + ")"; }
        }
    }

    public static class DevAssets
    {
        public static byte[][] DeriveDa(byte[] uasset, byte[] uexp, string mode, string newId, string display, string description, string menuTexture)
        {
            CookedPackageHeader h = CookedPackageHeader.Parse(uasset);
            string folder = PackagePaths.ModeFolder(mode), prefix = PackagePaths.ModePrefix(mode);
            string oldTexture = null;
            foreach (KeyValuePair<string, byte[]> n in h.Names) if (n.Key.StartsWith(PackagePaths.TexturePrefix, StringComparison.Ordinal)) oldTexture = n.Key;
            if (oldTexture == null) throw new InvalidDataException("the DA shows no texture");
            string oldPkg = h.PackageName;
            string oldObj = oldPkg.Substring(oldPkg.LastIndexOf('/') + 1);
            string oldId = oldObj.Substring(3, oldObj.Length - 4 - mode.Length);
            Dictionary<string, string> ren = new Dictionary<string, string>(StringComparer.Ordinal);
            ren[oldPkg] = PackagePaths.DaPackage(newId, mode);
            ren[oldObj] = PackagePaths.DaObject(newId, mode);
            ren["/Game/GM_Maps/" + folder + "/" + prefix + oldId + "?PrisonLobbyBypass"] = "/Game/GM_Maps/" + folder + "/" + prefix + newId + "?PrisonLobbyBypass";
            ren[oldTexture] = menuTexture;
            ren[oldTexture.Substring(oldTexture.LastIndexOf('/') + 1)] = menuTexture.Substring(menuTexture.LastIndexOf('/') + 1);
            int hits = 0;
            for (int i = 0; i < h.Names.Count; i++)
            {
                string nn;
                if (ren.TryGetValue(h.Names[i].Key, out nn)) { h.Names[i] = new KeyValuePair<string, byte[]>(nn, CookedPackageHeader.NameHash(nn)); hits++; }
            }
            if (hits != ren.Count) throw new InvalidDataException("renamed " + hits + " of " + ren.Count + " names");
            h.NewPackageName = PackagePaths.DaPackage(newId, mode);

            byte[] data = new byte[uexp.Length - 4];
            Buffer.BlockCopy(uexp, 0, data, 0, data.Length);
            int t0 = 4 + 8 + 20 + 20 + 4;
            if (BitConverter.ToInt32(data, t0 - 4) != 2) throw new InvalidDataException("expected 2 texts");
            int t2 = SkipText(data, SkipText(data, t0));
            MemoryStream ms = new MemoryStream();
            ms.Write(data, 0, t0);
            byte[] a = BaseText(display, Util.Uuid5UrlHex("bcmap/" + newId + "/" + mode + "/0"));
            byte[] b = BaseText(description, Util.Uuid5UrlHex("bcmap/" + newId + "/" + mode + "/1"));
            ms.Write(a, 0, a.Length);
            ms.Write(b, 0, b.Length);
            ms.Write(data, t2, data.Length - t2);
            byte[] newData = ms.ToArray();
            CookedPackageHeader.PutI64(h.Exports[0], 28, newData.Length);
            byte[] newUasset = h.Build(newData.Length);
            byte[] newUexp = new byte[newData.Length + 4];
            Buffer.BlockCopy(newData, 0, newUexp, 0, newData.Length);
            newUexp[newData.Length] = 0xC1; newUexp[newData.Length + 1] = 0x83; newUexp[newData.Length + 2] = 0x2A; newUexp[newData.Length + 3] = 0x9E;
            return new byte[][] { newUasset, newUexp };
        }

        static int SkipText(byte[] b, int o)
        {
            o += 4;
            sbyte ht = (sbyte)b[o]; o += 1;
            int end;
            if (ht == -1) { int has = BitConverter.ToInt32(b, o); o += 4; if (has != 0) { CookedPackageHeader.FStr(b, o, out end); o = end; } return o; }
            if (ht == 0) { for (int k = 0; k < 3; k++) { CookedPackageHeader.FStr(b, o, out end); o = end; } return o; }
            if (ht == 11) { o += 8; CookedPackageHeader.FStr(b, o, out end); return end; }
            throw new InvalidDataException("text history " + ht);
        }

        static byte[] BaseText(string src, string key)
        {
            MemoryStream ms = new MemoryStream();
            BinaryWriter w = new BinaryWriter(ms);
            w.Write(0u);
            w.Write((sbyte)0);
            foreach (string s in new string[] { "", key, src })
            {
                byte[] raw = Encoding.GetEncoding(28591).GetBytes(s);
                w.Write(raw.Length + 1); w.Write(raw); w.Write((byte)0);
            }
            return ms.ToArray();
        }

        public static byte[] RenameTextureSameLength(byte[] uasset, string oldName, string newName)
        {
            if (oldName.Length != newName.Length) throw new ArgumentException("names must have the same length");
            byte[] d = (byte[])uasset.Clone();
            CookedPackageHeader h = CookedPackageHeader.ParseLenient(d);
            int hits = 0;
            int o = (int)h["NameOffset"];
            for (int i = 0; i < h.Names.Count; i++)
            {
                int end;
                string s = CookedPackageHeader.FStr(d, o, out end);
                if (s.EndsWith("/" + oldName, StringComparison.Ordinal) || s == oldName)
                {
                    string ns = s.Substring(0, s.Length - oldName.Length) + newName;
                    Encoding.ASCII.GetBytes(ns, 0, ns.Length, d, o + 4);
                    Buffer.BlockCopy(CookedPackageHeader.NameHash(ns), 0, d, end, 4);
                    hits++;
                }
                o = end + 4;
            }
            int pkgEnd;
            string pkg = CookedPackageHeader.FStr(d, 32, out pkgEnd);
            if (!pkg.EndsWith("/" + oldName, StringComparison.Ordinal) || hits != 2) throw new InvalidDataException("texture rename hits " + hits + " package " + pkg);
            string newPkg = pkg.Substring(0, pkg.Length - oldName.Length) + newName;
            Encoding.ASCII.GetBytes(newPkg, 0, newPkg.Length, d, 36);
            if (CookedPackageHeader.ParseLenient(d).PackageName != newPkg) throw new InvalidDataException("texture package name not rewritten");
            return d;
        }
    }
}
