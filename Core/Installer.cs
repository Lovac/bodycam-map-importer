// Install, update, remove, hide and show maps, rebuild the menu pak, and recover after a crash.
using System;
using System.Collections.Generic;
using System.IO;

namespace BodycamMapInstaller.Core
{
    public enum MapKind { Card, OlderPak, OtherMod }

    public sealed class InstalledMap
    {
        public MapKind Kind;
        public string Id, DisplayName, Version, Author, Description, Source, CardDir, ThumbPng;
        public string[] Modes, PakFiles;
        public DateTime InstalledAtUtc;
        public List<string> Problems = new List<string>();
        public CardInfo Card;
        public List<string> ContentPaths = new List<string>();
        public bool Paused;

        public string Label { get { return Kind == MapKind.OtherMod ? Id : (DisplayName ?? Id); } }
    }

    public sealed class InstallContext
    {
        public GameInstall Game;
        public BaseTables Base;
        public BaseCheck BaseStatus;
        public MenuState Menu;
        public IList<InstalledMap> Installed;
        public GamePakIndex GamePaths;
        public IList<LegacyRow> LegacyRows;
        public Dictionary<string, InstalledMap> PathOwners = new Dictionary<string, InstalledMap>(StringComparer.OrdinalIgnoreCase);

        public InstalledMap OwnerOf(string relPath)
        {
            InstalledMap m;
            return PathOwners.TryGetValue(relPath, out m) ? m : null;
        }

        public InstalledMap FindCard(string id)
        {
            if (Installed == null || id == null) return null;
            foreach (InstalledMap m in Installed) if (m.Kind == MapKind.Card && string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase)) return m;
            return null;
        }

