// Settings and log file in %LOCALAPPDATA%\BodycamMapInstaller.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using BodycamMapInstaller.Core;

namespace BodycamMapInstaller.Ui
{
    sealed class AppSettings
    {
        public string GameOverride;
        public Rectangle Bounds = Rectangle.Empty;
        public bool Maximized;
        public readonly List<string> HardpointNoted = new List<string>();
        public bool Disabled;

        public static string Dir
        {
            get
            {
                string over = Environment.GetEnvironmentVariable("BCMI_SETTINGS_DIR");
                if (!string.IsNullOrEmpty(over)) return over;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BodycamMapInstaller");
            }
        }

        static string FilePath { get { return Path.Combine(Dir, "settings.json"); } }

        public static AppSettings Load(bool disabled)
        {
            AppSettings s = new AppSettings();
            s.Disabled = disabled;
            if (disabled) return s;
            try
            {
                if (!File.Exists(FilePath)) return s;
                JsonObject o = Json.ParseObject(File.ReadAllBytes(FilePath));
                s.GameOverride = o.Str("game_folder");
                object v;
                if (o.TryGetValue("window", out v) && v is JsonObject)
                {
                    JsonObject w = (JsonObject)v;
                    int x = Num(w, "x"), y = Num(w, "y"), wd = Num(w, "width"), ht = Num(w, "height");
                    if (wd > 0 && ht > 0) s.Bounds = new Rectangle(x, y, wd, ht);
                    object m;
                    if (w.TryGetValue("maximized", out m) && m is bool) s.Maximized = (bool)m;
                }
                if (o.TryGetValue("hardpoint_note_shown", out v) && v is List<object>)
                    foreach (object x in (List<object>)v) if (x is string) s.HardpointNoted.Add((string)x);
            }
            catch (Exception) { }
            return s;
        }

        static int Num(JsonObject o, string key)
        {
            object v;
            if (o.TryGetValue(key, out v) && v is double) return (int)(double)v;
            return 0;
        }

        public void Save()
        {
            if (Disabled) return;
            try
            {
                Directory.CreateDirectory(Dir);
                JsonOut w = new JsonOut();
                w.Set("x", Bounds.X).Set("y", Bounds.Y).Set("width", Bounds.Width).Set("height", Bounds.Height).Set("maximized", Maximized);
                JsonOut o = new JsonOut();
                o.Set("format", 1).Set("installer_version", Util.InstallerVersion).Set("game_folder", GameOverride).Set("window", w).Set("hardpoint_note_shown", HardpointNoted);
                byte[] data = Json.ToBytes(o);
                string part = FilePath + "." + DateTime.UtcNow.Ticks + ".part";
                using (FileStream fs = new FileStream(part, FileMode.CreateNew, FileAccess.Write)) fs.Write(data, 0, data.Length);
                if (File.Exists(FilePath)) File.Replace(part, FilePath, null);
                else File.Move(part, FilePath);
            }
            catch (Exception) { }
        }
    }

    sealed class FileLog
    {
        readonly object gate = new object();
        readonly bool disabled;
        const long RollBytes = 1024 * 1024;

        public FileLog(bool disabled)
        {
            this.disabled = disabled;
        }

        public string PathOnDisk { get { return Path.Combine(AppSettings.Dir, "installer.log"); } }

        public void Write(string level, string text)
        {
            if (disabled) return;
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(AppSettings.Dir);
                    string p = PathOnDisk;
                    if (File.Exists(p) && new FileInfo(p).Length > RollBytes)
                    {
                        string rolled = Path.Combine(AppSettings.Dir, "installer." + DateTime.Now.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".log");
                        if (!File.Exists(rolled)) File.Move(p, rolled);
                    }
                    string stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                    StringBuilder sb = new StringBuilder();
                    foreach (string line in (text ?? "").Replace("\r\n", "\n").Split('\n'))
                        sb.Append(stamp).Append(' ').Append(level.PadRight(5)).Append(' ').Append(line).Append("\r\n");
                    using (FileStream fs = new FileStream(p, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
                    {
                        byte[] b = Util.Utf8.GetBytes(sb.ToString());
                        fs.Write(b, 0, b.Length);
                    }
                }
                catch (Exception) { }
            }
        }
    }
}
