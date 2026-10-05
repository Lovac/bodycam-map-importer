// Map packages (.bcmap, .zip, folder): open, classify, validate and create.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace BodycamMapInstaller.Core
{
    public sealed class ValidationReport
    {
        public bool Ok;
        public List<string> Problems = new List<string>();
        public List<string> Conflicts = new List<string>();
        public List<string> Notes = new List<string>();
        public bool PakChecksDeferred;
        public Dictionary<string, List<string>> PakPaths = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        void AddOnce(List<string> list, string s) { if (!list.Contains(s)) list.Add(s); }
        public void Problem(string s) { AddOnce(Problems, s); }
        public void Conflict(string s) { AddOnce(Problems, s); AddOnce(Conflicts, s); }
        public void Note(string s) { AddOnce(Notes, s); }
    }

    public enum DropKind { Package, Packages, LegacyPaks, LegacyPak, Project, NotAMap, Folder }

    public sealed class CardPackage : IDisposable
    {
        public CardInfo Card;
        public string SourceName, SourcePath, SourceSha256;
        public long SourceSize;
        public IList<string> Entries;
        public byte[] CardJsonBytes;

        ZipSource zip;
        string prefix = "";
        string folder;
        readonly List<string> openProblems = new List<string>();

        CardPackage() { }

        public static CardPackage Open(string zipBcmapOrFolder)
        {
            string path = zipBcmapOrFolder;
            string name = Path.GetFileName(path.TrimEnd('\\', '/'));
            CardPackage p = new CardPackage();
            p.SourceName = name;
            p.SourcePath = path;
            if (Directory.Exists(path))
            {
                p.folder = Path.GetFullPath(path);
                if (!File.Exists(Path.Combine(p.folder, "card.json"))) throw new PackageException(name + " is not a Bodycam map.");
                List<string> entries = new List<string>();
                foreach (string f in Util.FilesRecursive(p.folder, null))
                {
                    entries.Add(Util.Relative(p.folder, f));
                    p.SourceSize += new FileInfo(f).Length;
                }
                p.Entries = entries;
            }
            else
            {
                if (!File.Exists(path) || !ZipSource.LooksLikeZip(path)) throw new PackageException(name + " is not a Bodycam map.");
                try { p.zip = new ZipSource(path); }
                catch (InvalidDataException) { throw new PackageException(name + " is not a Bodycam map."); }
                p.SourceSize = new FileInfo(path).Length;
                p.SourceSha256 = Util.Sha256File(path);
                p.FinishZipOpen();
            }
            p.ReadCard();
            return p;
        }

        public static CardPackage OpenBytes(byte[] zipBytes, string name)
        {
            CardPackage p = new CardPackage();
            p.SourceName = name;
            p.SourcePath = name;
            try { p.zip = new ZipSource(zipBytes, name); }
            catch (InvalidDataException) { throw new PackageException(name + " is not a Bodycam map."); }
            p.SourceSize = zipBytes.Length;
            using (System.Security.Cryptography.SHA256 s = System.Security.Cryptography.SHA256.Create()) p.SourceSha256 = Util.Hex(s.ComputeHash(zipBytes));
            p.FinishZipOpen();
            p.ReadCard();
            return p;
        }

        static string CardPrefix(ZipSource z)
        {
            if (z.Has("card.json")) return "";
            string top = null;
            foreach (string n in z.Names)
            {
                int slash = n.IndexOf('/');
                if (slash <= 0) return null;
                string t = n.Substring(0, slash + 1);
                if (top == null) top = t;
                else if (t != top) return null;
            }
            return top != null && z.Has(top + "card.json") ? top : null;
        }

        void FinishZipOpen()
        {
            string pre = CardPrefix(zip);
            if (pre == null)
            {
                zip.Dispose();
                zip = null;
                throw new PackageException(SourceName + " is not a Bodycam map.");
            }
            prefix = pre;
            List<string> entries = new List<string>();
            bool unsafeSeen = false;
            foreach (string raw in zip.Names)
            {
                if (!PackagePaths.IsSafeEntryName(raw) || !raw.StartsWith(prefix, StringComparison.Ordinal))
                {
                    if (!unsafeSeen) openProblems.Add(SourceName + " has files the installer does not know. Ask its author for a newer version.");
                    unsafeSeen = true;
                    openProblems.Add("A file name in the package is not allowed: " + Printable(raw));
                    continue;
                }
                entries.Add(raw.Substring(prefix.Length));
            }
            entries.Sort(StringComparer.Ordinal);
            Entries = entries;
        }

        static string Printable(string s)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in s) sb.Append(c < 0x20 ? '?' : c);
            return sb.ToString();
        }

        void ReadCard()
        {
            if (!Has("card.json")) throw new PackageException(SourceName + " is not a Bodycam map.");
            CardJsonBytes = Read("card.json");
            try { Card = CardInfo.Parse(CardJsonBytes); }
            catch (PackageException ex)
            {
                Dispose();
                throw new PackageException(SourceName + " " + ex.Message + ".");
            }
        }

        public static IList<CardPackage> OpenAll(string zipPath)
        {
            List<CardPackage> list = new List<CardPackage>();
            using (ZipSource z = new ZipSource(zipPath))
            {
                List<string> names = new List<string>();
                foreach (string n in z.Names) if (n.EndsWith(".bcmap", StringComparison.OrdinalIgnoreCase) && PackagePaths.IsSafeEntryName(n)) names.Add(n);
                names.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string n in names) list.Add(OpenBytes(z.Read(n), Path.GetFileName(n)));
            }
            return list;
        }

        static readonly string[] HarmlessExtensions = { ".txt", ".md", ".png", ".jpg", ".jpeg", ".exe", ".config", ".dll" };

        static bool HarmlessEntry(string name)
        {
            if (name.EndsWith("/", StringComparison.Ordinal)) return true;
            string lower = name.ToLowerInvariant();
            string[] parts = lower.Split('/');
            if (parts.Length >= 2 && parts[0] == "installer") return true;
            if (parts.Length >= 3 && parts[1] == "installer") return true;
            foreach (string e in HarmlessExtensions) if (lower.EndsWith(e, StringComparison.Ordinal)) return true;
            return false;
        }

        public const int FolderSearchDepth = 3;

        public static List<string> FindMapsInFolder(string folder)
        {
            List<string> found = new List<string>();
            Walk(Path.GetFullPath(folder), 0, found);
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        static void Walk(string dir, int depth, List<string> found)
        {
            string[] files, dirs;
            try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
            catch (UnauthorizedAccessException) { return; }
            catch (IOException) { return; }
            foreach (string f in files)
            {
                string n = Path.GetFileName(f);
                if (n.EndsWith(".bcmap", StringComparison.OrdinalIgnoreCase)) found.Add(f);
                else if (n.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && !Util.IsGamePakName(n) && !string.Equals(n, MenuPakBuilder.PakName, StringComparison.OrdinalIgnoreCase)) found.Add(f);
            }
            if (depth >= FolderSearchDepth) return;
            foreach (string d in dirs)
            {
                string n = Path.GetFileName(d);
                if (n.StartsWith("_") || n.StartsWith(".")) continue;
                if (File.Exists(Path.Combine(d, "card.json"))) { found.Add(d); continue; }
                Walk(d, depth + 1, found);
            }
        }

        public static DropKind Classify(string path, out List<string> inner)
        {
            inner = new List<string>();
            try
            {
                if (Directory.Exists(path))
                {
                    if (File.Exists(Path.Combine(path, "card.json"))) return DropKind.Package;
                    inner = FindMapsInFolder(path);
                    return inner.Count > 0 ? DropKind.Folder : DropKind.NotAMap;
                }
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".uproject") return DropKind.Project;
                if (ext == ".pak") return File.Exists(path) ? DropKind.LegacyPak : DropKind.NotAMap;
                if (!File.Exists(path) || !ZipSource.LooksLikeZip(path)) return DropKind.NotAMap;
                using (ZipSource z = new ZipSource(path))
                {
                    if (CardPrefix(z) != null) return DropKind.Package;
                    bool other = false;
                    foreach (string n in z.Names)
                    {
                        string lower = n.ToLowerInvariant();
                        if (lower.EndsWith(".bcmap") || lower.EndsWith(".pak")) inner.Add(n);
                        else if (!HarmlessEntry(n)) other = true;
                    }
                    int bcmaps = 0, paks = 0;
                    foreach (string n in inner) if (n.EndsWith(".bcmap", StringComparison.OrdinalIgnoreCase)) bcmaps++; else paks++;
                    if (bcmaps > 0 && paks == 0 && !other) return DropKind.Packages;
                    if (paks > 0 && bcmaps == 0 && !other) return DropKind.LegacyPaks;
                    return DropKind.NotAMap;
                }
            }
            catch (InvalidDataException) { return DropKind.NotAMap; }
            catch (IOException) { return DropKind.NotAMap; }
            catch (UnauthorizedAccessException) { return DropKind.NotAMap; }
        }

        public static void Create(string zipPath, CardInfo card, IDictionary<string, byte[]> files)
        {
            List<string> names = new List<string>(files.Keys);
            names.Remove("card.json");
            names.Sort(StringComparer.Ordinal);
            using (FileStream fs = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (ZipArchive za = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                byte[] cardBytes = files.ContainsKey("card.json") ? files["card.json"] : card.ToJson();
                Add(za, "card.json", cardBytes, CompressionLevel.Optimal);
                foreach (string n in names)
                    Add(za, n, files[n], n.StartsWith("paks/", StringComparison.Ordinal) || n.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            }
        }

        static void Add(ZipArchive za, string name, byte[] data, CompressionLevel level)
        {
            ZipArchiveEntry e = za.CreateEntry(name, level);
            using (Stream s = e.Open()) s.Write(data, 0, data.Length);
        }

        public bool Has(string entry)
        {
            if (folder != null) return PackagePaths.IsSafeEntryName(entry) && File.Exists(Path.Combine(folder, Util.OsPath(entry)));
            return zip != null && zip.Has(prefix + entry);
        }

        public byte[] Read(string entry)
        {
            if (folder != null) return File.ReadAllBytes(Path.Combine(folder, Util.OsPath(entry)));
            return zip.Read(prefix + entry);
        }

        public long EntryLength(string entry)
        {
            if (folder != null) return new FileInfo(Path.Combine(folder, Util.OsPath(entry))).Length;
            return zip.Length(prefix + entry);
        }

        public void CopyEntryTo(string entry, Stream dest)
        {
            if (folder != null)
            {
                using (FileStream fs = new FileStream(Path.Combine(folder, Util.OsPath(entry)), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
                    fs.CopyTo(dest, 1 << 20);
                return;
            }
            zip.CopyTo(prefix + entry, dest);
        }

        public Func<Stream> SeekableOpener(string entry)
        {
            if (folder != null)
            {
                string f = Path.Combine(folder, Util.OsPath(entry));
                return delegate { return new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16); };
            }
            return zip.SeekableOpener(prefix + entry);
        }

        public void Dispose()
        {
            if (zip != null) { zip.Dispose(); zip = null; }
        }

        public string MapName { get { return Card != null && !string.IsNullOrEmpty(Card.DisplayName) ? Card.DisplayName : SourceName; } }

        public List<string> ExpectedMenuEntries(bool includeOptional)
        {
            List<string> list = new List<string>();
            if (Card.Modes != null && Card.Id != null)
                foreach (string m in Card.Modes)
                    if (PackagePaths.IsMode(m))
                    {
                        list.Add("menu/" + PackagePaths.DaPath(Card.Id, m) + ".uasset");
                        list.Add("menu/" + PackagePaths.DaPath(Card.Id, m) + ".uexp");
                    }
            if (Card.TextureRelPath != null)
            {
                list.Add("menu/" + Card.TextureRelPath + ".uasset");
                list.Add("menu/" + Card.TextureRelPath + ".uexp");
                if (includeOptional) list.Add("menu/" + Card.TextureRelPath + ".ubulk");
            }
            return list;
        }

        public ValidationReport Validate(InstallContext ctx)
        {
            ValidationReport r = new ValidationReport();
            CardInfo c = Card;
            string name = MapName;
            foreach (string p in openProblems) r.Problems.Add(p);
            foreach (string p in c.FieldProblems()) r.Problem(p);
            IList<string> idProblems = PackagePaths.IdProblems(c.Id, c.Modes, null);
            foreach (string p in idProblems) r.Problem(p);
            BaseTables bt = ctx != null && ctx.Base != null ? ctx.Base : BaseTables.Embedded();
            bool idUsable = idProblems.Count == 0 || (c.Id != null && System.Text.RegularExpressions.Regex.IsMatch(c.Id, "^[A-Za-z][A-Za-z0-9]{1,22}$"));
            if (idProblems.Count == 0 && bt.NameTablesContain(c.Id)) r.Problem("The map name \"" + c.Id + "\" is already used inside Bodycam's map lists.");
            if (ctx != null && c.Id != null)
            {
                InstalledMap same = ctx.FindCard(c.Id);
                if (same != null && !string.Equals(same.Id, c.Id, StringComparison.Ordinal))
                    r.Conflict(name + " uses the map name " + c.Id + ", which " + same.Label + " already has. Remove " + same.Label + " first.");
                if (ctx.LegacyRows != null)
                    foreach (LegacyRow lr in ctx.LegacyRows)
                        if (string.Equals(lr.Row.Name, c.Id, StringComparison.OrdinalIgnoreCase) && !InReplaces(lr.PakName))
                        {
                            string other = PackagePaths.FriendlyPakName(lr.PakName);
                            r.Conflict(name + " uses the map name " + c.Id + ", which " + other + " already has. Remove " + other + " first.");
                        }
                if (c.Replaces != null)
                    foreach (string rp in c.Replaces)
                    {
                        InstalledMap m = ctx.FindLoosePak(rp);
                        if (m != null && m.Kind == MapKind.Card) r.Problem("\"" + rp + "\" in replaces belongs to the map " + m.Label + ".");
                    }

                if (PackagePaths.IdProblems(c.Id, null, null).Count == 0)
                    foreach (string pn in new string[] { PackagePaths.ContentPakName(c.Id), PackagePaths.ModesPakName(c.Id) })
                    {
                        InstalledMap m = ctx.FindLoosePak(pn);
                        if (m != null && m.Kind != MapKind.Card && !InReplaces(m.Id))
                            r.Conflict(name + " needs the file name " + pn + ", which " + (m.Kind == MapKind.OlderPak ? "the single-file map " + m.Label : "the installed file " + m.Id) + " already uses. Remove it first.");
                    }
                if (c.GameBuild != null && ctx.Game != null && ctx.Game.BuildId != null && c.GameBuild != ctx.Game.BuildId)
                    r.Note(name + " was made for Bodycam build " + c.GameBuild + "; this PC has build " + ctx.Game.BuildId + ". It may still work.");
            }

            if (idUsable)
            {
                List<string> required = new List<string>();
                required.Add("card.json");
                required.Add("thumb.png");
                required.Add("paks/" + PackagePaths.ContentPakName(c.Id));
                required.Add("paks/" + PackagePaths.ModesPakName(c.Id));
                required.AddRange(ExpectedMenuEntries(false));
                HashSet<string> allowed = new HashSet<string>(required, StringComparer.Ordinal);
                allowed.Add("README.txt");
                if (c.TextureRelPath != null) allowed.Add("menu/" + c.TextureRelPath + ".ubulk");
                List<string> unknown = new List<string>();
                foreach (string e in Entries) if (!allowed.Contains(e)) unknown.Add(e);
                if (unknown.Count > 0)
                {
                    r.Problem(SourceName + " has files the installer does not know. Ask its author for a newer version.");
                    r.Problem("Files the installer does not know: " + string.Join(", ", unknown.GetRange(0, Math.Min(5, unknown.Count)).ToArray()) + (unknown.Count > 5 ? " and " + (unknown.Count - 5) + " more" : ""));
                }
                HashSet<string> have = new HashSet<string>(Entries, StringComparer.Ordinal);
                List<string> missing = new List<string>();
                foreach (string e in required) if (!have.Contains(e)) missing.Add(e);
                if (missing.Count > 0) r.Problem(SourceName + " is missing " + string.Join(", ", missing.ToArray()) + ". Ask its author for a newer version.");

                if (have.Contains("thumb.png"))
                {
                    int w, h;
                    if (!Util.PngSize(Read("thumb.png"), out w, out h) || w != 2 * h) r.Problem("thumb.png in " + SourceName + " is not a 2:1 PNG picture.");
                }
                if (c.Modes != null)
                    foreach (string m in c.Modes)
                    {
                        if (!PackagePaths.IsMode(m)) continue;
                        string baseName = "menu/" + PackagePaths.DaPath(c.Id, m);
                        if (have.Contains(baseName + ".uasset") && have.Contains(baseName + ".uexp"))
                            MenuFiles.CheckDa(c, m, Read(baseName + ".uasset"), Read(baseName + ".uexp"), r);
                    }
                if (c.TextureRelPath != null && have.Contains("menu/" + c.TextureRelPath + ".uasset") && have.Contains("menu/" + c.TextureRelPath + ".uexp"))
                    MenuFiles.CheckTexture(c, Read("menu/" + c.TextureRelPath + ".uasset"), Read("menu/" + c.TextureRelPath + ".uexp"), r);

                HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (bool modes in new bool[] { false, true })
                {
                    string pakName = modes ? PackagePaths.ModesPakName(c.Id) : PackagePaths.ContentPakName(c.Id);
                    if (!have.Contains("paks/" + pakName)) continue;
                    Func<Stream> opener = SeekableOpener("paks/" + pakName);
                    if (opener == null) { r.PakChecksDeferred = true; continue; }
                    PakReader pr;
                    try { pr = PakReader.Open(opener, true); }
                    catch (Exception) { r.Problem(SourceName + " holds " + pakName + ", which is not a pak the installer can read."); continue; }
                    CheckPak(pr, pakName, modes, r, ctx, seenPaths);
                }

                if (ctx != null)
                    foreach (string e in ExpectedMenuEntries(true))
                    {
                        string rel = e.Substring(5);
                        InstalledMap owner = ctx.OwnerOf(rel);
                        if (owner != null && !IsSelfOrReplaced(owner)) r.Conflict(name + " uses files that belong to " + owner.Label + ". Remove " + owner.Label + " first.");
                    }
            }

            if (c.Modes != null && Array.IndexOf(c.Modes, "HP") >= 0)
                r.Note(name + " also offers Hardpoint. Hardpoint on custom maps is still being tested. Team Deathmatch and Deathmatch are the safe picks.");
            r.Ok = r.Problems.Count == 0;
            return r;
        }

        bool InReplaces(string pakName)
        {
            if (Card.Replaces == null) return false;
            foreach (string s in Card.Replaces) if (string.Equals(s, pakName, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        bool IsSelfOrReplaced(InstalledMap owner)
        {
            if (owner.Kind == MapKind.Card) return string.Equals(owner.Id, Card.Id, StringComparison.Ordinal);
            return InReplaces(owner.Id);
        }

        public void CheckPak(PakReader pr, string pakName, bool modes, ValidationReport r, InstallContext ctx, HashSet<string> seenPaths)
        {
            CardInfo c = Card;
            string name = MapName;
            List<string> paths = new List<string>();
            foreach (PakEntryInfo e in pr.Entries.Values)
            {
                string rel = pr.ContentPath(e);
                if (rel == null) { r.Problem(name + ": " + pakName + " puts files outside Bodycam/Content."); continue; }
                if (e.Encrypted) r.Problem(name + ": " + pakName + " is encrypted; the game could not read it.");
                paths.Add(rel);
                if (seenPaths != null && !seenPaths.Add(rel)) r.Problem(name + ": both paks hold " + rel + ".");
            }
            paths.Sort(StringComparer.OrdinalIgnoreCase);
            r.PakPaths[pakName] = paths;
            HashSet<string> set = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
            if (modes)
            {
                HashSet<string> expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (c.Modes != null)
                    foreach (string m in c.Modes)
                        if (PackagePaths.IsMode(m)) { expected.Add(PackagePaths.WrapperPath(c.Id, m) + ".umap"); expected.Add(PackagePaths.WrapperPath(c.Id, m) + ".uexp"); }
                foreach (string p in paths)
                    if (!expected.Contains(p)) { r.Problem(name + ": " + pakName + " holds " + p + ", which is not one of this map's own mode wrappers."); }
                foreach (string p in expected)
                    if (!set.Contains(p)) r.Problem(name + ": " + pakName + " lacks the wrapper " + p + ".");
            }
            else
            {
                if (c.LevelPackage != null && c.LevelPackage.StartsWith("/Game/", StringComparison.Ordinal))
                {
                    string lvl = c.LevelPackage.Substring(6);
                    if (!set.Contains(lvl + ".umap") || !set.Contains(lvl + ".uexp")) r.Problem(name + ": " + pakName + " lacks the level " + lvl + ".umap / .uexp.");
                }
                foreach (string p in paths)
                    if (p.StartsWith("GM_Maps/", StringComparison.OrdinalIgnoreCase) || p.StartsWith("UI/", StringComparison.OrdinalIgnoreCase) || p.StartsWith("GM/WeatherManaer/", StringComparison.OrdinalIgnoreCase))
                        r.Problem(name + ": " + pakName + " holds " + p + ", which belongs in the menu files or the wrapper pak.");
            }
            if (ctx == null) return;
            foreach (string p in paths)
            {
                if (ctx.GamePaths != null && ctx.GamePaths.Contains(p)) { r.Problem(name + " would replace files of Bodycam itself (" + p + "). Not installed."); continue; }
                InstalledMap owner = ctx.OwnerOf(p);
                if (owner != null && !IsSelfOrReplaced(owner)) r.Conflict(name + " uses files that belong to " + owner.Label + ". Remove " + owner.Label + " first.");
            }
        }
    }

    public static class MenuFiles
    {
        static bool EndsWithTag(byte[] b)
        {
            int n = b.Length;
            return n >= 4 && b[n - 4] == 0xC1 && b[n - 3] == 0x83 && b[n - 2] == 0x2A && b[n - 1] == 0x9E;
        }

        static bool Complete(CookedPackageHeader h, byte[] uasset, byte[] uexp)
        {
            return h.Exports.Count == 1 && h["TotalHeaderSize"] == uasset.Length && h.ExportSerialOffset(0) == h["TotalHeaderSize"]
                   && h.ExportSerialSize(0) + 4 == uexp.Length && EndsWithTag(uexp);
        }

        public static void CheckDa(CardInfo c, string mode, byte[] uasset, byte[] uexp, ValidationReport r)
        {
            string file = "DA_" + c.Id + "_" + mode;
            CookedPackageHeader h;
            try { h = CookedPackageHeader.ParseLenient(uasset); }
            catch (Exception) { r.Problem(file + " in " + c.DisplayName + " cannot be read."); return; }
            if (h.PackageName != PackagePaths.DaPackage(c.Id, mode)) r.Problem(file + " names itself " + h.PackageName + ".");
            string own = "/Game/GM_Maps/" + PackagePaths.ModeFolder(mode) + "/" + PackagePaths.ModePrefix(mode) + c.Id + "?";
            bool hasLevel = false;
            bool hasTexture = false;
            foreach (KeyValuePair<string, byte[]> n in h.Names)
            {
                if (n.Key.StartsWith(own, StringComparison.Ordinal) && n.Key.IndexOf(PackagePaths.LevelTokenLobby, StringComparison.Ordinal) >= 0) hasLevel = true;
                else if (n.Key.StartsWith("/Game/GM_Maps/", StringComparison.OrdinalIgnoreCase)) r.Problem(file + " points at another map (" + n.Key + ").");
                if (n.Key == c.MenuTexture) hasTexture = true;
            }
            if (!hasLevel) r.Problem(file + " does not open its own " + PackagePaths.ModeLongName(mode) + " wrapper.");
            if (!hasTexture) r.Problem(file + " does not show the photo " + c.MenuTexture + ".");
            if (!Complete(h, uasset, uexp)) r.Problem(file + " is not a complete cooked file.");
            string[] texts = ReadDaTexts(uexp);
            if (texts == null) r.Problem("The installer cannot read the map name inside " + file + ".");
            else
            {
                if (texts[0] != c.DisplayName) r.Problem(file + " shows the name \"" + texts[0] + "\" but card.json says \"" + c.DisplayName + "\".");
                if (texts[1] != c.Description) r.Problem(file + " shows the description \"" + texts[1] + "\" but card.json says \"" + c.Description + "\".");
            }
        }

        public static void CheckTexture(CardInfo c, byte[] uasset, byte[] uexp, ValidationReport r)
        {
            string file = c.TextureName;
            CookedPackageHeader h;
            try { h = CookedPackageHeader.ParseLenient(uasset); }
            catch (Exception) { r.Problem(file + " in " + c.DisplayName + " cannot be read."); return; }
            if (h.PackageName != c.MenuTexture) r.Problem(file + " names itself " + h.PackageName + ".");
            if (!Complete(h, uasset, uexp)) r.Problem(file + " is not a complete cooked file.");
        }

        public static string[] ReadDaTexts(byte[] uexp)
        {
            try
            {
                if (uexp.Length < 60 || uexp[0] != 0x00 || uexp[1] != 0x08 || uexp[2] != 0x02 || uexp[3] != 0x05) return null;
                int o = 4 + 8 + 20 + 20;
                if (BitConverter.ToInt32(uexp, o) != 2) return null;
                o += 4;
                string t0 = ReadText(uexp, ref o);
                string t1 = ReadText(uexp, ref o);
                if (t0 == null || t1 == null) return null;
                return new string[] { t0, t1 };
            }
            catch (Exception) { return null; }
        }

        static string ReadText(byte[] b, ref int o)
        {
            o += 4;
            sbyte history = (sbyte)b[o]; o += 1;
            if (history == -1)
            {
                int has = BitConverter.ToInt32(b, o); o += 4;
                return has != 0 ? FString(b, ref o) : "";
            }
            if (history == 0)
            {
                FString(b, ref o);
                FString(b, ref o);
                return FString(b, ref o);
            }
            return null;
        }

        static string FString(byte[] b, ref int o)
        {
            int end;
            string s = CookedPackageHeader.FStr(b, o, out end);
            o = end;
            return s;
        }
    }
}
