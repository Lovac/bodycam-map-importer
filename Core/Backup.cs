// Backups: every replaced or removed file is moved to Paks/_backup, never deleted.
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace BodycamMapInstaller.Core
{
    public sealed class Backup
    {
        sealed class MoveRecord
        {
            public string From, To, Why, Sha1, Kind, FromFull, ToFull;
            public List<KeyValuePair<string, string>> InnerRenames = new List<KeyValuePair<string, string>>();
            public bool PutBack;
        }

        readonly GameInstall game;
        readonly List<MoveRecord> moves = new List<MoveRecord>();
        bool finished;
        public string Stamp, Dir;
        public ILog Log;

        public static Backup Begin(GameInstall game)
        {
            Backup b = new Backup(game);
            return b;
        }

        Backup(GameInstall game) { this.game = game; }

        public int Count { get { return moves.Count; } }

        void EnsureDir()
        {
            if (Dir != null) return;
            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture);
            string dir = Path.Combine(game.BackupRoot, stamp);
            for (int n = 2; Directory.Exists(dir); n++) { dir = Path.Combine(game.BackupRoot, stamp + "_" + n); }
            Directory.CreateDirectory(dir);
            Dir = dir;
            Stamp = Path.GetFileName(dir);
        }

        static string FreeName(string path, bool isDir)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            for (int n = 2; ; n++)
            {
                string p = path + "." + n;
                if (!File.Exists(p) && !Directory.Exists(p)) return p;
            }
        }

        public string Move(string path, string why)
        {
            if (finished) throw new InvalidOperationException("backup already finished");
            string full = Path.GetFullPath(path);
            bool isDir = Directory.Exists(full);
            if (!isDir && !File.Exists(full)) throw new FileNotFoundException("nothing to back up at " + full);
            string rel = Util.Relative(game.PaksDir, full);
            if (rel.StartsWith("_backup/", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("refusing to back up the backup folder");
            if (!isDir && full.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) && Util.IsGamePakName(Path.GetFileName(full)))
                throw new InvalidOperationException("refusing to move a game pak " + rel);
            EnsureDir();
            string destRel = rel + (!isDir && rel.EndsWith(".pak", StringComparison.OrdinalIgnoreCase) ? ".bak" : "");
            string dest = FreeName(Path.Combine(Dir, Util.OsPath(destRel)), isDir);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            MoveRecord m = new MoveRecord();
            m.From = rel; m.Why = why; m.Kind = isDir ? "folder" : "file";
            m.FromFull = full; m.ToFull = dest;
            if (!isDir) m.Sha1 = Util.Sha1File(full);
            if (isDir) Util.MoveDirectory(full, dest); else Util.MoveFile(full, dest);
            m.To = Util.Relative(Dir, dest);
            moves.Add(m);
            if (isDir)
                foreach (string pak in Util.FilesRecursive(dest, ".pak"))
                {
                    string renamed = FreeName(pak + ".bak", false);
                    Util.MoveFile(pak, renamed);
                    m.InnerRenames.Add(new KeyValuePair<string, string>(pak, renamed));
                }
            if (Log != null) Log.Detail("Moved " + rel + " to the backup folder (" + Stamp + ").");
            return dest;
        }

        public int Mark { get { return moves.Count; } }

        public int UndoRange(int from, int end)
        {
            int n = 0;
            for (int i = Math.Min(end, moves.Count) - 1; i >= Math.Max(0, from); i--)
            {
                MoveRecord m = moves[i];
                if (m.PutBack) continue;
                try
                {
                    if (File.Exists(m.FromFull) || Directory.Exists(m.FromFull))
                    {
                        if (Log != null) Log.Detail("Could not put back " + m.From + ": the place is taken again. It stays in the backup folder (" + Stamp + ").");
                        continue;
                    }
                    for (int k = m.InnerRenames.Count - 1; k >= 0; k--)
                        if (File.Exists(m.InnerRenames[k].Value)) Util.MoveFile(m.InnerRenames[k].Value, m.InnerRenames[k].Key);
                    Directory.CreateDirectory(Path.GetDirectoryName(m.FromFull));
                    if (m.Kind == "folder") Util.MoveDirectory(m.ToFull, m.FromFull); else Util.MoveFile(m.ToFull, m.FromFull);
                    m.PutBack = true;
                    n++;
                    if (Log != null) Log.Detail("Put back " + m.From + " from the backup folder (" + Stamp + ").");
                }
                catch (Exception ex)
                {
                    if (Log != null) Log.Detail("Could not put back " + m.From + " (" + ex.Message + "). It stays in the backup folder (" + Stamp + ").");
                }
            }
            return n;
        }

        public int PutBackCount
        {
            get { int n = 0; foreach (MoveRecord m in moves) if (m.PutBack) n++; return n; }
        }

        public void Finish()
        {
            if (finished) return;
            finished = true;
            if (moves.Count == 0) return;
            List<object> list = new List<object>();
            StringBuilder restore = new StringBuilder();
            restore.Append("Bodycam Map Installer backup ").Append(Stamp).Append("\n\n");
            restore.Append("Nothing here was deleted. To put a file back: close Bodycam, move it from this folder into\n");
            restore.Append(game.PaksDir).Append("\n");
            restore.Append("at the place shown below, with the name shown (a .pak.bak file becomes .pak again).\n");
            restore.Append("If the installer rebuilt the menu cards since, open the installer afterwards so it can rebuild them again.\n\n");
            foreach (MoveRecord m in moves)
            {
                JsonOut o = new JsonOut();
                o.Set("from", m.From).Set("to", m.To).Set("kind", m.Kind).Set("why", m.Why);
                if (m.Sha1 != null) o.Set("sha1", m.Sha1);
                if (m.PutBack) o.Set("put_back", true);
                list.Add(o);
                if (m.PutBack) restore.Append("(already put back by the installer) ").Append(m.To).Append("  ->  Paks/").Append(m.From).Append("\n");
                else restore.Append(m.To).Append("  ->  Paks/").Append(m.From).Append("   (").Append(m.Why).Append(")\n");
            }
            JsonOut manifest = new JsonOut();
            manifest.Set("format", 1).Set("stamp", Stamp).Set("installer_version", Util.InstallerVersion)
                    .Set("created_utc", Util.UtcStamp(DateTime.UtcNow)).Set("moves", list);
            Util.WriteNew(FreeName(Path.Combine(Dir, "manifest.json"), false), Json.ToBytes(manifest));
            Util.WriteNew(FreeName(Path.Combine(Dir, "RESTORE.txt"), false), Util.Utf8.GetBytes(restore.ToString()));
        }
    }

    public static class StateFile
    {
        public static void Write(GameInstall game, string path, byte[] data)
        {
            string staging = Path.Combine(game.CardsDir, ".staging");
            Directory.CreateDirectory(staging);
            string part = Path.Combine(staging, Path.GetFileName(path) + "." + DateTime.UtcNow.Ticks + ".part");
            Util.WriteNew(part, data);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            if (File.Exists(path)) File.Replace(part, path, null);
            else Util.MoveFile(part, path);
        }
    }

    public sealed class MenuState
    {
        public enum Owner { None, Installer, Foreign }

        readonly GameInstall game;
        public string MenuPakSha1;
        public List<string> Order = new List<string>();
        public bool Paused;
        public List<string> PausedFiles = new List<string>();

        MenuState(GameInstall game) { this.game = game; }

        public string PathOnDisk { get { return Path.Combine(game.CardsDir, "_installer.json"); } }

        public static MenuState Load(GameInstall game)
        {
            MenuState s = new MenuState(game);
            string p = s.PathOnDisk;
            if (!File.Exists(p)) return s;
            try
            {
                JsonObject o = Json.ParseObject(File.ReadAllBytes(p));
                s.MenuPakSha1 = o.Str("menu_pak_sha1");
                object v;
                if (o.TryGetValue("paused", out v) && v is bool) s.Paused = (bool)v;
                if (o.TryGetValue("order", out v) && v is List<object>) foreach (object x in (List<object>)v) if (x is string) s.Order.Add((string)x);
                if (o.TryGetValue("paused_files", out v) && v is List<object>) foreach (object x in (List<object>)v) if (x is string) s.PausedFiles.Add((string)x);
            }
            catch (Exception) { }
            return s;
        }

        public Owner Check()
        {
            string menu = game.MenuPakPath;
            if (!File.Exists(menu)) return Owner.None;
            if (MenuPakSha1 != null && string.Equals(Util.Sha1File(menu), MenuPakSha1, StringComparison.OrdinalIgnoreCase)) return Owner.Installer;
            return Owner.Foreign;
        }

        public void Save()
        {
            JsonOut o = new JsonOut();
            o.Set("format", 1).Set("menu_pak_sha1", MenuPakSha1).Set("order", Order).Set("paused", Paused).Set("paused_files", PausedFiles)
             .Set("installer_version", Util.InstallerVersion);
            StateFile.Write(game, PathOnDisk, Json.ToBytes(o));
        }
    }
}
