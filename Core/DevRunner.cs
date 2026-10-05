// Developer build only: runs the project build script through Git Bash and streams its output.
#if BCMI_DEV
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace BodycamMapInstaller.Core
{
    public static class DevRunner
    {
        public const string OneclickRelPath = @"Saved\multimapa\menu_v2\oneclick.sh";

        public static string FindGitBash()
        {
            try
            {
                using (RegistryKey b = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey k = b.OpenSubKey(@"SOFTWARE\GitForWindows", false))
                {
                    string root = k == null ? null : k.GetValue("InstallPath") as string;
                    if (root != null && File.Exists(Path.Combine(root, @"bin\bash.exe"))) return Path.Combine(root, @"bin\bash.exe");
                }
            }
            catch (Exception) { }
            string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string guess = Path.Combine(pf, @"Git\bin\bash.exe");
            return File.Exists(guess) ? guess : null;
        }

        public static bool IsDevProject(string uprojectPath)
        {
            try
            {
                if (uprojectPath == null || !uprojectPath.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase) || !File.Exists(uprojectPath)) return false;
                return File.Exists(OneclickFor(uprojectPath));
            }
            catch (Exception) { return false; }
        }

        public static string OneclickFor(string uprojectPath)
        {
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(uprojectPath)), OneclickRelPath);
        }

        public static int Run(string bashExe, string script, string arg, string workDir,
                              IDictionary<string, string> extraEnv, Action<string> onLine, Action whileRunning)
        {
            ProcessStartInfo psi = new ProcessStartInfo(bashExe, Quote(script) + " " + Quote(arg));
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            psi.WorkingDirectory = workDir;
            psi.EnvironmentVariables["MSYS_NO_PATHCONV"] = "1";
            if (extraEnv != null) foreach (KeyValuePair<string, string> kv in extraEnv) psi.EnvironmentVariables[kv.Key] = kv.Value;
            ManualResetEvent outDone = new ManualResetEvent(false), errDone = new ManualResetEvent(false);
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data == null) outDone.Set(); else onLine(e.Data); };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data == null) errDone.Set(); else onLine(e.Data); };
                p.Start();
                p.StandardInput.Close();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                int ticks = 0;
                while (!p.WaitForExit(250))
                {
                    ticks++;
                    if (ticks == 3 && whileRunning != null) whileRunning();
                }

                Stopwatch drain = Stopwatch.StartNew();
                outDone.WaitOne(2000);
                errDone.WaitOne((int)Math.Max(0, 2000 - drain.ElapsedMilliseconds));
                int code = p.ExitCode;
                try { p.CancelOutputRead(); p.CancelErrorRead(); } catch (Exception) { }
                return code;
            }
        }

        // Same as Run, for a script that takes several arguments (Scripts/newmap.sh <Id> <Name> <photo> <version>).
        public static int RunArgs(string bashExe, string script, string[] args, string workDir,
                                  IDictionary<string, string> extraEnv, Action<string> onLine)
        {
            StringBuilder sb = new StringBuilder(Quote(script));
            foreach (string a in args) sb.Append(' ').Append(Quote(a));
            ProcessStartInfo psi = new ProcessStartInfo(bashExe, sb.ToString());
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.WindowStyle = ProcessWindowStyle.Hidden;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            psi.WorkingDirectory = workDir;
            psi.EnvironmentVariables["MSYS_NO_PATHCONV"] = "1";
            if (extraEnv != null) foreach (KeyValuePair<string, string> kv in extraEnv) psi.EnvironmentVariables[kv.Key] = kv.Value;
            ManualResetEvent outDone = new ManualResetEvent(false), errDone = new ManualResetEvent(false);
            using (Process p = new Process())
            {
                p.StartInfo = psi;
                p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data == null) outDone.Set(); else onLine(e.Data); };
                p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data == null) errDone.Set(); else onLine(e.Data); };
                p.Start();
                p.StandardInput.Close();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                while (!p.WaitForExit(250)) { }
                outDone.WaitOne(2000);
                errDone.WaitOne(2000);
                int code = p.ExitCode;
                try { p.CancelOutputRead(); p.CancelErrorRead(); } catch (Exception) { }
                return code;
            }
        }

        public static string Explain(int exitCode)
        {
            switch (exitCode)
            {
                case 0: return "every map is installed with its own card.";
                case 1: return "a check stopped one map; its files in Paks were not touched. The lines above say which.";
                case 2: return "the build tools were called the wrong way (read the first line marked ***).";
                case 3: return "something the build needs is missing (read the line marked ***).";
                case 98: return "Bodycam, the editor or another build is busy. Nothing was written; drop the project again later.";
                case 99: return "a check after installing did not pass, and the previous state was put back.";
                default: return "the build tools stopped with code " + exitCode + ". The lines above have the details.";
            }
        }

        public static string Quote(string s)
        {
            return Util.QuoteArg(s);
        }
    }
}
#endif
