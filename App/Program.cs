// Entry point: runs a command-line job, or opens the window.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using BodycamMapInstaller.Core;
using BodycamMapInstaller.Ui;

namespace BodycamMapInstaller
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll")]
        static extern bool AttachConsole(int processId);

        [DllImport("kernel32.dll")]
        static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("kernel32.dll")]
        static extern int GetFileType(IntPtr handle);

        static void BorrowConsoleIfNeeded()
        {
            try
            {
                IntPtr h = GetStdHandle(-11);
                if (h == IntPtr.Zero || h == new IntPtr(-1) || GetFileType(h) == 0) AttachConsole(-1);
            }
            catch (Exception) { }
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (CommandLine.IsCommand(args))
            {
                BorrowConsoleIfNeeded();
                TextWriter stdout = Console.Out;
                int code;
                try { code = CommandLine.Run(args, stdout); }
                catch (Exception ex)
                {
                    try { stdout.WriteLine("stopped: " + ex.Message); } catch (Exception) { }
                    code = 1;
                }
                try { stdout.Flush(); } catch (Exception) { }
                return code;
            }

            StartOptions opt = new StartOptions();
            bool fake = false;
            string uiSelftest = null;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                bool hasNext = i + 1 < args.Length;
                if (a == "--screenshot" && hasNext) opt.ScreenshotPng = args[++i];
                else if ((a == "--screenshot-state" || a == "--state") && hasNext) opt.ScreenshotState = args[++i];
                else if (a == "--fake") fake = true;
                else if (a == "--thumb" && hasNext) opt.ThumbPng = args[++i];
                else if (a == "--screenshot-drop" && hasNext) opt.ScreenshotDrops.Add(args[++i]);
                else if (a == "--game" && hasNext) opt.GameArg = args[++i];
                else if (a == "--screenshot-show-root" && hasNext) opt.ShowRoot = args[++i];
                else if (a == "--ui-selftest" && hasNext) uiSelftest = args[++i];
                else if (a == "--no-settings") opt.NoSettings = true;
                else if (a.StartsWith("--")) return Usage("unknown or incomplete argument " + a);
                else opt.OpenFiles.Add(a);
            }
            if (fake && opt.ScreenshotState == null) opt.ScreenshotState = "ready";
            if (!opt.Screenshot && (opt.ScreenshotState != null || opt.ScreenshotDrops.Count > 0 || opt.ThumbPng != null))
                return Usage("--screenshot-state, --screenshot-drop and --thumb need --screenshot <png>");
            if (opt.Screenshot && opt.ScreenshotState != null && (opt.GameArg != null || opt.ScreenshotDrops.Count > 0 || opt.ShowRoot != null))
                return Usage("--screenshot-state draws made-up data; it does not take --game, --screenshot-drop or --screenshot-show-root");
            if (opt.ShowRoot != null && !opt.Screenshot) return Usage("--screenshot-show-root needs --screenshot <png>");
            if (opt.Screenshot && opt.OpenFiles.Count > 0) return Usage("a screenshot run takes files only through --screenshot-drop");

            if (opt.Screenshot && opt.ScreenshotState == null)
            {
                if (opt.GameArg == null) return Usage("--screenshot without --screenshot-state needs --game <a fake game made by --selftest, under TEMP>; nothing was written");
                GameInstall g = GameLocator.FromUserPath(opt.GameArg);
                bool underTemp = false;
                try { underTemp = g != null && Util.IsUnder(g.Root, Path.GetTempPath()); } catch (Exception) { }
                if (g == null || !g.IsSelftestFake || !underTemp)
                    return Usage("--screenshot --game " + opt.GameArg + " is not a fake game under TEMP (marker " + GameInstall.SelftestMarker + " and a 0-byte Bodycam-Win64-Shipping.exe); nothing was written");
            }
            if (uiSelftest != null && (opt.Screenshot || opt.GameArg != null || opt.OpenFiles.Count > 0)) return Usage("--ui-selftest runs alone");

            try { SetProcessDPIAware(); } catch (Exception) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            if (uiSelftest != null)
            {
                BorrowConsoleIfNeeded();
                TextWriter stdout = Console.Out;
                int code = UiSelfTest.Run(uiSelftest, stdout);
                try { stdout.Flush(); } catch (Exception) { }
                return code;
            }

            if (opt.Screenshot)
            {
                BorrowConsoleIfNeeded();
                TextWriter stdout = Console.Out;
                int code;
                using (MainForm form = new MainForm(opt)) code = form.RunScreenshot(stdout);
                try { stdout.Flush(); } catch (Exception) { }
                return code;
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Unexpected(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Unexpected(e.ExceptionObject as Exception); };
            using (MainForm form = new MainForm(opt)) Application.Run(form);
            return 0;
        }

        static void Unexpected(Exception ex)
        {
            try { new FileLog(false).Write("DETAIL", ex == null ? "unknown" : ex.ToString()); } catch (Exception) { }
            try
            {
                DarkDialog.Show(null, "Something unexpected happened",
                    "The installer met a situation it does not know. The details are in installer.log in %LOCALAPPDATA%\\BodycamMapInstaller. Anything already moved is in the backup folder; nothing was deleted.", "OK");
            }
            catch (Exception) { }
        }

        static int Usage(string why)
        {
            BorrowConsoleIfNeeded();
            try
            {
                TextWriter o = Console.Out;
                o.WriteLine("Bodycam Map Installer v" + Util.InstallerVersion + ": " + why);
                o.WriteLine("  (no arguments)                       open the window");
                o.WriteLine("  <file> [<file>...]                   open the window and install these files");
                o.WriteLine("  --game <dir> [--no-settings]         open the window for this game folder only");
                o.WriteLine("  --screenshot <png> --game <fake game under TEMP> [--screenshot-drop <file>]... [--screenshot-show-root <text>]");
                o.WriteLine("  --screenshot <png> --screenshot-state " + string.Join("|", ShotRenderer.States) + " [--thumb <png>]");
                o.WriteLine("  --ui-selftest <report.txt>           window checks, no game folder");
                o.WriteLine("  --selftest | --validate | --list | --install | --remove | --pause | --resume   (no window; see README)");
                o.Flush();
            }
            catch (Exception) { }
            return 2;
        }
    }
}
