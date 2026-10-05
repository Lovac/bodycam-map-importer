// Developer build only: what a map maker's PC needs for EDIT and COOK, each with a plain check and a link.
// 22 Sep 2026 (Lovac: "instructions to get the requirements and checks for them so any idiot noob knows how to
// use ... with checks and links to all of the requirements in the app").
#if BCMI_DEV
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace BodycamMapInstaller.Core
{
    public sealed class Requirement
    {
        public string Name;       // what it is, in a few words
        public bool Ok;
        public string Found;      // what was found, or why it is missing
        public string Fix;        // one sentence: what to do
        public string Link;       // where to get it (null when there is nothing to download)
    }

    public static class MakerChecks
    {
        public const string UnrealLink = "https://www.unrealengine.com/en-US/download";
        public const string GitLink = "https://gitforwindows.org/";
        public const string PythonLink = "https://www.python.org/downloads/";
        public const string BodycamLink = "https://store.steampowered.com/app/2406770/";
        public const string NewMapScript = @"Scripts\newmap.sh";
        public const string ReadmeRel = @"Saved\release\MAP_MAKERS_README.txt";

        // ------------------------------------------------------------------------------------------ Unreal 5.5
        public static string FindUnreal(out string version)
        {
            version = null;
            string root = null;
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(@"SOFTWARE\EpicGames\Unreal Engine\5.5", false))
                    if (k != null) root = k.GetValue("InstalledDirectory") as string;
            }
            catch (Exception) { }
            if (root == null)
                root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Epic Games\UE_5.5");
            string exe = Path.Combine(root, @"Engine\Binaries\Win64\UnrealEditor.exe");
            if (!File.Exists(exe)) return null;
            version = ReadBuildVersion(Path.Combine(root, @"Engine\Build\Build.version"));
            return exe;
        }

        static string ReadBuildVersion(string path)
        {
            try
            {
                string s = File.ReadAllText(path);
                return Field(s, "MajorVersion") + "." + Field(s, "MinorVersion") + "." + Field(s, "PatchVersion");
            }
            catch (Exception) { return "5.5"; }
        }

        static string Field(string json, string key)
        {
            int i = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (i < 0) return "?";
            i = json.IndexOf(':', i) + 1;
            StringBuilder sb = new StringBuilder();
            while (i < json.Length && (char.IsWhiteSpace(json[i]) || char.IsDigit(json[i])))
            {
                if (char.IsDigit(json[i])) sb.Append(json[i]);
                i++;
            }
            return sb.Length == 0 ? "?" : sb.ToString();
        }

        // ------------------------------------------------------------------------- the BodycamSandbox project
        // Walks up from the exe to the folder that holds a .uproject AND Scripts\newmap.sh, so the tool works from
        // Saved\release\app\dev as well as from a copy placed anywhere inside the project.
        public static string FindSandbox(out string uproject)
        {
            uproject = null;
            try
            {
                string dir = AppDomain.CurrentDomain.BaseDirectory;
                for (int up = 0; up < 8 && !string.IsNullOrEmpty(dir); up++)
                {
                    if (File.Exists(Path.Combine(dir, NewMapScript)))
                        foreach (string f in Directory.GetFiles(dir, "*.uproject"))
                        {
                            uproject = f;
                            return dir;
                        }
                    DirectoryInfo parent = Directory.GetParent(dir.TrimEnd('\\', '/'));
                    dir = parent == null ? null : parent.FullName;
                }
            }
            catch (Exception) { }
            return null;
        }

        // ------------------------------------------------------------------------------------- Python + Pillow
        // Calls python.exe directly, the same one the cook finds on PATH. A fresh Windows has a Microsoft Store
        // "python" shortcut that prints nothing: that counts as missing, which is the right answer.
        public static bool CheckPython(out string detail)
        {
            detail = null;
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("python", "-c \"import sys;print(sys.version.split()[0]);import PIL;print(PIL.__version__)\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                using (Process p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    if (!p.WaitForExit(20000)) { try { p.Kill(); } catch (Exception) { } detail = "python did not answer"; return false; }
                    string[] lines = o.Replace("\r", "").Trim().Split('\n');
                    if (p.ExitCode == 0 && lines.Length >= 2) { detail = "Python " + lines[0].Trim() + ", Pillow " + lines[1].Trim(); return true; }
                    if (lines.Length >= 1 && lines[0].Trim().Length > 0 && e.IndexOf("PIL", StringComparison.Ordinal) >= 0)
                    { detail = "Python " + lines[0].Trim() + " is there, but Pillow is not"; return false; }
                    detail = "python did not run";
                    return false;
                }
            }
            catch (Exception) { detail = "python is not on this PC"; return false; }
        }

        public static bool IsUnrealRunning()
        {
            foreach (string n in new string[] { "UnrealEditor", "UnrealEditor-Cmd" })
                if (Process.GetProcessesByName(n).Length > 0) return true;
            return false;
        }

        // ------------------------------------------------------------------------------------------ everything
        public static List<Requirement> CheckAll(bool bodycamFound, string bodycamPath)
        {
            List<Requirement> list = new List<Requirement>();

            Requirement r = new Requirement();
            r.Name = "Bodycam";
            r.Ok = bodycamFound;
            r.Found = bodycamFound ? bodycamPath : "not found";
            r.Fix = "Install Bodycam from Steam, or click Browse... in the main window and pick its folder.";
            r.Link = BodycamLink;
            list.Add(r);

            string uever;
            string ue = FindUnreal(out uever);
            r = new Requirement();
            r.Name = "Unreal Engine 5.5";
            r.Ok = ue != null;
            r.Found = ue != null ? "version " + uever + "  (" + ue + ")" : "not installed";
            r.Fix = "Install the Epic Games Launcher, then Unreal Engine > Library > the + button > choose 5.5 (NOT a newer one: Bodycam is built on 5.5).";
            r.Link = UnrealLink;
            list.Add(r);

            string bash = DevRunner.FindGitBash();
            r = new Requirement();
            r.Name = "Git for Windows (Git Bash)";
            r.Ok = bash != null;
            r.Found = bash != null ? bash : "not installed";
            r.Fix = "Install Git for Windows with the default options. COOK runs its build scripts through Git Bash.";
            r.Link = GitLink;
            list.Add(r);

            string py;
            bool pyOk = CheckPython(out py);
            r = new Requirement();
            r.Name = "Python 3 with Pillow";
            r.Ok = pyOk;
            r.Found = py;
            r.Fix = "Install Python 3 and TICK \"Add python.exe to PATH\" in the first installer screen. Then open a command window and run:  py -m pip install Pillow";
            r.Link = PythonLink;
            list.Add(r);

            string uproject;
            string sandbox = FindSandbox(out uproject);
            r = new Requirement();
            r.Name = "The map project (BodycamSandbox)";
            r.Ok = sandbox != null;
            r.Found = sandbox != null ? uproject : "this installer is not inside a map project";
            r.Fix = "Run this developer installer from inside the project folder (Saved\\release\\app\\dev). The project holds the levels and the build scripts.";
            r.Link = null;
            list.Add(r);

            return list;
        }

        public static bool AllOk(List<Requirement> list)
        {
            foreach (Requirement r in list) if (!r.Ok) return false;
            return true;
        }
    }
}
#endif
