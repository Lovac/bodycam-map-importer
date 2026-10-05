// Map maker buttons of the installer window (22 Sep 2026, Lovac: "the ONLY APP ON THE COMPUTER THAT CAN IMPORT MAPS
// ... NEEDS THE BUTTONS and instructions to get the requirements and checks for them").
//
//   click the photo   (both builds) choose any picture: the list shows it at once, and the developer build keeps a
//                     2048x1024 copy so the next COOK puts it in the game's menu too
//   EDIT              (developer build) opens the selected map's level in Unreal Engine 5.5
//   COOK              (developer build) rebuilds the selected map from its level and installs the next version
//   SETUP             (developer build) what this PC needs, OK or MISSING, with a link for each
//
// EDIT and COOK exist only in the developer build: they need Unreal, Git Bash, Python and the map project, which a
// player who downloads a map does not have.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using BodycamMapInstaller.Core;

namespace BodycamMapInstaller.Ui
{
    partial class MainForm
    {
        // ---------------------------------------------------------------------------------------- the picture
        // Kept in the user's own folder, never next to the exe (that may be in Program Files, where writing fails).
        static string PendingDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"BodycamMapInstaller\pending");
        }

        static string PendingPicture(string id)
        {
            return Path.Combine(PendingDir(), id + ".png");
        }

        // Any picture, centre-cropped to the wanted shape and resized. A 16:9 screenshot and a phone photo both work.
        static byte[] CropResizePng(Image src, int w, int h)
        {
            double want = (double)w / h;
            int sw = src.Width, sh = src.Height;
            Rectangle crop;
            if ((double)sw / sh > want)
            {
                int nw = (int)Math.Round(sh * want);
                crop = new Rectangle((sw - nw) / 2, 0, nw, sh);
            }
            else
            {
                int nh = (int)Math.Round(sw / want);
                crop = new Rectangle(0, (sh - nh) / 2, sw, nh);
            }
            using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, w, h), crop, GraphicsUnit.Pixel);
                }
                using (MemoryStream ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }

        void OnChangePicture()
        {
            if (busy) return;
            MapRow row = list.SelectedRow;
            if (row == null || row.Map == null || row.Map.Kind != MapKind.Card || row.Map.CardDir == null)
            {
                log.Info("Only a map with its own card can get a new picture. Older single-file maps show the picture they came with.");
                DrainLog();
                return;
            }
            InstalledMap m = row.Map;
            string file;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Choose a picture for " + row.Name;
                d.Filter = "Pictures (*.png, *.jpg, *.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*";
                d.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                file = d.FileName;
            }
            byte[] thumb, card;
            try
            {
                using (MemoryStream ms = new MemoryStream(File.ReadAllBytes(file)))
                using (Image src = Image.FromStream(ms))
                {
                    if (src.Width < 256 || src.Height < 128)
                    {
                        log.Warn(Path.GetFileName(file) + " is too small (" + src.Width + "x" + src.Height + "). Use a screenshot, at least 1024x512.");
                        DrainLog();
                        return;
                    }
                    thumb = CropResizePng(src, 1024, 512);
                    card = CropResizePng(src, 2048, 1024);
                }
            }
            catch (Exception)
            {
                log.Warn(Path.GetFileName(file) + " is not a picture this installer can read. Use a .png or .jpg file.");
                DrainLog();
                return;
            }

            string id = m.Card != null ? m.Card.Id : Path.GetFileName(m.CardDir);
            string thumbPath = Path.Combine(m.CardDir, "thumb.png");
            try
            {
                Directory.CreateDirectory(PendingDir());
                // the old picture is kept, so a wrong choice can be undone by hand
                if (File.Exists(thumbPath)) File.Copy(thumbPath, Path.Combine(PendingDir(), id + ".previous-thumb.png"), true);
                File.WriteAllBytes(PendingPicture(id), card);
                File.WriteAllBytes(thumbPath, thumb);
            }
            catch (UnauthorizedAccessException)
            {
                log.Block("Windows did not let the installer save the picture. Run the installer as administrator and try again.");
                DrainLog();
                return;
            }
            catch (Exception ex)
            {
                log.Block("The picture could not be saved: " + ex.Message);
                DrainLog();
                return;
            }
