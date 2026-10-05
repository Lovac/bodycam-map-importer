// Screenshot mode: renders the window to a PNG without showing it (used by tests).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using BodycamMapInstaller.Core;

namespace BodycamMapInstaller.Ui
{
    static class ShotRenderer
    {
        public static readonly string[] States = { "ready", "empty", "locked", "nogame", "busy", "paused", "foreign", "hot", "hot-refused", "done", "refused", "dialog-remove", "dialog-build" };

        public static void CreateHidden(Form f)
        {
            MethodInfo cc = typeof(Control).GetMethod("CreateControl", BindingFlags.Instance | BindingFlags.NonPublic, null, new Type[] { typeof(bool) }, null);
            bool created = false;
            if (cc != null)
            {
                try { cc.Invoke(f, new object[] { true }); created = f.IsHandleCreated; } catch (Exception) { created = false; }
            }
            if (!created)
            {
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-20000, -20000);
                f.ShowInTaskbar = false;
                f.Show();
            }
        }

        public static bool Save(Form f, string png)
        {
            f.PerformLayout();
            Application.DoEvents();
            using (Bitmap whole = new Bitmap(f.Width, f.Height, PixelFormat.Format32bppArgb))
            {
                f.DrawToBitmap(whole, new Rectangle(0, 0, f.Width, f.Height));
                Rectangle client = f.RectangleToScreen(f.ClientRectangle);
                Rectangle crop = new Rectangle(client.X - f.Bounds.X, client.Y - f.Bounds.Y, f.ClientSize.Width, f.ClientSize.Height);
                if (crop.X < 0 || crop.Y < 0 || crop.Right > whole.Width || crop.Bottom > whole.Height) crop = new Rectangle(0, 0, whole.Width, whole.Height);
                using (Bitmap outBmp = whole.Clone(crop, PixelFormat.Format24bppRgb))
                {
                    string dir = Path.GetDirectoryName(Path.GetFullPath(png));
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                    using (FileStream fs = new FileStream(png, FileMode.CreateNew, FileAccess.Write)) outBmp.Save(fs, ImageFormat.Png);
                }
            }
            if (f.Visible) f.Close();
            return File.Exists(png);
        }
    }

    partial class MainForm
    {
        public int RunScreenshot(TextWriter stdout)
        {
            string png = opt.ScreenshotPng;
            try
            {
                if (File.Exists(png)) { stdout.WriteLine("--screenshot: " + png + " already exists (nothing is overwritten)"); return 1; }
                string state = opt.ScreenshotState == null ? null : opt.ScreenshotState.ToLowerInvariant();
                if (state != null && Array.IndexOf(ShotRenderer.States, state) < 0)
                {
                    stdout.WriteLine("--screenshot-state " + state + " is not known; known: " + string.Join(" ", ShotRenderer.States));
                    return 2;
                }
                if (state != null && state.StartsWith("dialog-"))
                {
                    bool written = DarkDialog.Render(png, state);
                    stdout.WriteLine((written ? "wrote " : "did not write ") + png + " (made-up state " + state + ")");
                    return written ? 0 : 1;
                }
                ShotRenderer.CreateHidden(this);
                if (state != null) ApplyMadeUpState(state);
                else
                {
                    ScanResult r = Scan(null, true);
                    ApplyScan(r, true);
                    foreach (string f in opt.ScreenshotDrops) OnFiles(new string[] { f });
                }
                DrainLog();
                bool ok = ShotRenderer.Save(this, png);
                stdout.WriteLine((ok ? "wrote " : "did not write ") + png + " (" + ClientSize.Width + "x" + ClientSize.Height + ", " + (state == null ? "start-up scan" + (opt.GameArg != null ? " of " + opt.GameArg : "") + ", " + opt.ScreenshotDrops.Count + " drops" : "made-up state " + state) + ")");
                foreach (string line in activity.AllText().Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries)) stdout.WriteLine("  activity | " + line);
                stdout.WriteLine("  status   | " + gamePath.Text + " | " + gameState.Text.Replace(Theme.Dot, "|"));
                stdout.WriteLine("  drop box | " + drop.State + (drop.State == DropState.Done || drop.State == DropState.Refused ? " | " + drop.ResultHead + " | " + drop.ResultSub : ""));
                List<string> rowText = new List<string>();
                foreach (MapRow row in list.Rows) rowText.Add(row.Name + " [" + row.Right + "] " + row.Modes);
                stdout.WriteLine("  list     | " + (rowText.Count == 0 ? "(empty)" : string.Join(" || ", rowText.ToArray())) + (list.SelectedRow != null ? "   selected: " + list.SelectedRow.Name : ""));
                stdout.WriteLine("  shown    | " + activity.Count + " Activity lines drawn; window " + Width + "x" + Height + " outside");
                return ok ? 0 : 1;
            }
            catch (Exception ex)
            {
                try { stdout.WriteLine("--screenshot stopped: " + ex); } catch (Exception) { }
                return 1;
            }
        }

        static InstalledMap MadeUpCard(string id, string name, string version, string description, string source, string[] modes)
        {
            InstalledMap m = new InstalledMap();
            m.Kind = MapKind.Card;
            m.Id = id; m.DisplayName = name; m.Version = version; m.Author = "Lovac"; m.Description = description; m.Source = source; m.Modes = modes;
            return m;
        }

        void ApplyMadeUpState(string state)
        {
            string root = @"C:\Program Files (x86)\Steam\steamapps\common\Bodycam";
            GameInstall g = new GameInstall();
            g.Root = root;
            g.PaksDir = root + @"\Bodycam\Content\Paks";
            g.ExePath = root + @"\Bodycam\Binaries\Win64\Bodycam-Win64-Shipping.exe";
            g.CardsDir = g.PaksDir + @"\_cards";
            g.BackupRoot = g.PaksDir + @"\_backup";
            g.PausedDir = g.PaksDir + @"\_paused";
            g.Source = "Steam";

            ScanResult r = new ScanResult();
            r.Base = new BaseCheck();
            r.Base.Result = BaseCheck.Status.Match;
            r.Owner = MenuState.Owner.Installer;
            if (state == "nogame")
            {
                detecting = false;
                log.Warn("Looked in every Steam library on this PC: Bodycam is not there.");
                ApplyScan(r, false);
                return;
            }
            r.Game = g;
            InstalledMap prison = MadeUpCard("Prison", "Panopticon Prison", "1.0.0", "Custom map: the panopticon", "PanopticonPrison-1.0.0.zip", new string[] { "TDM", "DM", "HP" });
            InstalledMap tower = MadeUpCard("TowerTest", "Tower Test", "0.3.0", "Custom map: the tower", "TowerTest-0.3.0.zip", new string[] { "TDM", "DM" });
            if (opt.ThumbPng != null && File.Exists(opt.ThumbPng))
            {
                Image img = LoadThumb(opt.ThumbPng);
                if (img != null) r.Thumbs["Card:Prison"] = img;
            }
            List<InstalledMap> maps = new List<InstalledMap>();
            if (state != "empty") { maps.Add(prison); maps.Add(tower); }
            if (state == "foreign")
            {
                r.Owner = MenuState.Owner.Foreign;
                InstalledMap older = new InstalledMap();
                older.Kind = MapKind.OlderPak; older.Id = "PanopticonPrison_P.pak"; older.DisplayName = PackagePaths.FriendlyPakName("PanopticonPrison_P.pak"); older.Source = "PanopticonPrison_P.pak"; older.Modes = new string[] { "HP", "TDM", "DM" };
                InstalledMap other = new InstalledMap();
                other.Kind = MapKind.OtherMod; other.Id = "WeaponSounds_P.pak"; other.DisplayName = "WeaponSounds_P"; other.Source = "WeaponSounds_P.pak"; other.Modes = new string[0];
                maps.Clear();
                maps.Add(tower); maps.Add(older); maps.Add(other);
            }
            if (state == "paused") { r.Paused = true; foreach (InstalledMap m in maps) m.Paused = true; }
            r.Maps = maps;
            r.Running = state == "locked";
            detecting = false;
            log.Detail("Found Bodycam at " + root);
            ApplyScan(r, true);
            if (state == "ready" || state == "hot" || state == "done")
            {
                log.Info("Checking TowerTest-0.3.0.zip...");
                log.Info("Installing Tower Test 0.3.0 (Team Deathmatch, Deathmatch)...");
                log.Detail("Copied zTowerTest_9_P.pak");
                log.Dim("Kept a copy of the old map list in Backups.");
                log.Info("2 maps now show in Play > Custom.");
                log.Good("Installed Tower Test 0.3.0. Start Bodycam and look in Play > Custom > Team Deathmatch.");
                log.Dim("Playing with friends: everyone needs the same maps, same versions.");
            }
            if (state == "done")
            {
                list.Selected = 1;
                ShowDropResult(true, "Tower Test installed", "Start Bodycam > Play > Custom > Team Deathmatch");
                UpdateChrome();
            }
            if (state == "refused")
            {
                log.Info("Checking holiday_photos.zip...");
                log.Block("holiday_photos.zip" + Installer.NoMapInside);
                ShowDropResult(false, "Not installed", "holiday_photos.zip" + Installer.NoMapInside);
                UpdateChrome();
            }
            if (state == "busy")
            {
                busy = true;
                busyStart = DateTime.Now;
                busyStatus = "Installing Tower Test 0.3.0...   step 2 of 6";
                log.Info("Checking TowerTest-0.3.0.zip...");
                log.Info("Installing Tower Test 0.3.0 (Team Deathmatch, Deathmatch)...");
                log.Detail("Copied zTowerTest_9_P.pak");
                log.Progress(2, 6, "Installing Tower Test 0.3.0...");
                UpdateChrome();
                drop.Progress = log.ProgressFraction;
            }
            if (state == "hot") drop.SetHot(true, "Let go to install");
            if (state == "hot-refused") drop.SetHotRefused();
        }
    }
}
