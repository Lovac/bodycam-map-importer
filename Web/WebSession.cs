// The web version of the importer (5 Oct 2026). Browsers may not write into C:\Program Files (x86), where Steam puts
// Bodycam, so the page never touches the game: it runs the desktop app's own installer on a fake game folder that lives
// in the browser's memory (the same fake-game mode the map releases are built with), then hands the player the files
// that changed. The player drags them into Bodycam\Content\Paks. What the page remembers between visits (which maps
// this player added) is the fake Paks folder, kept in the browser's storage without the big map files.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BodycamMapInstaller.Core;

namespace BodycamMapImporterWeb
{
    public sealed class LogLine
    {
        public string Kind, Text;
    }

    public sealed class WebLog : ILog
    {
        public readonly List<LogLine> Lines = new List<LogLine>();
        void Add(string k, string t) { Lines.Add(new LogLine { Kind = k, Text = t }); }
        public void Info(string line) { Add("info", line); }
        public void Good(string line) { Add("good", line); }
        public void Warn(string line) { Add("warn", line); }
        public void Block(string line) { Add("block", line); }
        public void Dim(string line) { Add("dim", line); }
        public void Detail(string line) { Add("detail", line); }
        public void Progress(int step, int total, string what) { }
    }

    public sealed class OutFile
    {
        public string Name;
        public byte[] Data;
    }

    public sealed class StepResult
    {
        public bool Ok;
        public string Message;
        public List<OutFile> Copy = new List<OutFile>();       // drag these into Paks (replace)
        public List<string> Delete = new List<string>();        // delete these from Paks
        public List<LogLine> Log = new List<LogLine>();
    }

    public sealed class WebSession
    {
        public const string Root = "/bcmi/game";
        const string Inbox = "/bcmi/in";

        public readonly GameInstall Game;
        public string PaksDir { get { return Game.PaksDir; } }

        public WebSession()
        {
            Directory.CreateDirectory(Root);
            Game = GameInstall.FromRoot(Root, "Web", null);
            Directory.CreateDirectory(Game.PaksDir);
            Directory.CreateDirectory(Path.GetDirectoryName(Game.ExePath) ?? Root);
            if (!File.Exists(Game.ExePath)) File.WriteAllBytes(Game.ExePath, new byte[0]);
            string marker = Path.Combine(Game.Root, GameInstall.SelftestMarker);
            if (!File.Exists(marker)) File.WriteAllText(marker, "web importer fake game");
            Directory.CreateDirectory(Inbox);
        }

        Installer NewInstaller(WebLog log, AskUser ask)
        {
            Installer i = new Installer(Game, null, log, ask);
            i.IsGameRunning = delegate { return false; };
            return i;
        }

        // ------------------------------------------------------------------------- the remembered Paks folder
        static bool Skip(string rel)
        {
            return rel.StartsWith("_backup/", StringComparison.OrdinalIgnoreCase);
        }

        IEnumerable<string> AllFiles()
        {
            if (!Directory.Exists(PaksDir)) yield break;
            foreach (string f in Directory.GetFiles(PaksDir, "*", SearchOption.AllDirectories))
            {
                string rel = f.Substring(PaksDir.Length).TrimStart('/').Replace('\\', '/');
                if (!Skip(rel)) yield return rel;
            }
        }

        string Full(string rel) { return Path.Combine(PaksDir, rel); }

        static bool IsMapPak(string rel)
        {
            return rel.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && !rel.Contains("/")
                   && !string.Equals(rel, MenuPakBuilder.PakName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>What the browser keeps: every file, the map paks already shrunk to their index (ShrinkMapPaks).</summary>
        public Dictionary<string, byte[]> Snapshot()
        {
            Dictionary<string, byte[]> d = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string rel in AllFiles()) d[rel] = File.ReadAllBytes(Full(rel));
            return d;
        }

        public void Restore(Dictionary<string, byte[]> files)
        {
            foreach (KeyValuePair<string, byte[]> kv in files)
            {
                if (Skip(kv.Key) || kv.Key.Contains("..")) continue;
                string f = Full(kv.Key);
                string dir = Path.GetDirectoryName(f);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(f, kv.Value);
            }
        }

        Dictionary<string, string> Hashes()
        {
            Dictionary<string, string> h = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using (SHA1 sha = SHA1.Create())
                foreach (string rel in AllFiles())
                    h[rel] = Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(Full(rel))));
            return h;
        }