#if BCMI_DEV
            log.Good("New picture for " + row.Name + ". It shows in this list now. Press COOK to put it in the game's menu too.");
#else
            log.Good("New picture for " + row.Name + " in this list. The picture inside the game's menu comes with the map and stays as it was.");
#endif
            DrainLog();
            StartScan(false, false);
        }

#if BCMI_DEV
        // ------------------------------------------------------------------------------------------ SETUP
        void ShowRequirements(string why)
        {
            using (RequirementsForm f = new RequirementsForm(game != null, game == null ? null : game.Root, why))
                f.ShowDialog(this);
        }

        // Every check EDIT and COOK need. Shows the SETUP window with the reason when one fails.
        bool MakerReady(string action, out string bash, out string ue, out string sandbox, out string uproject)
        {
            string uever;
            bash = DevRunner.FindGitBash();
            ue = MakerChecks.FindUnreal(out uever);
            sandbox = MakerChecks.FindSandbox(out uproject);
            string py;
            bool pyOk = action == "EDIT" || MakerChecks.CheckPython(out py);
            bool ok = ue != null && sandbox != null && (action == "EDIT" || (bash != null && pyOk && game != null));
            if (!ok)
            {
                log.Warn(action + " needs something this PC does not have yet. The SETUP window shows what, with a link for each.");
                DrainLog();
                ShowRequirements(action + " needs everything below to say OK. Install what says MISSING, then try " + action + " again.");
            }
            return ok;
        }

        static string LevelFile(string sandbox, string levelPackage)
        {
            if (string.IsNullOrEmpty(levelPackage) || !levelPackage.StartsWith("/Game/", StringComparison.Ordinal)) return null;
            return Path.Combine(sandbox, "Content", levelPackage.Substring("/Game/".Length).Replace('/', '\\') + ".umap");
        }

        static string NextVersion(string v)
        {
            string[] p = (v ?? "").Split('.');
            int a, b, c;
            if (p.Length == 3 && int.TryParse(p[0], out a) && int.TryParse(p[1], out b) && int.TryParse(p[2], out c))
                return a + "." + b + "." + (c + 1);
            return "0.1.0";
        }

        // ------------------------------------------------------------------------------------------- EDIT
        void OnEdit()
        {
            MapRow row = list.SelectedRow;
            if (busy || row == null || row.Map == null || row.Map.Card == null) return;
            CardInfo c = row.Map.Card;
            string bash, ue, sandbox, uproject;
            if (!MakerReady("EDIT", out bash, out ue, out sandbox, out uproject)) return;
            if (MakerChecks.IsUnrealRunning())
            {
                log.Warn("Unreal is already open. Use that window, or close it and press EDIT again.");
                DrainLog();
                return;
            }
            string level = LevelFile(sandbox, c.LevelPackage);
            if (level == null || !File.Exists(level))
            {
                log.Block(row.Name + " was built from " + (c.LevelPackage ?? "an unknown level") + ", which is not in this map project. EDIT can only open maps made here.");
                DrainLog();
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(ue, Util.QuoteArg(uproject) + " " + c.LevelPackage) { UseShellExecute = false });
            }
            catch (Exception ex)
            {
                log.Block("Unreal did not start: " + ex.Message);
                DrainLog();
                return;
            }
            log.Info("Unreal 5.5 is opening " + row.Name + " (" + c.LevelPackage + "). The first start takes a few minutes.");
            log.Info("When you are done: save with Ctrl+S, CLOSE Unreal, then press COOK.");
            DrainLog();
        }

        // ------------------------------------------------------------------------------------------- COOK
        void OnCook()
        {
            MapRow row = list.SelectedRow;
            if (busy || row == null || row.Map == null || row.Map.Card == null) return;
            InstalledMap m = row.Map;
            CardInfo c = m.Card;
            string bash, ue, sandbox, uproject;
            if (!MakerReady("COOK", out bash, out ue, out sandbox, out uproject)) return;
            if (GameLocator.IsGameRunning()) { running = true; UpdateChrome(); log.Block("Bodycam is open. Close it, then press COOK again."); DrainLog(); return; }
            if (MakerChecks.IsUnrealRunning()) { log.Block("Unreal is open. Save your level (Ctrl+S), close Unreal, then press COOK again."); DrainLog(); return; }

            string level = LevelFile(sandbox, c.LevelPackage);
            if (level == null || !File.Exists(level))
            {
                log.Block(row.Name + " was built from " + (c.LevelPackage ?? "an unknown level") + ", which is not in this map project, so it cannot be cooked here.");
                DrainLog();
                return;
            }
            string script = Path.Combine(sandbox, MakerChecks.NewMapScript);
            // The game's menu card is 2048x1024. Best picture first: the one just chosen, then the full-size one the
            // last COOK made, and only then the 1024x512 list picture, which would come out soft in the game.
            string lastCook = Path.Combine(sandbox, @"Saved\maps\" + c.Id + @"\thumb\card_2048x1024.png");
            string picture = null;
            bool soft = false;
            if (File.Exists(PendingPicture(c.Id))) picture = PendingPicture(c.Id);
            else if (File.Exists(lastCook)) picture = lastCook;
            else if (File.Exists(Path.Combine(m.CardDir, "thumb.png"))) { picture = Path.Combine(m.CardDir, "thumb.png"); soft = true; }
            if (picture == null)
            {
                log.Block(row.Name + " has no picture. Click its photo first and choose one.");
                DrainLog();
                return;
            }
            string version = NextVersion(c.Version);
            string softNote = soft
                ? "\r\n\r\nNote: there is no full-size picture yet, so the game's menu card will be made from the small list picture and may look soft. Press Cancel and click the photo to choose a full-size screenshot first."
                : "";
            int go = DarkDialog.Show(this, "COOK " + row.Name + "?",
                "This rebuilds " + row.Name + " from its level and installs it as version " + version + ". It takes a few minutes and this window stays busy until it says done. Keep Bodycam and Unreal closed." + softNote,
                "COOK", "Cancel");
            if (go != 0) { log.Info("Nothing changed."); DrainLog(); return; }

            string name = c.DisplayName ?? row.Name;
            string[] args = { c.Id, name, picture.Replace('\\', '/'), version };
            Dictionary<string, string> env = new Dictionary<string, string>();
            env["BCM_LEVEL"] = c.LevelPackage;
            // a rebuild keeps what the card already says; without these the script would write its own defaults
            if (!string.IsNullOrEmpty(c.Description)) env["BCM_DESCRIPTION"] = c.Description;
            if (!string.IsNullOrEmpty(c.WeatherDa)) env["BCM_WEATHER"] = c.WeatherDa;
            log.Info("Cooking " + name + " " + version + " from " + c.LevelPackage + ". Picture: " + Path.GetFileName(picture));
            log.Dim("Git Bash: " + bash + "  |  " + script);
            devRunning = true;
            RunOperation("Cooking " + name + "...", delegate(Installer inst)
            {
                int code = DevRunner.RunArgs(bash, script, args, sandbox, env, delegate(string line)
                {
                    if (line.StartsWith("***")) log.Warn(line); else log.Output(line);
                });
                if (code == 0) log.Good(name + " " + version + " is cooked and installed. Start Bodycam and look in Play > Custom.");
                else log.Block("COOK stopped. The line marked *** above says why; nothing of the installed " + name + " was changed.");
            }, null, null);
        }
#endif
    }
}
