// Finds the Bodycam install through Steam and reads the index of the game paks. Read only.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace BodycamMapInstaller.Core
{
    public sealed class GameInstall
    {
        public string Root, PaksDir, ExePath, CardsDir, BackupRoot, PausedDir;
        public string Source;
        public string BuildId;

        public const string SelftestMarker = "BCMI_SELFTEST.txt";

        public static GameInstall FromRoot(string root, string source, string buildId)
        {
            GameInstall g = new GameInstall();
            g.Root = Path.GetFullPath(root).TrimEnd('\\', '/');
            g.PaksDir = Path.Combine(g.Root, @"Bodycam\Content\Paks");
            g.ExePath = Path.Combine(g.Root, @"Bodycam\Binaries\Win64\" + GameLocator.ShippingExe + ".exe");
            g.CardsDir = Path.Combine(g.PaksDir, "_cards");
            g.BackupRoot = Path.Combine(g.PaksDir, "_backup");
            g.PausedDir = Path.Combine(g.PaksDir, "_paused");
            g.Source = source;
            g.BuildId = buildId;
            if (g.IsSelftestFake) g.Source = "Selftest";
            return g;
        }

        public string MenuPakPath { get { return Path.Combine(PaksDir, MenuPakBuilder.PakName); } }

        public bool IsSelftestFake
        {
            get
            {
                try { return File.Exists(Path.Combine(Root, SelftestMarker)) && File.Exists(ExePath) && new FileInfo(ExePath).Length == 0; }
                catch (Exception) { return false; }
            }
        }
    }

    public static class GameLocator
    {
        public const string SteamAppId = "2406770";
        public const string ShippingExe = "Bodycam-Win64-Shipping";

        public static IList<string> SteamRoots()
        {
            List<string> roots = new List<string>();
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam", false))
                    if (k != null) { string v = k.GetValue("SteamPath") as string; if (!string.IsNullOrEmpty(v)) roots.Add(v.Replace('/', '\\')); }
            }
            catch (Exception) { }
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (RegistryKey k = b.OpenSubKey(@"SOFTWARE\Valve\Steam", false))
                    if (k != null) { string v = k.GetValue("InstallPath") as string; if (!string.IsNullOrEmpty(v)) roots.Add(v); }
            }
            catch (Exception) { }
            List<string> unique = new List<string>();
            foreach (string r in roots)
            {
                bool dup = false;
                foreach (string u in unique) if (string.Equals(Util.FullDir(u), Util.FullDir(r), StringComparison.OrdinalIgnoreCase)) dup = true;
                if (!dup) unique.Add(r);
            }
            return unique;
        }

        public static IList<string> SteamLibraries()
        {
            List<string> libs = new List<string>();
            foreach (string root in SteamRoots())
            {
                libs.Add(root);
                foreach (string l in SteamLocator.Libraries(root)) libs.Add(l);
            }
            return libs;
        }

        public static GameInstall FindSteam(ILog log)
        {
            foreach (string root in SteamRoots())
            {
                string buildId;
                string game;
                try { game = SteamLocator.FindInSteamRoot(root, out buildId); }
                catch (Exception) { continue; }
                if (game != null)
                {
                    GameInstall g = GameInstall.FromRoot(game, "Steam", buildId);
                    if (log != null) log.Detail("Found Bodycam at " + g.Root);
                    return g;
                }
            }
            if (log != null) log.Warn("Looked in every Steam library on this PC: Bodycam is not there.");
            return null;
        }

        public static bool IsValidRoot(string dir)
        {
            try
            {
                return File.Exists(Path.Combine(dir, @"Bodycam\Binaries\Win64\" + ShippingExe + ".exe")) && Directory.Exists(Path.Combine(dir, @"Bodycam\Content\Paks"));
            }
            catch (Exception) { return false; }
        }

        public static GameInstall FromUserPath(string anyPath)
        {
            if (string.IsNullOrEmpty(anyPath)) return null;
            string dir;
            try
            {
                dir = Path.GetFullPath(anyPath.Trim().Trim('"'));
                if (File.Exists(dir)) dir = Path.GetDirectoryName(dir);
            }
            catch (Exception) { return null; }
            for (int up = 0; up < 6 && !string.IsNullOrEmpty(dir); up++)
            {
                if (IsValidRoot(dir))
                {
                    string buildId = null;
                    try
                    {
                        string acf = Path.Combine(dir, @"..\..\appmanifest_" + SteamAppId + ".acf");
                        if (File.Exists(acf))
                        {
                            Dictionary<string, object> m = Vdf.Parse(File.ReadAllText(acf));
                            Dictionary<string, object> st = m.ContainsKey("AppState") ? m["AppState"] as Dictionary<string, object> : null;
                            if (st != null && st.ContainsKey("buildid")) buildId = st["buildid"] as string;
                        }
                    }
                    catch (Exception) { }
                    return GameInstall.FromRoot(dir, "Manual", buildId);
                }
                DirectoryInfo parent = Directory.GetParent(dir);
                dir = parent == null ? null : parent.FullName;
            }
            return null;
        }

        public static bool IsGameRunning()
        {
            Process[] p = Process.GetProcessesByName(ShippingExe);
            bool any = p.Length > 0;
            foreach (Process x in p) x.Dispose();
            return any;
        }
    }

    public static class SteamLocator
    {
        public const string AppId = GameLocator.SteamAppId;

        public static List<string> Libraries(string steamRoot)
        {
            List<string> libs = new List<string>();
            string vdfPath = Path.Combine(steamRoot, @"steamapps\libraryfolders.vdf");
            if (!File.Exists(vdfPath)) return libs;
            Dictionary<string, object> root = Vdf.Parse(File.ReadAllText(vdfPath));
            object lf;
            if (root.TryGetValue("libraryfolders", out lf) && lf is Dictionary<string, object>)
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)lf)
                {
                    Dictionary<string, object> entry = kv.Value as Dictionary<string, object>;
                    object p;
                    if (entry != null && entry.TryGetValue("path", out p) && p is string) libs.Add((string)p);
                }
            return libs;
        }

        public static string FindInSteamRoot(string steamRoot, out string buildId)
        {
            buildId = null;
            List<string> libs = new List<string>();
            libs.Add(steamRoot);
            libs.AddRange(Libraries(steamRoot));
            foreach (string lib in libs)
            {
                string acf = Path.Combine(lib, @"steamapps\appmanifest_" + AppId + ".acf");
                if (!File.Exists(acf)) continue;
                Dictionary<string, object> m = Vdf.Parse(File.ReadAllText(acf));
                Dictionary<string, object> st = m.ContainsKey("AppState") ? m["AppState"] as Dictionary<string, object> : null;
                if (st == null || !st.ContainsKey("installdir")) continue;
                string game = Path.Combine(lib, @"steamapps\common", (string)st["installdir"]);
                if (GameLocator.IsValidRoot(game))
                {
                    buildId = st.ContainsKey("buildid") ? st["buildid"] as string : null;
                    return game;
                }
            }
            return null;
        }
    }

    public static class Vdf
    {
        public static Dictionary<string, object> Parse(string text)
        {
            int i = 0;
            return Block(text, ref i, false);
        }

        static Dictionary<string, object> Block(string t, ref int i, bool nested)
        {
            Dictionary<string, object> d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            while (true)
            {
                string key = Token(t, ref i);
                if (key == null) { if (nested) throw new InvalidDataException("vdf: unexpected end"); return d; }
                if (key == "}") { if (!nested) throw new InvalidDataException("vdf: stray }"); return d; }
                string val = Token(t, ref i);
                if (val == "{") d[key] = Block(t, ref i, true);
                else if (val == null || val == "}") throw new InvalidDataException("vdf: key without value " + key);
                else d[key] = val;
            }
        }

        static string Token(string t, ref int i)
        {
            while (i < t.Length)
            {
                char c = t[i];
                if (char.IsWhiteSpace(c)) { i++; continue; }
                if (c == '/' && i + 1 < t.Length && t[i + 1] == '/') { while (i < t.Length && t[i] != '\n') i++; continue; }
                if (c == '{' || c == '}') { i++; return c.ToString(); }
                if (c == '"')
                {
                    StringBuilder sb = new StringBuilder();
                    i++;
                    while (i < t.Length && t[i] != '"')
                    {
                        if (t[i] == '\\' && i + 1 < t.Length) { i++; char e = t[i]; sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e); }
                        else sb.Append(t[i]);
                        i++;
                    }
                    i++;
                    return sb.ToString();
                }
                int s = i;
                while (i < t.Length && !char.IsWhiteSpace(t[i]) && t[i] != '{' && t[i] != '}' && t[i] != '"') i++;
                return t.Substring(s, i - s);
            }
            return null;
        }
    }

    public sealed class GamePakIndex
    {
        sealed class Hit { public string PakName; public int Order; public PakReader Reader; public PakEntryInfo Entry; }
        sealed class Cached { public long Size; public DateTime Mtime; public PakReader Reader; public List<KeyValuePair<string, PakEntryInfo>> Content; }

        static readonly Dictionary<string, Cached> cache = new Dictionary<string, Cached>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, Hit> winners = new Dictionary<string, Hit>(StringComparer.OrdinalIgnoreCase);
        public int PakCount;
        public readonly List<string> Unreadable = new List<string>();

        public static GamePakIndex Empty() { return new GamePakIndex(); }

        public static GamePakIndex Load(GameInstall game, ILog log)
        {
            GamePakIndex idx = new GamePakIndex();
            if (game == null || !Directory.Exists(game.PaksDir)) return idx;
            string[] files = Directory.GetFiles(game.PaksDir, "*.pak", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string f in files)
            {
                string name = Path.GetFileName(f);
                if (!Util.IsGamePakName(name)) continue;
                idx.PakCount++;
                Cached c;
                FileInfo fi = new FileInfo(f);
                lock (cache)
                {
                    if (!cache.TryGetValue(f, out c) || c.Size != fi.Length || c.Mtime != fi.LastWriteTimeUtc)
                    {
                        c = new Cached();
                        c.Size = fi.Length; c.Mtime = fi.LastWriteTimeUtc;
                        try
                        {
                            c.Reader = PakReader.Open(f, false);
                            c.Content = new List<KeyValuePair<string, PakEntryInfo>>();
                            foreach (PakEntryInfo e in c.Reader.Entries.Values)
                            {
                                string rel = c.Reader.ContentPath(e);
                                if (rel != null) c.Content.Add(new KeyValuePair<string, PakEntryInfo>(rel, e));
                            }
                        }
                        catch (Exception ex)
                        {
                            c.Reader = null;
                            c.Content = new List<KeyValuePair<string, PakEntryInfo>>();
                            if (log != null) log.Detail("Could not read the index of " + name + " (" + ex.Message + ").");
                        }
                        cache[f] = c;
                    }
                }
                if (c.Reader == null) { idx.Unreadable.Add(name); continue; }
                int order = PakReader.ReadOrder(name);
                foreach (KeyValuePair<string, PakEntryInfo> kv in c.Content)
                {
                    Hit h;
                    if (idx.winners.TryGetValue(kv.Key, out h))
                    {
                        if (order < h.Order || (order == h.Order && string.Compare(name, h.PakName, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                    }
                    h = new Hit();
                    h.PakName = name; h.Order = order; h.Reader = c.Reader; h.Entry = kv.Value;
                    idx.winners[kv.Key] = h;
                }
            }
            return idx;
        }

        public bool Contains(string relPath)
        {
            return winners.ContainsKey(relPath);
        }

        public PakEntryInfo Winner(string relPath, out string pakName)
        {
            pakName = null;
            Hit h;
            if (!winners.TryGetValue(relPath, out h)) return null;
            pakName = h.PakName;
            if (h.Entry.Sha1 == null) h.Reader.ReadSha1(h.Entry);
            return h.Entry;
        }
    }
}