        StepResult Diff(Dictionary<string, string> before, OperationResult r, WebLog log)
        {
            StepResult s = new StepResult();
            s.Ok = r.Ok;
            s.Message = r.Message;
            s.Log = log.Lines;
            Dictionary<string, string> after = Hashes();
            foreach (KeyValuePair<string, string> kv in after)
            {
                // only what goes into Paks itself; the installer's own records (_cards) stay in the browser
                if (kv.Key.Contains("/")) continue;
                string old;
                if (before.TryGetValue(kv.Key, out old) && old == kv.Value) continue;
                byte[] data = File.ReadAllBytes(Full(kv.Key));
                s.Copy.Add(new OutFile { Name = kv.Key, Data = data });
            }
            foreach (string rel in before.Keys)
                if (!rel.Contains("/") && !after.ContainsKey(rel)) s.Delete.Add(rel);
            s.Copy.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            s.Delete.Sort(StringComparer.OrdinalIgnoreCase);
            return s;
        }

        /// <summary>After the player has the files, a map pak is kept only as its index: the same file list with empty
        /// files, which is all the installer reads from an installed map (what it owns, which modes it has).</summary>
        public void ShrinkMapPaks()
        {
            foreach (string rel in AllFiles())
            {
                if (!IsMapPak(rel)) continue;
                string f = Full(rel);
                byte[] now = File.ReadAllBytes(f);
                if (now.Length < 64 * 1024) continue;                  // already small (a skeleton, or tiny)
                try
                {
                    PakReader r = PakReader.Open(f, false);
                    List<KeyValuePair<string, byte[]>> empty = new List<KeyValuePair<string, byte[]>>();
                    foreach (string k in r.Entries.Keys.OrderBy(x => x, StringComparer.Ordinal))
                        empty.Add(new KeyValuePair<string, byte[]>(k, new byte[0]));
                    byte[] skeleton = PakWriter.Build(rel, r.MountPoint, empty);
                    string tmp = f + ".skel";
                    File.WriteAllBytes(tmp, skeleton);
                    List<string> a = r.ContentPaths(), b = PakReader.Open(tmp, false).ContentPaths();
                    a.Sort(StringComparer.Ordinal); b.Sort(StringComparer.Ordinal);
                    if (a.SequenceEqual(b)) { File.Delete(f); File.Move(tmp, f); }
                    else File.Delete(tmp);                            // keep the full pak rather than a wrong list
                }
                catch (Exception) { }
            }
            string backups = Path.Combine(PaksDir, "_backup");
            if (Directory.Exists(backups)) Directory.Delete(backups, true);   // in-memory copy only, never the player's
        }

        // ------------------------------------------------------------------------- the three things a player does
        public StepResult Install(string fileName, byte[] data, AskUser ask)
        {
            WebLog log = new WebLog();
            string safe = Path.GetFileName(fileName.Replace('\\', '/'));
            string path = Path.Combine(Inbox, safe);
            File.WriteAllBytes(path, data);
            Dictionary<string, string> before = Hashes();
            OperationResult r;
            try { r = NewInstaller(log, ask).InstallDropped(path); }
            finally { File.Delete(path); }
            return Diff(before, r, log);
        }

        public StepResult Remove(string id, AskUser ask)
        {
            WebLog log = new WebLog();
            Dictionary<string, string> before = Hashes();
            OperationResult r = NewInstaller(log, ask).Remove(id);
            return Diff(before, r, log);
        }

        public IList<InstalledMap> List()
        {
            return NewInstaller(new WebLog(), null).ListInstalled();
        }

        /// <summary>The menu file for exactly the maps this page knows (e.g. after the player lost theirs).</summary>
        public StepResult MenuOnly()
        {
            WebLog log = new WebLog();
            StepResult s = new StepResult();
            MenuResult m = NewInstaller(log, null).RegenerateMenu(true);
            s.Log = log.Lines;
            s.Ok = File.Exists(Game.MenuPakPath);
            if (s.Ok) s.Copy.Add(new OutFile { Name = MenuPakBuilder.PakName, Data = File.ReadAllBytes(Game.MenuPakPath) });
            return s;
        }
    }
}
