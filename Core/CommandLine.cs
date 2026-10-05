// Command-line jobs: --selftest, --validate, --list, --install, --remove, --pause, --resume.
using System;
using System.Collections.Generic;
using System.IO;

namespace BodycamMapInstaller.Core
{
    public static class CommandLine
    {
        static readonly string[] ValueFlags = { "--selftest", "--validate", "--install", "--remove", "--game", "--fixtures", "--legacy", "--report", "--pakpy", "--unrealpak", "--bash", "--python" };
        static readonly string[] BoolFlags = { "--list", "--pause", "--resume", "--yes" };
        static readonly string[] Verbs = { "--selftest", "--validate", "--list", "--install", "--remove", "--pause", "--resume" };

        public static bool IsCommand(string[] args)
        {
            if (args == null) return false;
            foreach (string a in args) if (Array.IndexOf(Verbs, a) >= 0) return true;
            return false;
        }

        public static int Run(string[] args, TextWriter stdout)
        {
            return Run(args, stdout, null);
        }

        public static int Run(string[] args, TextWriter stdout, Func<bool> isRunning)
        {
            Dictionary<string, string> opt = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> flags = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                if (Array.IndexOf(ValueFlags, a) >= 0)
                {
                    if (i + 1 >= args.Length) return Usage(stdout, a + " needs a value");
                    opt[a] = args[++i];
                }
                else if (Array.IndexOf(BoolFlags, a) >= 0) flags.Add(a);
                else return Usage(stdout, "unknown argument " + a);
            }
            List<string> verbs = new List<string>();
            foreach (string v in Verbs) if (opt.ContainsKey(v) || flags.Contains(v)) verbs.Add(v);
            if (verbs.Count != 1) return Usage(stdout, verbs.Count == 0 ? "no job given" : "one job at a time");
            string verb = verbs[0];

            if (verb == "--selftest")
            {
                SelfTestOptions so = new SelfTestOptions();
                so.FixturesDir = Get(opt, "--fixtures");
                so.LegacyPak = Get(opt, "--legacy");
                so.ReportPath = Get(opt, "--report");
                so.PakPy = Get(opt, "--pakpy");
                so.UnrealPak = Get(opt, "--unrealpak");
                so.BashExe = Get(opt, "--bash");
                so.Python = Get(opt, "--python");
                return SelfTest.Run(opt["--selftest"], so, new TextLog(stdout));
            }

            StreamWriter report = null;
            try
            {
                if (opt.ContainsKey("--report"))
                {
                    try { report = new StreamWriter(new FileStream(opt["--report"], FileMode.CreateNew, FileAccess.Write), Util.Utf8); report.NewLine = "\n"; }
                    catch (IOException) { return Usage(stdout, "--report file already exists or cannot be written"); }
                }
                TextLog log = new TextLog(stdout, report);
                log.Info("Bodycam Map Installer v" + Util.InstallerVersion);
                bool yes = flags.Contains("--yes");
                AskUser ask = delegate(string title, string body, string yesText, string noText)
                {
                    if (yes) { log.Info("(--yes) " + title + " -> " + yesText); return true; }
                    log.Warn(title + " " + body + " Add --yes to answer " + yesText + ".");
                    return false;
                };

                GameInstall game = null;
                if (opt.ContainsKey("--game"))
                {
                    game = GameLocator.FromUserPath(opt["--game"]);
                    if (game == null) { log.Block("That is not the Bodycam folder. Pick the folder that holds Bodycam.exe, or its Bodycam\\Content\\Paks folder."); return Installer.ExitNoGame; }
                    log.Info("Found Bodycam at " + game.Root + " (" + game.Source + ")");
                }

                if (verb == "--validate")
                {
                    CardPackage pkg;
                    try { pkg = CardPackage.Open(opt["--validate"]); }
                    catch (PackageException ex) { log.Block(ex.Message); return Installer.ExitRefused; }
                    using (pkg)
                    {
                        InstallContext ctx = null;
                        if (game != null)
                        {
                            Installer inst0 = new Installer(game, null, new NullLog(), ask);
                            ctx = inst0.Context();
                        }
                        ValidationReport rep = pkg.Validate(ctx);
                        foreach (string p in rep.Problems) log.Block(p);
                        foreach (string n in rep.Notes) log.Warn(n);
                        log.Info(pkg.SourceName + (rep.Ok ? " is a valid map package" : " was refused") + ": " + Util.Plural(rep.Problems.Count, "problem", "problems") + ", " + Util.Plural(rep.Notes.Count, "note", "notes") + (ctx == null ? " (checked on its own, no game folder)." : "."));
                        return rep.Ok ? Installer.ExitOk : Installer.ExitRefused;
                    }
                }

                if (game == null)
                {
                    game = GameLocator.FindSteam(log);
                    if (game == null) { log.Block("Bodycam not found. Pass --game <folder>."); return Installer.ExitNoGame; }
                }
                Installer inst = new Installer(game, null, log, ask);
                if (isRunning != null) inst.IsGameRunning = isRunning;
                try
                {
                    inst.RecoverStaleFiles();
                    switch (verb)
                    {
                        case "--list":
                            {
                                int n = 0;
                                foreach (InstalledMap m in inst.ListInstalled())
                                {
                                    n++;
                                    string source = m.Kind == MapKind.Card ? "card" + (m.Source != null ? " (" + m.Source + ")" : "") : m.Kind == MapKind.OlderPak ? "single file (" + m.Source + ")" : "other mod (" + m.Source + ")";
                                    stdout.WriteLine(m.Id + "\t" + (m.Version ?? "-") + "\t" + (m.Modes != null ? string.Join(",", m.Modes) : "-") + "\t" + source + "\t" + (m.Problems.Count == 0 ? "-" : string.Join("; ", m.Problems.ToArray())) + (m.Paused ? "\tpaused" : ""));
                                    if (report != null) report.WriteLine(m.Id + "\t" + (m.Version ?? "-"));
                                }
                                log.Info(n == 0 ? "No custom maps installed yet." : Util.Plural(n, "entry", "entries") + ".");
                                return Installer.ExitOk;
                            }
                        case "--install": return inst.InstallDropped(opt["--install"]).ExitCode;
                        case "--remove": return inst.Remove(opt["--remove"]).ExitCode;
                        case "--pause":
                            if (!ask("Hide custom maps?", "The game will show only its own maps, for public or Casual matches. Your maps move to a side folder and come back with Show custom maps again.", "Hide", "Cancel")) return Installer.ExitRefused;
                            return inst.Pause().ExitCode;
                        case "--resume": return inst.Resume().ExitCode;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    log.Block("Windows did not let the installer write in the Bodycam folder. Run the installer as administrator to continue.");
                    return Installer.ExitRefused;
                }
                return Usage(stdout, "no job given");
            }
            finally
            {
                if (report != null) report.Dispose();
            }
        }

        static string Get(Dictionary<string, string> opt, string key)
        {
            string v;
            return opt.TryGetValue(key, out v) ? v : null;
        }

        static int Usage(TextWriter stdout, string why)
        {
            try
            {
                stdout.WriteLine("Bodycam Map Installer v" + Util.InstallerVersion + ": " + why);
                stdout.WriteLine("  --selftest <new folder under %TEMP%> [--fixtures <dir>] [--legacy <pak>] [--report <txt>]");
                stdout.WriteLine("  --validate <package> [--game <dir>]");
                stdout.WriteLine("  [--game <dir>] --list | --install <file> [--yes] | --remove <Id or pak> [--yes] | --pause [--yes] | --resume");
            }
            catch (Exception) { }
            return 2;
        }
    }
}
