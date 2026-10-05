// Window checks for --ui-selftest (no game folder needed, no window shown).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using BodycamMapInstaller.Core;

namespace BodycamMapInstaller.Ui
{
    static class UiSelfTest
    {
        public static int Run(string reportPath, TextWriter stdout)
        {
            StreamWriter report;
            try { report = new StreamWriter(new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write), Util.Utf8); }
            catch (IOException) { stdout.WriteLine("--ui-selftest: " + reportPath + " already exists or cannot be written (nothing is overwritten)"); return 2; }
            report.NewLine = "\n";
            int fails = 0, passes = 0;
            using (report)
            {
                Action<string> say = delegate(string line) { report.WriteLine(line); report.Flush(); try { stdout.WriteLine(line); } catch (Exception) { } };
                Action<string, bool, string> check = delegate(string name, bool ok, string detail)
                {
                    if (ok) passes++; else fails++;
                    say((ok ? "PASS " : "FAIL ") + name + "  " + detail);
                };
                say("Bodycam Map Installer ui selftest  v" + Util.InstallerVersion + " (" + MainForm.AppVersion + ")  " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture) + "  scale " + Theme.Scale);

                try
                {
                    int esc, enter, close;
                    string styles = DarkDialog.Probe("Still working", "body", MainForm.StillWorkingDefault, MainForm.StillWorkingCancel, -1, MainForm.StillWorkingButtons, out esc, out enter, out close);

                    check("dialog.still_working", esc != 0 && enter != 0 && close != 0 && MainForm.StillWorkingButtons[0] == "Close anyway",
                        "buttons [" + styles + "]; Escape -> " + MainForm.StillWorkingButtons[esc] + ", Enter -> " + MainForm.StillWorkingButtons[enter] + ", close box -> " + MainForm.StillWorkingButtons[close] + " (the window closes only on 'Close anyway')");
                    int d, c, x;
                    MainForm.AskStyle("Replace", out d, out c, out x);
                    string[] rb = { "Replace", "Cancel" };
                    styles = DarkDialog.Probe("Replace the map list?", "body", d, c, x, rb, out esc, out enter, out close);
                    check("dialog.replace_map_list", rb[esc] == "Cancel" && rb[enter] == "Cancel" && rb[close] == "Cancel",
                        "buttons [" + styles + "]; Escape -> " + rb[esc] + ", Enter -> " + rb[enter] + ", close box -> " + rb[close]);
                    MainForm.AskStyle("Remove", out d, out c, out x);
                    string[] mb = { "Remove", "Cancel" };
                    styles = DarkDialog.Probe("Remove Tower Test?", "body", d, c, x, mb, out esc, out enter, out close);
                    check("dialog.remove", mb[esc] == "Cancel" && mb[close] == "Cancel" && styles.Contains("Remove (red text)") && !styles.Contains("Remove (default, green)"),
                        "buttons [" + styles + "]; Escape -> " + mb[esc] + ", Enter -> " + mb[enter] + ", close box -> " + mb[close]);
                }
                catch (Exception ex) { check("dialogs", false, ex.ToString()); }

                try
                {
                    StartOptions o = new StartOptions();
                    o.NoSettings = true;
                    using (MainForm f = new MainForm(o))
                    {
                        Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
                        bool fits = f.Height <= wa.Height && f.Width <= wa.Width;
                        check("window.fits_the_screen", fits, "outside " + f.Width + "x" + f.Height + ", client " + f.ClientSize.Width + "x" + f.ClientSize.Height + ", working area " + wa.Width + "x" + wa.Height + ", minimum " + f.MinimumSize.Width + "x" + f.MinimumSize.Height + " (design 960x660 at " + Theme.Scale + " = " + Theme.Px(960) + "x" + Theme.Px(660) + " client)");
                    }
                }
                catch (Exception ex) { check("window.fits_the_screen", false, ex.ToString()); }