        public InstalledMap FindLoosePak(string fileName)
        {
            if (Installed == null) return null;
            foreach (InstalledMap m in Installed)
            {
                if (m.Kind != MapKind.Card && string.Equals(m.Id, fileName, StringComparison.OrdinalIgnoreCase)) return m;
                if (m.Kind == MapKind.Card && m.PakFiles != null)
                    foreach (string p in m.PakFiles) if (string.Equals(Path.GetFileName(p), fileName, StringComparison.OrdinalIgnoreCase)) return m;
                if (m.Kind == MapKind.Card && m.Id != null && (string.Equals(PackagePaths.ContentPakName(m.Id), fileName, StringComparison.OrdinalIgnoreCase) || string.Equals(PackagePaths.ModesPakName(m.Id), fileName, StringComparison.OrdinalIgnoreCase))) return m;
            }
            return null;
        }
    }

    public sealed class MapOutcome
    {
        public MapKind Kind;
        public string Id, Name, PlayMode;
        public bool AlreadyInstalled;
    }

    public sealed class OperationResult
    {
        public bool Ok;
        public bool NothingChanged;
        public int ExitCode;
        public string Message;
        public string BackupStamp;
        public MenuResult Menu;
        public List<MapOutcome> Outcomes = new List<MapOutcome>();
    }

    public sealed class MenuResult
    {
        public bool Written;
        public bool Skipped;
        public string Why;
        public int Cards;
        public int Entries;
        public string Sha1;
    }

    public delegate bool AskUser(string title, string body, string yes, string no);

    public sealed class Installer
    {
        public const int ExitOk = 0, ExitNoGame = 3, ExitRunning = 4, ExitRefused = 5, ExitMenuWaiting = 6;
        public const string WhyBaseChanged = "base tables changed";
        public const string WhyForeign = "another tool's map list";
        public const string WhyRunning = "game running";
        public const string WhyNotNeeded = "not needed";
        public const string WhyReadBack = "read-back";
        public const string WhyPaused = "paused";

        public const string PausedLine = "Custom maps are hidden. Show them again to install or remove maps.";

        readonly GameInstall game;
        readonly BaseTables baseTables;
        readonly ILog log;
        readonly AskUser ask;
        public Func<bool> IsGameRunning = GameLocator.IsGameRunning;

        public Action<string> TestFault;

        public Installer(GameInstall game, BaseTables baseTables, ILog log, AskUser ask)
        {
            this.game = game;
            this.baseTables = baseTables ?? BaseTables.Embedded();
            this.log = log ?? new NullLog();
            this.ask = ask ?? delegate { return false; };
        }

        public GameInstall Game { get { return game; } }

        static OperationResult Result(int code, string message)
        {
            OperationResult r = new OperationResult();
            r.ExitCode = code;
            r.Ok = code == ExitOk;
            r.Message = message;
            return r;
        }

        void Fault(string point)
        {
            if (TestFault != null) TestFault(point);
        }

        bool AcceptMissingGamePaks { get { return game.IsSelftestFake; } }

        static string WhyText(string why)
        {
            if (why == WhyRunning) return "Bodycam was started";
            if (why == WhyForeign) return "the map list another mod made was kept";
            if (why == WhyReadBack) return "the new map list did not read back correctly";
            if (why == WhyPaused) return "custom maps are hidden";
            return why;
        }

        public InstallContext Context()
        {
            InstallContext ctx = new InstallContext();
            ctx.Game = game;
            ctx.Base = baseTables;
            ctx.GamePaths = GamePakIndex.Load(game, log);
            ctx.BaseStatus = baseTables.CheckAgainst(ctx.GamePaths);
            ctx.Menu = MenuState.Load(game);
            ctx.Installed = ListInstalled();
            List<LegacyRow> rows = new List<LegacyRow>();
            foreach (InstalledMap m in ctx.Installed)
            {
                foreach (string p in m.ContentPaths) if (!ctx.PathOwners.ContainsKey(p)) ctx.PathOwners[p] = m;
                if (m.Kind == MapKind.OlderPak && !m.Paused && !m.Id.StartsWith("pakchunk", StringComparison.OrdinalIgnoreCase) && m.PakFiles != null && m.PakFiles.Length > 0)
                {
                    try { rows.AddRange(LegacyRows.Read(m.PakFiles[0], baseTables, null)); }
                    catch (Exception) { }
                }
            }
            ctx.LegacyRows = rows;
            return ctx;
        }

        static readonly Dictionary<string, KeyValuePair<string, List<string>>> pathCache = new Dictionary<string, KeyValuePair<string, List<string>>>(StringComparer.OrdinalIgnoreCase);

        static List<string> PakContentPaths(string pak)
        {
            FileInfo fi = new FileInfo(pak);
            string stamp = fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
            lock (pathCache)
            {
                KeyValuePair<string, List<string>> hit;
                if (pathCache.TryGetValue(fi.FullName, out hit) && hit.Key == stamp) return hit.Value;
            }
            List<string> paths = PakReader.Open(pak, false).ContentPaths();
            lock (pathCache) pathCache[fi.FullName] = new KeyValuePair<string, List<string>>(stamp, paths);
            return paths;
        }

        static string[] ModesIn(IEnumerable<string> contentPaths)
        {
            List<string> modes = new List<string>();
            foreach (string p in contentPaths)
                foreach (string mode in PackagePaths.AllModes)
                    if (p.StartsWith("GM_Maps/" + PackagePaths.ModeFolder(mode) + "/", StringComparison.OrdinalIgnoreCase) && !modes.Contains(mode)) modes.Add(mode);
            modes.Sort(delegate(string a, string b) { return Array.IndexOf(PackagePaths.AllModes, a).CompareTo(Array.IndexOf(PackagePaths.AllModes, b)); });
            return modes.ToArray();
        }

        public IList<InstalledMap> ListInstalled()
        {
            MenuState st = MenuState.Load(game);
            List<InstalledMap> cards = new List<InstalledMap>();
            HashSet<string> cardPaks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (Directory.Exists(game.CardsDir))
            {
                string[] dirs = Directory.GetDirectories(game.CardsDir);
                Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
                foreach (string dir in dirs)
                {
                    string dn = Path.GetFileName(dir);
                    if (dn.StartsWith(".") || dn.StartsWith("_")) continue;
                    InstalledMap m = new InstalledMap();
                    m.Kind = MapKind.Card;
                    m.Id = dn;
                    m.DisplayName = dn;
                    m.CardDir = dir;
                    m.Paused = st.Paused;
                    cards.Add(m);
                    string cj = Path.Combine(dir, "card.json");
                    if (!File.Exists(cj)) { m.Problems.Add("missing files: card.json"); continue; }
                    try { m.Card = CardInfo.Parse(File.ReadAllBytes(cj)); }
                    catch (PackageException) { m.Problems.Add("its card.json cannot be read"); continue; }
                    CardInfo c = m.Card;
                    m.Id = c.Id; m.DisplayName = c.DisplayName; m.Version = c.Version; m.Author = c.Author; m.Description = c.Description; m.Modes = c.Modes;
                    if (File.Exists(Path.Combine(dir, "thumb.png"))) m.ThumbPng = Path.Combine(dir, "thumb.png");
                    string ij = Path.Combine(dir, "installed.json");
                    if (File.Exists(ij))
                    {
                        try
                        {
                            JsonObject o = Json.ParseObject(File.ReadAllBytes(ij));
                            m.InstalledAtUtc = Util.ParseUtcStamp(o.Str("installed_at"));
                            m.Source = o.Str("source_file");
                        }
                        catch (Exception) { }
                    }
                    List<string> paks = new List<string>();
                    List<string> missing = new List<string>();
                    foreach (string pn in new string[] { PackagePaths.ContentPakName(c.Id), PackagePaths.ModesPakName(c.Id) })
                    {
                        cardPaks.Add(pn);
                        string live = Path.Combine(game.PaksDir, pn);
                        string paused = Path.Combine(game.PausedDir, pn + ".off");
                        if (File.Exists(live)) paks.Add(live);
                        else if (st.Paused && File.Exists(paused)) paks.Add(paused);
                        else missing.Add(pn);
                    }
                    m.PakFiles = paks.ToArray();
                    foreach (string e in ExpectedCardMenuFiles(c))
                        if (!File.Exists(Path.Combine(dir, "menu", e.Replace('/', '\\')))) missing.Add("menu/" + e);
                    if (missing.Count > 0) m.Problems.Add("missing files: " + string.Join(", ", missing.ToArray()));
                    foreach (string pak in paks)
                    {
                        try { m.ContentPaths.AddRange(PakContentPaths(pak)); }
                        catch (Exception) { m.Problems.Add("the installer cannot read " + Path.GetFileName(pak)); }
                    }
                    foreach (string e in ExpectedCardMenuFiles(c)) m.ContentPaths.Add(e);
                }
            }
            cards.Sort(delegate(InstalledMap a, InstalledMap b)
            {
                int k = a.InstalledAtUtc.CompareTo(b.InstalledAtUtc);
                return k != 0 ? k : StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id);
            });

            List<InstalledMap> loose = new List<InstalledMap>();
            if (Directory.Exists(game.PaksDir))
            {
                List<string> pakFiles = Util.FilesRecursive(game.PaksDir, ".pak");

                if (st.Paused) pakFiles.AddRange(Util.FilesRecursive(game.PausedDir, ".pak.off"));
                foreach (string f in pakFiles)
                {
                    string rel = Util.Relative(game.PaksDir, f);
                    bool pausedFile = rel.StartsWith("_paused/", StringComparison.OrdinalIgnoreCase);
                    string fileName = Path.GetFileName(f);
                    if (pausedFile) fileName = fileName.Substring(0, fileName.Length - 4);
                    bool atRoot = rel.IndexOf('/') < 0 || (pausedFile && rel.IndexOf('/', 8) < 0);
                    if (atRoot && Util.IsGamePakName(fileName)) continue;
                    if (atRoot && string.Equals(fileName, MenuPakBuilder.PakName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (atRoot && cardPaks.Contains(fileName)) continue;
                    InstalledMap m = new InstalledMap();
                    m.Id = fileName;
                    m.DisplayName = Path.GetFileNameWithoutExtension(fileName);
                    m.Source = pausedFile ? rel.Substring(8, rel.Length - 12) : rel;
                    m.Paused = pausedFile;
                    List<string> files = new List<string>();
                    files.Add(f);
                    if (!pausedFile)
                        foreach (string ext in new string[] { ".utoc", ".ucas", ".sig" })
                        {
                            string comp = Path.ChangeExtension(f, ext);
                            if (File.Exists(comp)) files.Add(comp);
                        }
                    m.PakFiles = files.ToArray();
                    m.InstalledAtUtc = File.GetLastWriteTimeUtc(f);
                    m.Kind = MapKind.OtherMod;
                    try
                    {
                        m.ContentPaths = PakContentPaths(f);
                        foreach (string p in m.ContentPaths)
                            if (p.StartsWith("GM_Maps/", StringComparison.OrdinalIgnoreCase) || (p.StartsWith("UI/MetaData/DA_", StringComparison.OrdinalIgnoreCase))) m.Kind = MapKind.OlderPak;
                        m.Modes = ModesIn(m.ContentPaths);
                        // 18 Sep 2026: an older all-in-one pak can carry a photo as a sidecar, _cards/_legacy/<pak name>.png
                        // (the card scan skips folders that start with "_", so it can never be mistaken for a card)
                        string side = Path.Combine(Path.Combine(game.CardsDir, "_legacy"), Path.GetFileNameWithoutExtension(fileName) + ".png");
                        if (m.Kind == MapKind.OlderPak && File.Exists(side)) m.ThumbPng = side;
                    }
                    catch (Exception) { m.Problems.Add("the installer cannot read this pak"); m.Modes = new string[0]; }
                    if (m.Kind == MapKind.OlderPak) m.DisplayName = PackagePaths.FriendlyPakName(fileName);
                    loose.Add(m);
                }
            }
            loose.Sort(delegate(InstalledMap a, InstalledMap b) { return StringComparer.OrdinalIgnoreCase.Compare(a.Id, b.Id); });
            List<InstalledMap> all = new List<InstalledMap>(cards);
            all.AddRange(loose);
            return all;
        }

        static List<string> ExpectedCardMenuFiles(CardInfo c)
        {
            List<string> list = new List<string>();
            if (c.Modes != null)
                foreach (string m in c.Modes)
                    if (PackagePaths.IsMode(m)) { list.Add(PackagePaths.DaPath(c.Id, m) + ".uasset"); list.Add(PackagePaths.DaPath(c.Id, m) + ".uexp"); }
            if (c.TextureRelPath != null) { list.Add(c.TextureRelPath + ".uasset"); list.Add(c.TextureRelPath + ".uexp"); }
            return list;
        }

        public const string NoMapInside = " has no map inside. Drop the .zip you downloaded, or the .pak in its Maps folder.";

        static OperationResult Merge(List<OperationResult> parts, string emptyMessage)
        {
            if (parts.Count == 0) return Result(ExitRefused, emptyMessage);
            OperationResult worst = parts[0];
            foreach (OperationResult p in parts) if (p.ExitCode > worst.ExitCode) worst = p;
            OperationResult m = Result(worst.ExitCode, worst.Message);
            m.NothingChanged = true;
            foreach (OperationResult p in parts)
            {
                if (!p.NothingChanged) m.NothingChanged = false;
                m.Outcomes.AddRange(p.Outcomes);
                if (p.Menu != null) m.Menu = p.Menu;
                if (p.BackupStamp != null) m.BackupStamp = p.BackupStamp;
            }
            return m;
        }

        public OperationResult InstallDropped(string path)
        {
            string name = Path.GetFileName(path.TrimEnd('\\', '/'));
            if (IsGameRunning()) { log.Block("Bodycam is open. Close it and drop the file again."); return Result(ExitRunning, "running"); }
            log.Info("Checking " + name + "...");
            List<string> inner;
            DropKind kind = CardPackage.Classify(path, out inner);
            switch (kind)
            {
                case DropKind.Package:
                    {
                        CardPackage pkg;
                        try { pkg = CardPackage.Open(path); }
                        catch (PackageException ex) { log.Block(ex.Message); return Result(ExitRefused, ex.Message); }
                        using (pkg) return Install(pkg);
                    }
                case DropKind.Packages:
                    {
                        log.Info(name + " holds " + inner.Count + " maps; installing them one by one.");
                        IList<CardPackage> pkgs;
                        try { pkgs = CardPackage.OpenAll(path); }
                        catch (PackageException ex) { log.Block(ex.Message); return Result(ExitRefused, ex.Message); }
                        List<OperationResult> results = new List<OperationResult>();
                        foreach (CardPackage p in pkgs)
                            using (p) results.Add(Install(p));
                        return Merge(results, "empty");
                    }
                case DropKind.LegacyPak:
                    return InstallLegacyPak(path);
                case DropKind.LegacyPaks:
                    {
                        List<OperationResult> results = new List<OperationResult>();
                        using (ZipSource z = new ZipSource(path))
                        {
                            inner.Sort(StringComparer.OrdinalIgnoreCase);
                            if (inner.Count > 1) log.Info(name + " holds " + inner.Count + " maps; installing them one by one.");
                            foreach (string entry in inner)
                            {
                                if (!PackagePaths.IsSafeEntryName(entry)) { log.Block(name + " has files the installer does not know. Ask its author for a newer version."); return Result(ExitRefused, "unsafe"); }
                                Func<Stream> opener = z.SeekableOpener(entry);
                                string e2 = entry;
                                OperationResult r = InstallLegacyCore(Path.GetFileName(entry), opener, delegate(Stream dest) { z.CopyTo(e2, dest); });
                                results.Add(r);
                                if (r.ExitCode == ExitRunning) break;
                            }
                        }
                        return Merge(results, "empty");
                    }
                case DropKind.Folder:
                    {
                        if (inner.Count > 1) log.Info(name + " holds " + inner.Count + " maps; installing them one by one.");
                        List<OperationResult> results = new List<OperationResult>();
                        foreach (string item in inner)
                        {
                            OperationResult r;
                            if (item.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && File.Exists(item)) r = InstallLegacyPak(item);
                            else
                            {
                                CardPackage pkg = null;
                                try { pkg = CardPackage.Open(item); }
                                catch (PackageException ex) { log.Block(ex.Message); r = Result(ExitRefused, ex.Message); }
                                if (pkg != null) { using (pkg) r = Install(pkg); }
                                else r = Result(ExitRefused, "not a package");
                            }
                            results.Add(r);
                            if (r.ExitCode == ExitRunning) break;
                        }
                        return Merge(results, "empty");
                    }
                case DropKind.Project:
                    log.Block(name + " is an Unreal project, not a map. Nothing changed.");
                    return Result(ExitRefused, "project");
                default:
                    log.Block(name + NoMapInside);
                    return Result(ExitRefused, "not a map");
            }
        }

        void ReportRefusal(string file, ValidationReport report)
        {
            string first = report.Conflicts.Count > 0
                ? report.Conflicts[0]
                : file + " was not installed: the map is packed wrong. Nothing changed. Tell its author (details: right-click Activity > Copy all).";
            log.Block(first);
            foreach (string p in report.Problems) if (p != first) log.Detail(p);
        }

        public OperationResult Install(CardPackage pkg)
        {
            CardInfo c = pkg.Card;
            string name = pkg.MapName;
            if (IsGameRunning()) { log.Block("Bodycam is open. Close it and drop the file again."); return Result(ExitRunning, "running"); }
            if (MenuState.Load(game).Paused) { log.Warn(PausedLine); return Result(ExitRefused, "paused"); }
            InstallContext ctx = Context();
            ValidationReport report = pkg.Validate(ctx);
            if (!report.Ok)
            {
                ReportRefusal(pkg.SourceName, report);
                return Result(ExitRefused, report.Problems[0]);
            }
            InstalledMap existing = ctx.FindCard(c.Id);
            if (existing != null)
            {
                if (existing.Version == c.Version && existing.Problems.Count == 0)
                {
                    log.Info(name + " " + c.Version + " is already installed. Nothing changed.");
                    OperationResult same = Result(ExitOk, "already installed");
                    same.NothingChanged = true;
                    same.Outcomes.Add(Outcome(MapKind.Card, c.Id, name, c.FirstModeLongName, true));
                    return same;
                }
                if (existing.Version != c.Version &&
                    !ask("Update " + name + "?", name + " " + existing.Version + " is installed. Replace it with " + c.Version + "? The old files go to the backup folder.", "Update", "Cancel"))
                {
                    log.Info("Nothing changed.");
                    return Result(ExitRefused, "update declined");
                }
            }
            List<InstalledMap> replaced = new List<InstalledMap>();
            if (c.Replaces != null)
                foreach (string rp in c.Replaces)
                {
                    InstalledMap m = ctx.FindLoosePak(rp);
                    if (m == null || m.Kind == MapKind.Card) continue;
                    if (!ask(name + " replaces an older file", name + " " + c.Version + " replaces " + m.Label + " (" + m.Id + "). Move the older file to the backup folder?", "Replace", "Cancel"))
                    {
                        log.Info("Nothing changed.");
                        return Result(ExitRefused, "replace declined");
                    }
                    replaced.Add(m);
                }
            bool foreignOk = false;
            if (ctx.Menu.Check() == MenuState.Owner.Foreign)
            {
                if (!AskForeign()) { log.Info("Nothing changed."); return Result(ExitRefused, "map list kept"); }
                foreignOk = true;
            }
            foreach (string n in report.Notes)
            {
                if (n.IndexOf("also offers Hardpoint", StringComparison.Ordinal) >= 0) log.Detail(n); else log.Warn(n);
            }
            try
            {
                DriveInfo drive = new DriveInfo(Path.GetPathRoot(game.PaksDir));
                long need = 2 * pkg.SourceSize;
                if (drive.IsReady && drive.AvailableFreeSpace < need)
                {
                    log.Block("There is not enough free space on " + drive.Name + " (" + (need / (1024 * 1024) + 1) + " MB needed).");
                    return Result(ExitRefused, "space");
                }
            }
            catch (ArgumentException) { }

            log.Info("Installing " + name + " " + c.Version + " (" + c.ModesText + ")...");
            Backup backup = Backup.Begin(game);
            backup.Log = log;
            OperationResult result;
            try { result = InstallCore(pkg, report, ctx, existing, replaced, backup, foreignOk); }
            finally { backup.Finish(); }
            result.BackupStamp = backup.Count > 0 ? backup.Stamp : null;
            return result;
        }

        static MapOutcome Outcome(MapKind kind, string id, string name, string playMode, bool already)
        {
            MapOutcome o = new MapOutcome();
            o.Kind = kind; o.Id = id; o.Name = name; o.PlayMode = playMode; o.AlreadyInstalled = already;
            return o;
        }

        static string Sentence(Exception ex, string fallback)
        {
            return Util.PlainReason(ex) ?? fallback;
        }

        void TryBackup(Backup backup, string path, string why)
        {
            try { if (File.Exists(path) || Directory.Exists(path)) backup.Move(path, why); }
            catch (Exception ex) { log.Detail("Could not move " + path + " to the backup folder (" + ex.Message + ")."); }
        }

        OperationResult InstallCore(CardPackage pkg, ValidationReport report, InstallContext ctx, InstalledMap existing, List<InstalledMap> replaced, Backup backup, bool foreignOk)
        {
            CardInfo c = pkg.Card;
            string name = pkg.MapName;
            string[] pakNames = { PackagePaths.ContentPakName(c.Id), PackagePaths.ModesPakName(c.Id) };
            int total = 6, step = 0;
            List<string> parts = new List<string>();
            string staging = Path.Combine(game.CardsDir, ".staging", c.Id);

            try
            {
                foreach (string pn in pakNames)
                {
                    log.Progress(++step, total, "Installing " + name + " " + c.Version + "...");
                    string part = Path.Combine(game.PaksDir, pn + ".part");
                    if (File.Exists(part)) backup.Move(part, "unfinished step from an earlier run");
                    using (FileStream fs = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
                    {
                        parts.Add(part);
                        pkg.CopyEntryTo("paks/" + pn, fs);
                        fs.Flush(true);
                    }
                    PakReader pr;
                    try { pr = PakReader.Open(part); }
                    catch (Exception)
                    {
                        return Abort(backup, parts, null, name + ": " + pn + " did not read back after copying. Nothing else changed.");
                    }
                    List<string> expected;
                    if (report.PakChecksDeferred && !report.PakPaths.ContainsKey(pn))
                    {
                        ValidationReport late = new ValidationReport();
                        pkg.CheckPak(pr, pn, pn == pakNames[1], late, ctx, null);
                        if (late.Problems.Count > 0)
                        {
                            foreach (string p in late.Problems) log.Detail(p);
                            return Abort(backup, parts, null, late.Conflicts.Count > 0 ? late.Conflicts[0] : pkg.SourceName + " was not installed: the map is packed wrong. Nothing else changed. Tell its author (details: right-click Activity > Copy all).");
                        }
                        expected = late.PakPaths[pn];
                    }
                    else expected = report.PakPaths[pn];
                    List<string> got = pr.ContentPaths();
                    if (got.Count != expected.Count || !new HashSet<string>(got, StringComparer.OrdinalIgnoreCase).SetEquals(expected))
                        return Abort(backup, parts, null, name + ": " + pn + " changed while copying. Nothing else changed.");
                }

                log.Progress(++step, total, "Installing " + name + " " + c.Version + "...");
                if (Directory.Exists(staging)) backup.Move(staging, "unfinished step from an earlier run");
                Directory.CreateDirectory(staging);
                Util.WriteNew(Path.Combine(staging, "card.json"), pkg.CardJsonBytes);
                Util.WriteNew(Path.Combine(staging, "thumb.png"), pkg.Read("thumb.png"));
                foreach (string e in pkg.ExpectedMenuEntries(true))
                    if (pkg.Has(e)) Util.WriteNew(Path.Combine(staging, e.Replace('/', '\\')), pkg.Read(e));
                DateTime installedAt = DateTime.UtcNow;
                if (existing != null && existing.InstalledAtUtc != DateTime.MinValue) installedAt = existing.InstalledAtUtc;
                JsonOut paksSha = new JsonOut();
                foreach (string part in parts) paksSha.Set(Path.GetFileName(part).Replace(".part", ""), Util.Sha1File(part));
                JsonOut inst = new JsonOut();
                inst.Set("format", 1).Set("installed_at", Util.UtcStamp(installedAt)).Set("updated_at", Util.UtcStamp(DateTime.UtcNow))
                    .Set("source_file", pkg.SourceName).Set("source_sha256", pkg.SourceSha256).Set("paks", paksSha)
                    .Set("installer_version", Util.InstallerVersion);
                Util.WriteNew(Path.Combine(staging, "installed.json"), Json.ToBytes(inst));
            }
            catch (Exception ex)
            {
                log.Detail(ex.ToString());
                return Abort(backup, parts, staging, name + " was not installed. " + Sentence(ex, "The installer could not copy its files.") + " Nothing else changed.");
            }

            if (IsGameRunning()) return Abort(backup, parts, staging, "Bodycam was started. Close it and drop the file again.", ExitRunning);

            log.Progress(++step, total, "Installing " + name + " " + c.Version + "...");
            int mark = backup.Mark;
            string cardDir = Path.Combine(game.CardsDir, c.Id);
            List<string> movedIn = new List<string>();
            bool cardMovedIn = false;
            try
            {
                foreach (string pn in pakNames)
                {
                    string live = Path.Combine(game.PaksDir, pn);
                    if (File.Exists(live)) backup.Move(live, existing != null ? "older version of " + name : "same file name as " + name);
                }
                if (existing != null && existing.CardDir != null && Directory.Exists(existing.CardDir)) backup.Move(existing.CardDir, "older version of " + name);
                if (Directory.Exists(cardDir)) backup.Move(cardDir, "older version of " + name);
                foreach (InstalledMap m in replaced)
                    foreach (string f in m.PakFiles)
                        if (File.Exists(f)) backup.Move(f, "replaced by " + name + " " + c.Version);
                Fault("install.moves");
                for (int i = 0; i < pakNames.Length; i++)
                {
                    string live = Path.Combine(game.PaksDir, pakNames[i]);
                    Util.MoveFile(parts[i], live);
                    movedIn.Add(live);
                    log.Detail("Copied " + pakNames[i]);
                }
                Util.MoveDirectory(staging, cardDir);
                cardMovedIn = true;
            }
            catch (Exception ex)
            {
                log.Detail(ex.ToString());
                return RollBack(backup, mark, movedIn, parts, cardMovedIn ? cardDir : null, staging, null, name, ex);
            }

            log.Progress(++step, total, "Updating the map list...");
            MenuPlan plan = null;
            MenuResult menu;
            try
            {
                Fault("install.menu");
                plan = PrepareMenu(false, backup, foreignOk, null);
                menu = CommitMenu(plan, backup);
            }
            catch (Exception ex)
            {
                log.Detail(ex.ToString());
                bool swapped = plan != null && plan.Result.Sha1 != null && File.Exists(game.MenuPakPath) && string.Equals(Util.Sha1File(game.MenuPakPath), plan.Result.Sha1, StringComparison.OrdinalIgnoreCase);
                if (!swapped) return RollBack(backup, mark, movedIn, parts, cardDir, staging, plan != null ? plan.Part : null, name, ex);
                log.Warn(name + " is installed and its card is in the map list, but the installer could not save its own notes (" + Sentence(ex, "a file step did not finish.") + ") Open the installer again before the next change.");
                OperationResult w = Result(ExitMenuWaiting, "state not saved");
                w.Outcomes.Add(Outcome(MapKind.Card, c.Id, name, c.FirstModeLongName, false));
                return w;
            }
            OperationResult r;
            if (menu.Skipped && menu.Why == WhyBaseChanged)
            {
                log.Warn("Bodycam changed its map list since this installer was made. " + name + " is installed; its card waits for a newer installer.");
                r = Result(ExitMenuWaiting, "menu waits");
            }
            else if (menu.Skipped && menu.Why != WhyNotNeeded)
            {
                log.Warn(name + " is installed, but the map list was not updated (" + WhyText(menu.Why) + ").");
                r = Result(ExitMenuWaiting, "menu " + menu.Why);
            }
            else
            {
                log.Good("Installed " + name + " " + c.Version + ". Start Bodycam and look in Play > Custom > " + c.FirstModeLongName + ".");
                r = Result(ExitOk, "installed");
            }
            log.Progress(++step, total, "");
            r.Menu = menu;
            r.Outcomes.Add(Outcome(MapKind.Card, c.Id, name, c.FirstModeLongName, false));
            return r;
        }

        OperationResult RollBack(Backup backup, int mark, List<string> movedIn, List<string> parts, string cardDirIn, string staging, string menuPart, string name, Exception ex)
        {
            int outMark = backup.Mark;
            if (menuPart != null) TryBackup(backup, menuPart, "not installed: the map list step did not finish");
            foreach (string f in movedIn) TryBackup(backup, f, "not installed: a step did not finish");
            foreach (string p in parts) TryBackup(backup, p, "not installed: a step did not finish");
            if (cardDirIn != null) TryBackup(backup, cardDirIn, "not installed: a step did not finish");
            TryBackup(backup, staging, "not installed: a step did not finish");
            int back = backup.UndoRange(mark, outMark);
            string line = name + " was not installed. " + Sentence(ex, "A file step did not finish.") + (back > 0 ? " The previous version was put back." : " Nothing else changed.");
            log.Block(line);
            return Result(ExitRefused, line);
        }

        OperationResult Abort(Backup backup, List<string> parts, string staging, string line)
        {
            return Abort(backup, parts, staging, line, ExitRefused);
        }

        OperationResult Abort(Backup backup, List<string> parts, string staging, string line, int code)
        {
            foreach (string p in parts) TryBackup(backup, p, "not installed");
            if (staging != null) TryBackup(backup, staging, "not installed");
            log.Block(line);
            return Result(code, line);
        }

        bool AskForeign()
        {
            return ask("Replace the map list?",
                "Another mod already changed the game's map list. Installing here replaces it; cards that only that mod knows leave the menu until it rebuilds them. The old file goes to the backup folder.",
                "Replace", "Cancel");
        }

        public OperationResult InstallLegacyPak(string pakPath)
        {
            if (IsGameRunning()) { log.Block("Bodycam is open. Close it and drop the file again."); return Result(ExitRunning, "running"); }
            string full = Path.GetFullPath(pakPath);
            return InstallLegacyCore(Path.GetFileName(full),
                delegate { return new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16); },
                delegate(Stream dest) { using (FileStream fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16)) fs.CopyTo(dest, 1 << 20); });
        }

        OperationResult InstallLegacyCore(string fileName, Func<Stream> seekable, Action<Stream> copyTo)
        {
            if (IsGameRunning()) { log.Block("Bodycam is open. Close it and drop the file again."); return Result(ExitRunning, "running"); }
            if (MenuState.Load(game).Paused) { log.Warn(PausedLine); return Result(ExitRefused, "paused"); }
            if (!PackagePaths.IsSafeFileName(fileName) || !fileName.EndsWith(".pak", StringComparison.OrdinalIgnoreCase)) { log.Block(fileName + " is not a Bodycam map."); return Result(ExitRefused, "name"); }
            if (fileName.StartsWith("pakchunk", StringComparison.OrdinalIgnoreCase)) { log.Block(fileName + " has the name of a Bodycam game file. Not installed."); return Result(ExitRefused, "pakchunk"); }
            if (string.Equals(fileName, MenuPakBuilder.PakName, StringComparison.OrdinalIgnoreCase)) { log.Block(fileName + " brings only a map list and no map. Not installed."); return Result(ExitRefused, "menu pak"); }
            if (seekable == null) { log.Block(fileName + " is too large to check inside the zip. Unzip it and drop the .pak itself."); return Result(ExitRefused, "too large"); }
            string friendly = PackagePaths.FriendlyPakName(fileName);
            PakReader pr;
            try { pr = PakReader.Open(seekable, true); }
            catch (Exception) { log.Block(fileName + " is not a Bodycam map."); return Result(ExitRefused, "not a pak"); }
            LegacyRows.Scan scan = LegacyRows.ScanPak(pr, fileName, baseTables, null);
            if (scan.HasTables && !scan.HasOwnDa) { log.Block(fileName + " brings only a map list and no map. Not installed."); return Result(ExitRefused, "tables only"); }
            if (!scan.HasWrappers && !scan.HasOwnDa) { log.Block(fileName + " is not a Bodycam map."); return Result(ExitRefused, "not a map"); }
            InstallContext ctx = Context();

            InstalledMap nameOwner = ctx.FindLoosePak(fileName);
            if (nameOwner != null && nameOwner.Kind == MapKind.Card)
            {
                log.Block(fileName + " has the same file name as a file of " + nameOwner.Label + ". Not installed; nothing changed.");
                return Result(ExitRefused, "card pak name");
            }
            foreach (string p in scan.ContentPaths)
            {
                InstalledMap owner = ctx.OwnerOf(p);
                if (owner == null || string.Equals(owner.Id, fileName, StringComparison.OrdinalIgnoreCase)) continue;
                if (p.StartsWith("GM_Maps/", StringComparison.OrdinalIgnoreCase) && owner.Kind == MapKind.Card)
                {
                    log.Block(fileName + " changes wrappers that belong to " + owner.Label + ". Not installed.");
                    return Result(ExitRefused, "wrappers");
                }
            }
            foreach (LegacyRow lr in scan.Rows)
            {
                InstalledMap card = ctx.FindCard(lr.Row.Name);
                if (card != null)
                {
                    log.Block(fileName + " uses the map name " + lr.Row.Name + ", which " + card.Label + " already has. Remove " + card.Label + " first.");
                    return Result(ExitRefused, "row name");
                }
            }
            string playMode = PackagePaths.PreferredModeLongName(ModesIn(scan.ContentPaths));
            string live = Path.Combine(game.PaksDir, fileName);
            if (File.Exists(live))
            {
                string liveSha = Util.Sha1File(live);
                string newSha;
                using (Stream s = seekable())
                using (System.Security.Cryptography.SHA1 h = System.Security.Cryptography.SHA1.Create()) newSha = Util.Hex(h.ComputeHash(s));
                if (liveSha == newSha)
                {
                    log.Info(friendly + " is already installed. Nothing changed.");
                    OperationResult same = Result(ExitOk, "already installed");
                    same.NothingChanged = true;
                    same.Outcomes.Add(Outcome(MapKind.OlderPak, fileName, friendly, playMode, true));
                    return same;
                }
            }
            bool foreignOk = false;
            if (ctx.Menu.Check() == MenuState.Owner.Foreign)
            {
                if (!AskForeign()) { log.Info("Nothing changed."); return Result(ExitRefused, "map list kept"); }
                foreignOk = true;
            }
            Backup backup = Backup.Begin(game);
            backup.Log = log;
            OperationResult r;
            try
            {
                string part = live + ".part";
                PakReader back;
                try
                {
                    if (File.Exists(part)) backup.Move(part, "unfinished step from an earlier run");
                    using (FileStream fs = new FileStream(part, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20))
                    {
                        copyTo(fs);
                        fs.Flush(true);
                    }
                }
                catch (Exception ex)
                {
                    log.Detail(ex.ToString());
                    return r = Abort(backup, new List<string> { part }, null, friendly + " was not installed. " + Sentence(ex, "The installer could not copy the file.") + " Nothing else changed.");
                }
                try { back = PakReader.Open(part); }
                catch (Exception) { return r = Abort(backup, new List<string> { part }, null, fileName + " did not read back after copying. Nothing else changed."); }
                if (back.Entries.Count != pr.Entries.Count) return r = Abort(backup, new List<string> { part }, null, fileName + " changed while copying. Nothing else changed.");
                if (IsGameRunning()) return r = Abort(backup, new List<string> { part }, null, "Bodycam was started. Close it and drop the file again.", ExitRunning);
                int mark = backup.Mark;
                bool movedIn = false;
                try
                {
                    if (File.Exists(live)) backup.Move(live, "older copy of " + fileName);
                    Fault("legacy.moves");
                    Util.MoveFile(part, live);
                    movedIn = true;
                    log.Detail("Copied " + fileName);
                }
                catch (Exception ex)
                {
                    log.Detail(ex.ToString());
                    int outMark = backup.Mark;
                    if (movedIn) TryBackup(backup, live, "not installed: a step did not finish");
                    TryBackup(backup, part, "not installed: a step did not finish");
                    int putBack = backup.UndoRange(mark, outMark);
                    string line = friendly + " was not installed. " + Sentence(ex, "A file step did not finish.") + (putBack > 0 ? " The previous copy was put back." : " Nothing else changed.");
                    log.Block(line);
                    return r = Result(ExitRefused, line);
                }
                MenuResult menu = RegenerateMenuCore(false, backup, foreignOk);
                if (menu.Skipped && menu.Why == WhyBaseChanged)
                {
                    log.Warn("Bodycam changed its map list since this installer was made. " + friendly + " is installed; the other custom cards wait for a newer installer.");
                    r = Result(ExitMenuWaiting, "menu waits");
                }
                else if (menu.Skipped && menu.Why != WhyNotNeeded)
                {
                    log.Warn(friendly + " is installed, but the map list was not updated (" + WhyText(menu.Why) + ").");
                    r = Result(ExitMenuWaiting, "menu " + menu.Why);
                }
                else
                {
                    log.Good("Installed " + friendly + ". Start Bodycam and look in Play > Custom > " + playMode + ".");
                    r = Result(ExitOk, "installed");
                }
                log.Detail(fileName + " is a single-file map: it carries its own copy of the map lists; the installer carries its cards when other maps are installed.");
                r.Menu = menu;
                r.Outcomes.Add(Outcome(MapKind.OlderPak, fileName, friendly, playMode, false));
            }
            finally { backup.Finish(); }
            r.BackupStamp = backup.Count > 0 ? backup.Stamp : null;
            return r;
        }

        public OperationResult Remove(string idOrPakName)
        {
            if (IsGameRunning()) { log.Block("Bodycam is open. Close it to install or remove maps."); return Result(ExitRunning, "running"); }
            if (MenuState.Load(game).Paused) { log.Warn(PausedLine); return Result(ExitRefused, "paused"); }
            IList<InstalledMap> all = ListInstalled();
            InstalledMap target = null;
            foreach (InstalledMap m in all)
                if (m.Kind == MapKind.Card && string.Equals(m.Id, idOrPakName, StringComparison.OrdinalIgnoreCase)) { target = m; break; }
            if (target == null)
                foreach (InstalledMap m in all)
                    if (m.Kind == MapKind.Card && m.CardDir != null && string.Equals(Path.GetFileName(m.CardDir), idOrPakName, StringComparison.OrdinalIgnoreCase)) { target = m; break; }
            if (target == null)
                foreach (InstalledMap m in all)
                    if (m.Kind != MapKind.Card && (string.Equals(m.Id, idOrPakName, StringComparison.OrdinalIgnoreCase) || string.Equals(m.DisplayName, idOrPakName, StringComparison.OrdinalIgnoreCase)
                                                   || string.Equals(Path.GetFileNameWithoutExtension(m.Id), idOrPakName, StringComparison.OrdinalIgnoreCase))) { target = m; break; }
            if (target == null) { log.Warn("Nothing named " + idOrPakName + " is installed."); return Result(ExitRefused, "not installed"); }

            Backup backup = Backup.Begin(game);
            backup.Log = log;
            OperationResult r;
            try
            {
                log.Progress(1, 3, "Removing " + target.Label + "...");

                MenuPlan plan = null;
                bool dropMenu = false;
                if (target.Kind != MapKind.OtherMod)
                {
                    log.Progress(2, 3, "Updating the map list...");
                    plan = PrepareMenu(false, backup, false, target);
                    MenuResult pr = plan.Result;
                    if (pr.Skipped && pr.Why == WhyBaseChanged) dropMenu = File.Exists(game.MenuPakPath);
                    else if (pr.Skipped && pr.Why != WhyNotNeeded)
                    {
                        log.Block(target.Label + " was not removed: " + WhyText(pr.Why) + ", and removing it now would leave its card pointing at a missing map. Nothing was removed.");
                        return r = Result(pr.Why == WhyRunning ? ExitRunning : ExitRefused, "remove refused: " + pr.Why);
                    }
                }
                if (IsGameRunning())
                {
                    if (plan != null && plan.Part != null) TryBackup(backup, plan.Part, "Bodycam was started");
                    log.Block("Bodycam is open. Close it to install or remove maps. Nothing was removed.");
                    return r = Result(ExitRunning, "running");
                }

                MenuResult menu = null;
                if (dropMenu)
                {
                    backup.Move(game.MenuPakPath, "Bodycam changed its map list: custom cards wait for a newer installer");
                    MenuState st = MenuState.Load(game);
                    st.MenuPakSha1 = null;
                    st.Save();
                    log.Dim("Kept a copy of the old map list in Backups.");
                }
                else if (plan != null)
                {
                    menu = CommitMenu(plan, backup);
                    if (menu.Skipped && menu.Why != WhyNotNeeded)
                    {
                        log.Block(target.Label + " was not removed: " + WhyText(menu.Why) + ". Nothing was removed.");
                        return r = Result(menu.Why == WhyRunning ? ExitRunning : ExitRefused, "remove refused: " + menu.Why);
                    }
                }

                log.Progress(3, 3, "Removing " + target.Label + "...");
                if (target.Kind == MapKind.Card)
                {
                    foreach (string pn in new string[] { PackagePaths.ContentPakName(target.Id), PackagePaths.ModesPakName(target.Id) })
                    {
                        string live = Path.Combine(game.PaksDir, pn);
                        if (File.Exists(live)) backup.Move(live, "removed " + target.Label);
                    }
                    backup.Move(target.CardDir, "removed " + target.Label);
                }
                else
                    foreach (string f in target.PakFiles) if (File.Exists(f)) backup.Move(f, "removed " + target.Id);
                if (dropMenu)
                {
                    log.Warn(target.Label + " removed. Bodycam changed its map list since this installer was made, so the other custom cards leave the menu until a newer installer is out.");
                    r = Result(ExitMenuWaiting, "removed; menu waits");
                }
                else
                {
                    log.Good(target.Label + " removed. A copy is in Backups.");
                    r = Result(ExitOk, "removed");
                }
                log.Detail("Backup folder: " + backup.Stamp);
                r.Menu = menu;
            }
            finally { backup.Finish(); }
            r.BackupStamp = backup.Count > 0 ? backup.Stamp : null;
            return r;
        }

        public OperationResult Pause()
        {
            if (IsGameRunning()) { log.Block("Bodycam is open. Close it to hide custom maps."); return Result(ExitRunning, "running"); }
            MenuState st = MenuState.Load(game);
            if (st.Paused) { log.Info("Custom maps are already hidden."); OperationResult same = Result(ExitOk, "already paused"); same.NothingChanged = true; return same; }
            List<string> files = new List<string>();
            foreach (InstalledMap m in ListInstalled())
            {
                if (m.Kind == MapKind.OtherMod || m.PakFiles == null) continue;
                foreach (string f in m.PakFiles) if (f.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && File.Exists(f)) files.Add(f);
            }
            if (File.Exists(game.MenuPakPath)) files.Add(game.MenuPakPath);
            Backup backup = Backup.Begin(game);
            backup.Log = log;
            try
            {
                foreach (string f in files)
                {
                    string rel = Util.Relative(game.PaksDir, f);
                    string dest = Path.Combine(game.PausedDir, rel.Replace('/', '\\') + ".off");
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    if (File.Exists(dest)) backup.Move(dest, "older paused copy");
                    Util.MoveFile(f, dest);
                    st.PausedFiles.Add(rel);
                }
                st.Paused = true;
                st.Save();
            }
            finally { backup.Finish(); }
            log.Good("Custom maps hidden: " + Util.Plural(files.Count, "file", "files") + " set aside. The game now shows only its own maps.");
            return Result(ExitOk, "paused");
        }

        public OperationResult Resume()
        {
            if (IsGameRunning()) { log.Block("Bodycam is open. Close it to show custom maps again."); return Result(ExitRunning, "running"); }
            MenuState st = MenuState.Load(game);
            List<string> offs = Util.FilesRecursive(game.PausedDir, ".pak.off");
            if (!st.Paused && offs.Count == 0) { log.Info("Custom maps are not hidden."); OperationResult same = Result(ExitOk, "not paused"); same.NothingChanged = true; return same; }
            Backup backup = Backup.Begin(game);
            backup.Log = log;
            OperationResult r;
            try
            {
                int n = 0;
                foreach (string off in offs)
                {
                    string rel = Util.Relative(game.PausedDir, off);
                    rel = rel.Substring(0, rel.Length - 4);
                    string dest = Path.Combine(game.PaksDir, rel.Replace('/', '\\'));
                    if (Util.IsGamePakName(Path.GetFileName(dest))) { backup.Move(off, "game file name"); continue; }
                    if (File.Exists(dest))
                    {
                        if (Util.Sha1File(dest) == Util.Sha1File(off)) { backup.Move(off, "same file is already back"); continue; }
                        backup.Move(dest, "name taken while maps were paused");
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    Util.MoveFile(off, dest);
                    n++;
                }
                st.Paused = false;
                st.PausedFiles.Clear();
                st.Save();
                MenuResult menu = RegenerateMenuCore(false, backup, false);
                log.Good("Custom maps are back: " + Util.Plural(n, "file", "files") + " returned." + (menu.Written ? " The map list is updated." : ""));
                r = Result(menu.Skipped && menu.Why == WhyBaseChanged ? ExitMenuWaiting : ExitOk, "resumed");
                r.Menu = menu;
            }
            finally { backup.Finish(); }
            r.BackupStamp = backup.Count > 0 ? backup.Stamp : null;
            return r;
        }

        public MenuResult RegenerateMenu(bool forceWrite)
        {
            MenuResult res;
            if (IsGameRunning())
            {
                log.Block("Bodycam is open. Close it to update the map list.");
                res = new MenuResult();
                res.Skipped = true; res.Why = WhyRunning;
                return res;
            }
            Backup backup = Backup.Begin(game);
            backup.Log = log;
            try { res = RegenerateMenuCore(forceWrite, backup, false); }
            finally { backup.Finish(); }
            return res;
        }

        MenuResult RegenerateMenuCore(bool forceWrite, Backup backup, bool foreignOk)
        {
            return CommitMenu(PrepareMenu(forceWrite, backup, foreignOk, null), backup);
        }

        sealed class MenuPlan
        {
            public readonly MenuResult Result = new MenuResult();
            public MenuState State;
            public MenuState.Owner Owner;
            public List<string> Order = new List<string>();
            public string Part;
            public bool RemoveLive;
            public bool Unchanged;
        }

        static bool Same(InstalledMap a, InstalledMap b)
        {
            if (a == null || b == null || a.Kind != b.Kind || !string.Equals(a.Id, b.Id, StringComparison.OrdinalIgnoreCase)) return false;
            if (a.Kind == MapKind.Card) return string.Equals(a.CardDir, b.CardDir, StringComparison.OrdinalIgnoreCase);
            return string.Equals(a.Source, b.Source, StringComparison.OrdinalIgnoreCase);
        }

        MenuPlan PrepareMenu(bool forceWrite, Backup backup, bool foreignOk, InstalledMap exclude)
        {
            MenuPlan plan = new MenuPlan();
            MenuResult res = plan.Result;
            MenuState st = MenuState.Load(game);
            plan.State = st;
            if (st.Paused)
            {
                res.Skipped = true; res.Why = WhyPaused;
                return plan;
            }
            IList<InstalledMap> installed = ListInstalled();
            List<InstalledMap> cards = new List<InstalledMap>();
            foreach (InstalledMap m in installed)
            {
                if (m.Kind != MapKind.Card || Same(m, exclude)) continue;
                if (m.Card == null || m.Problems.Count > 0)
                {
                    log.Warn(m.Label + " is missing files; its card stays out of the menu until it is removed or installed again.");
                    continue;
                }
                cards.Add(m);
                plan.Order.Add(m.Id);
            }
            List<LegacyRow> legacy = new List<LegacyRow>();
            int paksWithRows = 0;
            foreach (InstalledMap m in installed)
            {
                if (m.Kind != MapKind.OlderPak || m.Paused || m.PakFiles == null || m.PakFiles.Length == 0 || Same(m, exclude)) continue;
                if (m.Id.StartsWith("pakchunk", StringComparison.OrdinalIgnoreCase)) continue;
                IList<LegacyRow> rows;
                try { rows = LegacyRows.Read(m.PakFiles[0], baseTables, log); }
                catch (Exception) { log.Detail("The installer cannot read the map list inside " + m.Id + "; its card is not carried."); continue; }
                if (rows.Count > 0) paksWithRows++;
                legacy.AddRange(rows);
            }
            string menuPath = game.MenuPakPath;
            MenuState.Owner owner = st.Check();
            plan.Owner = owner;
            bool needed = forceWrite || cards.Count >= 1 || paksWithRows >= 2;
            if (!needed)
            {
                if (File.Exists(menuPath))
                {
                    if (owner == MenuState.Owner.Foreign && !foreignOk && !AskForeign())
                    {
                        log.Warn("Another mod already changed the game's map list. It was kept.");
                        res.Skipped = true; res.Why = WhyForeign;
                        return plan;
                    }
                    plan.RemoveLive = true;
                }
                res.Skipped = true; res.Why = WhyNotNeeded;
                return plan;
            }
            if (owner == MenuState.Owner.Foreign && !foreignOk && !AskForeign())
            {
                log.Warn("Another mod already changed the game's map list. It was kept.");
                res.Skipped = true; res.Why = WhyForeign;
                return plan;
            }
            BaseCheck check = baseTables.CheckAgainst(GamePakIndex.Load(game, log));
            if (check.Result == BaseCheck.Status.Mismatch || (check.Result == BaseCheck.Status.PaksMissing && !AcceptMissingGamePaks))
            {
                foreach (string dl in check.Details) log.Detail(dl);
                res.Skipped = true; res.Why = WhyBaseChanged;
                return plan;
            }
            MenuPakBuilder builder = new MenuPakBuilder(baseTables, legacy, cards, log);
            IList<KeyValuePair<string, byte[]>> entries = builder.BuildEntries();
            byte[] bytes = PakWriter.Build(MenuPakBuilder.PakName, PakWriter.ContentMount, entries);
            string sha = Util.Sha1Hex(bytes);
            res.Cards = builder.CustomMapCount;
            res.Entries = entries.Count;
            res.Sha1 = sha;
            if (File.Exists(menuPath) && owner == MenuState.Owner.Installer && string.Equals(st.MenuPakSha1, sha, StringComparison.OrdinalIgnoreCase))
            {
                plan.Unchanged = true;
                return plan;
            }
            string part = menuPath + ".part";
            if (File.Exists(part)) backup.Move(part, "unfinished step from an earlier run");
            Util.WriteNew(part, bytes);
            List<string> bad = new List<string>();
            try
            {
                PakReader r = PakReader.Open(part);
                if (r.Entries.Count != entries.Count) bad.Add("entry count " + r.Entries.Count);
                foreach (KeyValuePair<string, byte[]> kv in entries)
                {
                    PakEntryInfo e = r.FindContent(kv.Key);
                    if (e == null) { bad.Add("missing " + kv.Key); continue; }
                    if (!Util.BytesEqual(e.Sha1, Util.Sha1(kv.Value)) || !Util.BytesEqual(Util.Sha1(r.Read(e)), Util.Sha1(kv.Value))) bad.Add("bytes differ " + kv.Key);
                }
                if (bad.Count == 0) bad.AddRange(builder.CheckWrittenTables(r));
            }
            catch (Exception ex) { bad.Add(ex.Message); }
            if (bad.Count > 0)
            {
                backup.Move(part, "menu pak did not read back");
                log.Block("The new map list did not read back correctly, so the old one stays.");
                log.Detail("Read-back: " + bad[0]);
                res.Skipped = true; res.Why = WhyReadBack;
                return plan;
            }
            plan.Part = part;
            return plan;
        }

        MenuResult CommitMenu(MenuPlan plan, Backup backup)
        {
            MenuResult res = plan.Result;
            MenuState st = plan.State;
            string menuPath = game.MenuPakPath;
            if (res.Skipped && res.Why != WhyNotNeeded) return res;
            if (res.Skipped)
            {
                if (plan.RemoveLive && File.Exists(menuPath))
                {
                    if (IsGameRunning()) { log.Block("Bodycam is open. Close it so the map list can be updated."); res.Why = WhyRunning; return res; }
                    backup.Move(menuPath, "no custom cards left");
                    log.Dim("Kept a copy of the old map list in Backups.");
                }
                st.MenuPakSha1 = null;
                st.Order = plan.Order;
                st.Save();
                return res;
            }
            if (plan.Unchanged)
            {
                st.Order = plan.Order;
                st.Save();
                log.Info(Util.Plural(res.Cards, "map", "maps") + " in Play > Custom. The map list is up to date.");
                return res;
            }
            if (IsGameRunning())
            {
                backup.Move(plan.Part, "Bodycam was started");
                log.Block("Bodycam was started. Close it so the map list can be updated.");
                res.Skipped = true; res.Why = WhyRunning;
                return res;
            }
            if (File.Exists(menuPath))
            {
                backup.Move(menuPath, plan.Owner == MenuState.Owner.Foreign ? "map list made by another tool" : "menu cards rebuilt");
                log.Dim("Kept a copy of the old map list in Backups.");
            }
            Util.MoveFile(plan.Part, menuPath);
            st.MenuPakSha1 = res.Sha1;
            st.Order = plan.Order;
            st.Save();
            res.Written = true;
            log.Info(Util.Plural(res.Cards, "map now shows", "maps now show") + " in Play > Custom.");
            return res;
        }

        public int RecoverStaleFiles()
        {
            if (!Directory.Exists(game.PaksDir)) return 0;
            List<string> stale = new List<string>();
            foreach (string f in Directory.GetFiles(game.PaksDir, "*.part", SearchOption.TopDirectoryOnly))
                if (f.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) stale.Add(f);
            string staging = Path.Combine(game.CardsDir, ".staging");
            if (Directory.Exists(staging))
            {
                foreach (string d in Directory.GetDirectories(staging)) stale.Add(d);
                foreach (string f in Directory.GetFiles(staging)) stale.Add(f);
            }
            if (stale.Count == 0) return 0;
            if (IsGameRunning()) return 0;
            Backup backup = Backup.Begin(game);
            backup.Log = log;
            try
            {
                foreach (string s in stale)
                {
                    string rel = Util.Relative(game.PaksDir, s);
                    backup.Move(s, "unfinished step from an earlier run");
                    log.Detail("Unfinished step from last time: " + rel);
                }
                log.Info("Found an unfinished step from last time and put it in Backups. Nothing else changed.");
            }
            finally { backup.Finish(); }
            return stale.Count;
        }
    }
}