                try
                {
                    string tmp = Path.Combine(Path.GetTempPath(), "bcmi_ui_selftest_folder_" + DateTime.UtcNow.Ticks);
                    Directory.CreateDirectory(tmp);
                    string[] yes = { "Map.zip", "Map.BCMAP", "zMap_9_P.pak", tmp };
                    string[] no = { "photo.jpg", "notes.txt", "BodycamMapInstaller.exe", "Game.uproject", "archive.7z" };
                    List<string> wrong = new List<string>();
                    bool project;
                    foreach (string f in yes) if (!MainForm.IsMapFileName(f, out project)) wrong.Add(f + " refused");
                    foreach (string f in no)
                    {
                        bool ok = MainForm.IsMapFileName(f, out project);
#if BCMI_DEV
                        if (f.EndsWith(".uproject")) { if (!ok || !project) wrong.Add(f + " not taken as the dev project"); continue; }
#endif
                        if (ok) wrong.Add(f + " accepted");
                    }
                    check("drop.accepts_map_files_only", wrong.Count == 0, wrong.Count == 0 ? "accepted: .zip .bcmap .pak and a folder; refused: " + string.Join(" ", no) : string.Join("; ", wrong.ToArray()));
                    using (DropZone z = new DropZone())
                    {
                        z.SetHotRefused();
                        bool refusedHot = z.HotRefused && !z.Hot;
                        z.SetHot(false, null);
                        z.ShowResult(true, "Tower Test installed", "Start Bodycam > Play > Custom > Team Deathmatch");
                        bool done = z.State == DropState.Done && z.ResultHead == "Tower Test installed";
                        z.ShowResult(false, "Not installed", "notes.txt" + Installer.NoMapInside);
                        bool refused = z.State == DropState.Refused && z.ResultSub.Contains("has no map inside");
                        check("drop.answers", refusedHot && done && refused && DropZone.ReadySub == "The map you downloaded (.zip, .bcmap or .pak)",
                            "dragging a non-map file: red 'Not a map file'; after an install: Done 'Tower Test installed'; after a refusal: Refused with the reason; subtitle '" + DropZone.ReadySub + "'");
                    }
                }
                catch (Exception ex) { check("drop", false, ex.ToString()); }

                try
                {
                    using (ActivityView a = new ActivityView())
                    {
                        a.Add("Installed Tower Test 0.3.0.", Theme.Green, DateTime.Now, false);
                        a.Add("Copied zTowerTest_9_P.pak", Theme.TextDim, DateTime.Now, true);
                        a.Add("Moved zzMapCards_19_P.pak to the backup folder (20260916_051425).", Theme.TextDim, DateTime.Now, true);
                        bool ok = a.Count == 1 && a.AllText().Contains("Copied zTowerTest_9_P.pak") && !a.ShownText().Contains("Copied") && !a.ShownText().Contains("Moved");
                        PropertyInfo lineH = typeof(ActivityView).GetProperty("LineH", BindingFlags.Instance | BindingFlags.NonPublic);
                        int lh = lineH == null ? -1 : (int)lineH.GetValue(a, null);
                        bool font = lh == Theme.Small.Height + Theme.Px(4) && Theme.Small.Name == "Segoe UI";
                        check("activity.plain_lines", ok && font, "drawn " + a.Count + " of 3 lines (the 2 file lines only in Copy all and installer.log); font " + Theme.Small.Name + " " + Theme.Small.SizeInPoints + " pt, line " + lh + " px");
                    }
                }
                catch (Exception ex) { check("activity", false, ex.ToString()); }

                try
                {
                    string inUse = Util.PlainReason(new IOException("The process cannot access the file 'C:\\x' because it is being used by another process.", unchecked((int)0x80070020)));
                    string full = Util.PlainReason(new IOException("There is not enough space on the disk.", unchecked((int)0x80070070)));
                    string tooLong = Util.PlainReason(new PathTooLongException("long"));
                    string unknown = Util.PlainReason(new InvalidOperationException("internal wording"));
                    check("words.plain_errors", inUse != null && inUse.StartsWith("A map file is in use") && full == "The disk with Bodycam is full." && tooLong != null && unknown == null,
                        "in use: '" + inUse + "'; disk full: '" + full + "'; too long: '" + tooLong + "'; unknown: none (the window says 'Details are in installer.log')");
                }
                catch (Exception ex) { check("words", false, ex.ToString()); }

                Type dev = typeof(Installer).Assembly.GetType("BodycamMapInstaller.Core.DevRunner", false);
#if BCMI_DEV
                check("flavour.dev_build", dev != null, "dev build: DevRunner present, a dropped .uproject runs the project's build script");
#else
                check("flavour.player_build", dev == null, "player build: no DevRunner type, a dropped .uproject is refused like any other non-map file");
#endif

                say("summary: " + passes + " PASS, " + fails + " FAIL");
                say(fails == 0 ? "RESULT PASS" : "RESULT FAIL " + fails);
            }
            return fails == 0 ? 0 : 1;
        }
    }
}
