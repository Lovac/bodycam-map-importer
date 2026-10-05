// Main window: find the game, list maps, install dropped maps, remove, hide and show maps.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;
using BodycamMapInstaller.Core;

namespace BodycamMapInstaller.Ui
{
    sealed class StartOptions
    {
        public string GameArg;
        public bool NoSettings;
        public string ScreenshotPng;
        public string ScreenshotState;
        public string ThumbPng;
        public string ShowRoot;
        public readonly List<string> ScreenshotDrops = new List<string>();
        public readonly List<string> OpenFiles = new List<string>();
        public bool Screenshot { get { return ScreenshotPng != null; } }
    }

    sealed class ScanResult
    {
        public GameInstall Game;
        public bool Running;
        public IList<InstalledMap> Maps = new List<InstalledMap>();
        public BaseCheck Base;
        public MenuState.Owner Owner = MenuState.Owner.None;
        public bool Paused;
        public readonly Dictionary<string, Image> Thumbs = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        public string Problem;
    }

    delegate void Work(Installer inst);

    partial class MainForm : Form
    {
#if BCMI_DEV
        public const string AppVersion = "v0.1 preview (dev build)";
#else
        public const string AppVersion = "v0.1 preview";
#endif
        public const string HideLink = "Hide custom maps (for online play)";
        public const string ShowLink = "Show custom maps again";
        public const string ReadyLine = "Ready. Drop a map you downloaded on the box.";
        static readonly TimeSpan ResultTime = TimeSpan.FromSeconds(6);

        public static readonly string[] StillWorkingButtons = { "Close anyway", "Keep waiting" };
        public const int StillWorkingDefault = 1, StillWorkingCancel = 1;

        readonly StartOptions opt;
        readonly AppSettings settings;
        readonly FileLog fileLog;
        readonly WindowLog log;
        readonly bool auto;
        static BaseTables baseTables;

        readonly Label title = new Label();
        readonly Label version = new Label();
        readonly Label gameCaption = new Label();
        readonly Label gamePath = new Label();
        readonly Label gameState = new Label();
        readonly LinkLabel pauseLink = new LinkLabel();
        readonly FlatButton browse = new FlatButton("Browse...");
        readonly DropZone drop = new DropZone();
        readonly Label listCaption = new Label();
        readonly MapList list = new MapList();
        readonly DetailsView details = new DetailsView();
        readonly FlatButton remove = new FlatButton("DELETE");
        readonly FlatButton openPaks = new FlatButton("Mods folder");
        readonly FlatButton openBackups = new FlatButton("Backups");
#if BCMI_DEV
        // map maker row (22 Sep 2026): open the selected card's level, rebuild it, and see what the PC is missing
        readonly FlatButton edit = new FlatButton("EDIT");
        readonly FlatButton cook = new FlatButton("COOK");
        readonly FlatButton setup = new FlatButton("SETUP");
#endif
        readonly Label logCaption = new Label();
        readonly ActivityView activity = new ActivityView();
        readonly System.Windows.Forms.Timer uiTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer gameTimer = new System.Windows.Forms.Timer();

        GameInstall game;
        ScanResult last;
        bool detecting = true, scanned, busy, running, devRunning, pollInFlight, scanInFlight, closingConfirmed, noteOpen, repairTried;
        string busyStatus;
        DateTime busyStart;
        bool resultShown;
        DropState resultState;
        DateTime resultUntil;
        string selectAfterScan;
        readonly List<string> pending = new List<string>();

        public MainForm(StartOptions options)
        {
            opt = options;
            auto = opt.Screenshot;
            settings = AppSettings.Load(opt.NoSettings || auto);
            fileLog = new FileLog(opt.NoSettings || auto);
            log = new WindowLog(fileLog);
            fileLog.Write("START", "Bodycam Map Installer v" + Util.InstallerVersion + " (" + AppVersion + ") started" + (IsElevated() ? " as administrator" : ""));

            Text = "Bodycam Map Installer";
            if (Theme.AppIcon != null) Icon = Theme.AppIcon;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.Px(960), Theme.Px(660));
            MinimumSize = new Size(Theme.Px(840), Theme.Px(600));
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;
            if (!ApplySavedBounds()) FitToScreen();

            Setup(title, "Bodycam Map Installer", Theme.Title, Theme.Text);
            Setup(version, AppVersion, Theme.Small, Theme.TextDim);
            Setup(gameCaption, "Game", Theme.Small, Theme.TextDim);
            Setup(gamePath, "Looking for Bodycam...", Theme.Body, Theme.Text);
            gamePath.AutoSize = false;
            gamePath.AutoEllipsis = true;
            Setup(gameState, "", Theme.Small, Theme.TextDim);
            gameState.AutoSize = false;
            gameState.AutoEllipsis = true;
            Setup(listCaption, "Installed maps", Theme.H2, Theme.Text);
            Setup(logCaption, "Activity", Theme.H2, Theme.Text);
            pauseLink.Font = Theme.Small;
            pauseLink.AutoSize = true;
            pauseLink.BackColor = Color.Transparent;
            pauseLink.LinkColor = Theme.Blue;
            pauseLink.ActiveLinkColor = Theme.Text;
            pauseLink.VisitedLinkColor = Theme.Blue;
            pauseLink.DisabledLinkColor = Theme.TextDim;
            pauseLink.LinkBehavior = LinkBehavior.HoverUnderline;
            pauseLink.UseMnemonic = false;
            pauseLink.Text = HideLink;
            pauseLink.Visible = false;

            browse.TabIndex = 0; drop.TabIndex = 1; list.TabIndex = 2; remove.TabIndex = 3; openPaks.TabIndex = 4; openBackups.TabIndex = 5; activity.TabIndex = 6; pauseLink.TabIndex = 7;
            remove.Danger = true;
            remove.Solid = true;
            details.ThumbClick += delegate { OnChangePicture(); };
#if BCMI_DEV
            cook.Primary = true;
            cook.Solid = true;
            edit.Click += delegate { OnEdit(); };
            cook.Click += delegate { OnCook(); };
            setup.Click += delegate { ShowRequirements(null); };
            Controls.AddRange(new Control[] { edit, cook, setup });
#endif

            drop.Activate += delegate { OnZoneActivate(); };
            list.SelectionChanged += delegate { details.Row = list.SelectedRow; UpdateChrome(); };
            list.RemoveRequested += delegate { if (remove.Enabled) OnRemove(); };
            remove.Click += delegate { OnRemove(); };
            browse.Click += delegate { BrowseGame(); };
            openPaks.Click += delegate { OpenFolder(game == null ? null : game.PaksDir, "The mods folder"); };
            openBackups.Click += delegate { OpenFolder(game == null ? null : game.BackupRoot, "The Backups folder"); };
            pauseLink.LinkClicked += delegate { OnPauseLink(); };

            Controls.AddRange(new Control[] { title, version, gameCaption, gamePath, gameState, pauseLink, browse, drop, listCaption, list, details, remove, openPaks, openBackups, logCaption, activity });
            HookDrop(this);
            foreach (Control c in Controls) HookDrop(c);

            uiTimer.Interval = 100;
            uiTimer.Tick += delegate { OnUiTick(); };
            gameTimer.Interval = 2000;
            gameTimer.Tick += delegate { PollGame(); };
            pending.AddRange(opt.OpenFiles);
            UpdateChrome();
        }

        static void Setup(Label l, string text, Font f, Color c)
        {
            l.Text = text;
            l.Font = f;
            l.ForeColor = c;
            l.BackColor = Color.Transparent;
            l.AutoSize = true;
            l.UseMnemonic = false;
        }

        static bool IsElevated()
        {
            try { using (WindowsIdentity id = WindowsIdentity.GetCurrent()) return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
            catch (Exception) { return false; }
        }

        static BaseTables Tables
        {
            get { if (baseTables == null) baseTables = BaseTables.Embedded(); return baseTables; }
        }

        Installer NewInstaller(GameInstall g)
        {
            return new Installer(g, Tables, log, Ask);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            uiTimer.Start();
            gameTimer.Start();
            if (IsElevated()) log.Dim("Running as administrator: Windows blocks drag and drop from Explorer into this window. Click the box to pick a file instead.");
            StartScan(true, true);
        }

        bool ApplySavedBounds()
        {
            if (settings.Bounds.Width <= 0) return false;
            foreach (Screen s in Screen.AllScreens)
            {
                Rectangle inter = Rectangle.Intersect(s.WorkingArea, settings.Bounds);
                if (inter.Width >= Theme.Px(200) && inter.Height >= Theme.Px(120))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = settings.Bounds;
                    if (settings.Maximized) WindowState = FormWindowState.Maximized;
                    return true;
                }
            }
            return false;
        }

        void FitToScreen()
        {
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int chromeH = Height - ClientSize.Height, chromeW = Width - ClientSize.Width;
            if (MinimumSize.Height > wa.Height || MinimumSize.Width > wa.Width)
                MinimumSize = new Size(Math.Min(MinimumSize.Width, wa.Width), Math.Min(MinimumSize.Height, wa.Height));
            int h = ClientSize.Height, w = ClientSize.Width;
            if (Height > wa.Height) h = Math.Max(MinimumSize.Height - chromeH, wa.Height - chromeH - Theme.Px(8));
            if (Width > wa.Width) w = Math.Max(MinimumSize.Width - chromeW, wa.Width - chromeW - Theme.Px(8));
            if (h != ClientSize.Height || w != ClientSize.Width) ClientSize = new Size(w, h);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (busy && !auto && !closingConfirmed && e.CloseReason == CloseReason.UserClosing)
            {
                int c = DarkDialog.ShowEx(this, "Still working",
                    devRunning
                        ? "A build is running. Closing this window does not stop it, but its output stops showing here. Wait until it finishes."
                        : "The installer is in the middle of a step. Wait a moment; if you close now, the next start tidies the unfinished step into the backup folder.",
                    StillWorkingDefault, StillWorkingCancel, -1, StillWorkingButtons);
                if (c != 0) { e.Cancel = true; return; }
                closingConfirmed = true;
            }
            base.OnFormClosing(e);
            if (!auto)
            {
                settings.Maximized = WindowState == FormWindowState.Maximized;
                settings.Bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                settings.Save();
                fileLog.Write("STOP", "window closed");
            }
            uiTimer.Stop();
            gameTimer.Stop();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.F5 && !busy) { e.Handled = true; log.Info("Looking for Bodycam again..."); StartScan(true, true); }
        }

        void BeginInvokeSafe(MethodInvoker m)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(m); }
            catch (InvalidOperationException) { }
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if (activity == null || drop == null) return;
            int m = Theme.Px(24), gap = Theme.Px(16), rightW = Theme.Px(380), logH = Theme.Px(100);
            int w = ClientSize.Width, h = ClientSize.Height;
            int btnH = Theme.Body.Height + Theme.Px(14);
            title.Location = new Point(m - Theme.Px(2), Theme.Px(14));
            version.Location = new Point(w - m - version.PreferredWidth, Theme.Px(22));

            int y = title.Bottom + Theme.Px(12);
            int capW = gameCaption.PreferredWidth + Theme.Px(12);
            int bw = browse.Preferred;
            browse.SetBounds(w - m - bw, y, bw, btnH);
            gameCaption.Location = new Point(m, y + (Theme.Body.Height - Theme.Small.Height) / 2 + Theme.Px(1));
            int textW = browse.Left - gap - m - capW;
            gamePath.SetBounds(m + capW, y, textW, Theme.Body.Height + Theme.Px(2));
            gameState.SetBounds(m + capW, gamePath.Bottom + Theme.Px(1), textW, Theme.Small.Height + Theme.Px(2));
            pauseLink.Location = new Point(m + capW, gameState.Bottom + Theme.Px(2));
            int linkBottom = pauseLink.Top + Theme.Small.Height + Theme.Px(2);

            int top = Math.Max(browse.Bottom, linkBottom) + Theme.Px(14) + listCaption.PreferredHeight;
            int bodyH = h - top - m - logH - logCaption.PreferredHeight - Theme.Px(8) - gap;
            int rx = w - m - rightW;
            drop.SetBounds(m, top - listCaption.PreferredHeight - Theme.Px(6), rx - gap - m, bodyH + listCaption.PreferredHeight + Theme.Px(6));
            listCaption.Location = new Point(rx - Theme.Px(2), top - listCaption.PreferredHeight - Theme.Px(6));

            int thumbH = Theme.Px(64);
            int detailsH = Math.Max(thumbH, Theme.BodySemi.Height + Theme.Small.Height * 2 + Theme.Px(8));
#if BCMI_DEV
            int makerRow = btnH + Theme.Px(8);
#else
            int makerRow = 0;
#endif
            int listH = bodyH - detailsH - btnH - makerRow - Theme.Px(24);
            list.SetBounds(rx, top, rightW, listH);
            details.SetBounds(rx, list.Bottom + Theme.Px(12), rightW, detailsH);

            int by = top + bodyH - btnH;
            int each = (rightW - Theme.Px(16)) / 3;
            remove.SetBounds(rx, by, each, btnH);
            openPaks.SetBounds(remove.Right + Theme.Px(8), by, each, btnH);
            openBackups.SetBounds(openPaks.Right + Theme.Px(8), by, rightW - 2 * each - Theme.Px(16), btnH);
#if BCMI_DEV
            int my = by - makerRow;
            edit.SetBounds(rx, my, each, btnH);
            cook.SetBounds(edit.Right + Theme.Px(8), my, each, btnH);
            setup.SetBounds(cook.Right + Theme.Px(8), my, rightW - 2 * each - Theme.Px(16), btnH);
#endif

            int ly = top + bodyH + gap;
            logCaption.Location = new Point(m - Theme.Px(2), ly);
            activity.SetBounds(m, logCaption.Bottom + Theme.Px(8), w - m * 2, h - (logCaption.Bottom + Theme.Px(8)) - m);
        }

        string SourcePart(GameInstall g)
        {
            if (opt.ShowRoot != null) return "";
            if (g.Source == "Steam") return " (Steam)";
            if (g.Source == "Selftest") return " (test folder)";
            return " (folder you picked)";
        }

        int MapCount
        {
            get
            {
                int n = 0;
                if (last != null) foreach (InstalledMap m in last.Maps) if (m.Kind != MapKind.OtherMod) n++;
                return n;
            }
        }

        bool BaseChanged
        {
            get
            {
                if (last == null || last.Base == null || game == null) return false;
                return last.Base.Result == BaseCheck.Status.Mismatch || (last.Base.Result == BaseCheck.Status.PaksMissing && !game.IsSelftestFake);
            }
        }

        bool Paused { get { return last != null && last.Paused; } }

        void UpdateChrome()
        {
            int n = MapCount;
            DropState ds;
            if (detecting && game == null)
            {
                gamePath.Text = "Looking for Bodycam...";
                SetState("", Theme.TextDim);
                ds = busy ? DropState.Busy : DropState.Detecting;
            }
            else if (game == null)
            {
                gamePath.Text = "Bodycam not found";
                if (busy) SetState(StatusWithClock(), Theme.TextDim);
                else SetState("Click Browse... and pick your Bodycam folder.", Theme.Amber);
                ds = busy ? DropState.Busy : DropState.NoGame;
            }
            else
            {
                gamePath.Text = opt.ShowRoot ?? game.Root;
                if (busy) { SetState(StatusWithClock(), Theme.TextDim); ds = DropState.Busy; }
                else if (detecting) { SetState("Looking for Bodycam...", Theme.TextDim); ds = DropState.Detecting; }
                else if (running) { SetState("Bodycam is open. Close the game to install or remove maps.", Theme.Red); ds = DropState.Locked; }
                else if (Paused) { SetState("Custom maps are hidden. The game shows only its own maps.", Theme.Amber); ds = DropState.Paused; }
                else if (BaseChanged) { SetState("Bodycam changed its map list. Maps still install; their cards wait for a newer installer.", Theme.Amber); ds = DropState.Ready; }
                else if (last != null && last.Owner == MenuState.Owner.Foreign) { SetState("Another mod already changed the game's map list.", Theme.Amber); ds = DropState.Ready; }
                else
                {
                    string maps = n == 0 ? "no maps yet" : Util.Plural(n, "map", "maps");
                    SetState("Bodycam found" + SourcePart(game) + "  " + Theme.Dot + "  game closed  " + Theme.Dot + "  " + maps, Theme.Green);
                    ds = DropState.Ready;
                }
            }

            if (resultShown && ds == DropState.Ready) ds = resultState;
            else if (ds != DropState.Ready) resultShown = false;
            drop.State = ds;

            bool idle = !busy && !detecting;
            bool linkActive = idle && !running && game != null;
            pauseLink.Visible = game != null && !detecting && (n > 0 || Paused);
            pauseLink.Text = Paused ? ShowLink : HideLink;

            pauseLink.LinkColor = linkActive ? Theme.Blue : Theme.TextDim;
            pauseLink.VisitedLinkColor = pauseLink.LinkColor;
            pauseLink.ActiveLinkColor = linkActive ? Theme.Text : Theme.TextDim;
            pauseLink.LinkBehavior = linkActive ? LinkBehavior.HoverUnderline : LinkBehavior.NeverUnderline;
            pauseLink.Cursor = linkActive ? Cursors.Hand : Cursors.Default;
            browse.Enabled = !busy;
            list.Enabled = idle;
            details.Visible = list.Rows.Count > 0;
            MapRow sel = list.SelectedRow;
            remove.Enabled = idle && game != null && !running && !Paused && sel != null && sel.Map != null;
            openPaks.Enabled = game != null;
            openBackups.Enabled = game != null;
#if BCMI_DEV
            bool card = sel != null && sel.Map != null && sel.Map.Kind == MapKind.Card && sel.Map.Card != null;
            edit.Enabled = idle && card;
            cook.Enabled = idle && game != null && !running && !Paused && card;
            setup.Enabled = !busy;
#endif
        }

        void SetState(string text, Color color)
        {
            gameState.Text = text;
            gameState.ForeColor = color;
        }

        string StatusWithClock()
        {
            string s = busyStatus ?? "Working...";
            TimeSpan t = DateTime.Now - busyStart;
            if (t.TotalSeconds >= 5) s += "   " + (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
            return s;
        }

        void OnUiTick()
        {
            DrainLog();
            string p = log.TakeProgress();
            if (p != null && busy && p.Length > 0) busyStatus = p;
            drop.Progress = busy ? log.ProgressFraction : -1f;
            drop.Tick();
            if (busy) SetState(StatusWithClock(), Theme.TextDim);
            if (resultShown && !busy && DateTime.Now > resultUntil) { resultShown = false; UpdateChrome(); }
        }

        void DrainLog()
        {
            foreach (WindowLog.Entry e in log.Drain())
            {
                Color c = Theme.Text;
                switch (e.Level)
                {
                    case LogLevel.Good: c = Theme.Green; break;
                    case LogLevel.Warn: c = Theme.Amber; break;
                    case LogLevel.Block: c = Theme.RedText; break;
                    case LogLevel.Dim: c = Theme.TextDim; break;
                    case LogLevel.Detail: c = Theme.TextDim; break;
                    case LogLevel.Output: c = Theme.Mix(Theme.TextDim, Theme.Text, 0.35f); break;
                }
                activity.Add(e.Text, c, e.When, e.Level == LogLevel.Detail);
            }
        }

        void ShowDropResult(bool done, string head, string sub)
        {
            resultShown = true;
            resultState = done ? DropState.Done : DropState.Refused;
            resultUntil = DateTime.Now + ResultTime;
            drop.ShowResult(done, head, sub);
        }

        void ClearDropResult()
        {
            if (!resultShown) return;
            resultShown = false;
            UpdateChrome();
        }

        void StartScan(bool findGame, bool announce)
        {
            if (busy || scanInFlight) return;
            scanInFlight = true;
            if (findGame) { detecting = true; game = null; }
            UpdateChrome();
            GameInstall current = game;
            Thread t = new Thread(delegate()
            {
                ScanResult r = Scan(current, findGame);
                BeginInvokeSafe(delegate { scanInFlight = false; ApplyScan(r, announce); });
            });
            t.IsBackground = true;
            t.Name = "scan";
            t.Start();
        }

        string Plain(Exception ex, string fallback)
        {
            fileLog.Write("DETAIL", ex.ToString());
            return Util.PlainReason(ex) ?? fallback;
        }

        ScanResult Scan(GameInstall current, bool findGame)
        {
            ScanResult r = new ScanResult();
            GameInstall g = findGame ? null : current;
            try
            {
                if (g == null)
                {
                    if (opt.GameArg != null)
                    {
                        g = GameLocator.FromUserPath(opt.GameArg);
                        if (g == null) { log.Warn("That is not the Bodycam folder."); log.Detail("Not the Bodycam folder: " + opt.GameArg); }
                        else log.Detail("Found Bodycam at " + g.Root);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(settings.GameOverride))
                        {
                            g = GameLocator.FromUserPath(settings.GameOverride);
                            if (g != null) log.Detail("Found Bodycam at " + g.Root);
                            else { log.Warn("The Bodycam folder picked earlier is not there any more. Looking in Steam..."); log.Detail("Missing folder: " + settings.GameOverride); }
                        }
                        if (g == null) g = GameLocator.FindSteam(log);
                    }
                }
                if (g == null) return r;
                r.Game = g;
                r.Running = GameLocator.IsGameRunning();
                Installer inst = NewInstaller(g);
                if (!r.Running && !auto) inst.RecoverStaleFiles();
                InstallContext ctx = inst.Context();
                r.Maps = ctx.Installed;
                r.Base = ctx.BaseStatus;
                r.Owner = ctx.Menu.Check();
                r.Paused = ctx.Menu.Paused;
                foreach (InstalledMap m in r.Maps)
                    if (m.ThumbPng != null)
                    {
                        Image img = LoadThumb(m.ThumbPng);
                        if (img != null) r.Thumbs[RowKey(m)] = img;
                    }
            }
            catch (UnauthorizedAccessException ex)
            {
                fileLog.Write("DETAIL", ex.ToString());
                r.Problem = "Windows did not let the installer read the Bodycam folder. Run the installer as administrator to continue.";
            }
            catch (Exception ex)
            {
                r.Problem = "The installer could not read the mods folder. " + Plain(ex, "Details are in installer.log.");
            }
            return r;
        }

        static string RowKey(InstalledMap m) { return m.Kind + ":" + m.Id; }
        static string RowKey(MapOutcome o) { return o.Kind + ":" + o.Id; }

        static Image LoadThumb(string path)
        {
            try
            {
                byte[] b = File.ReadAllBytes(path);
                using (MemoryStream ms = new MemoryStream(b))
                using (Image src = Image.FromStream(ms))
                {
                    int w = Math.Min(src.Width, Theme.Px(256));
                    int h = Math.Max(1, (int)Math.Round((double)w * src.Height / src.Width));
                    return new Bitmap(src, new Size(w, h));
                }
            }
            catch (Exception) { return null; }
        }

        void ApplyScan(ScanResult r, bool announce)
        {
            bool first = !scanned;
            scanned = true;
            detecting = false;
            game = r.Game;
            last = r;
            running = r.Running;
            list.SetRows(BuildRows(r));
            if (selectAfterScan != null)
            {
                for (int i = 0; i < list.Rows.Count; i++)
                    if (string.Equals(list.Rows[i].Key, selectAfterScan, StringComparison.OrdinalIgnoreCase)) list.Selected = i;
                selectAfterScan = null;
            }
            details.Row = list.SelectedRow;
            if (r.Problem != null) log.Block(r.Problem);
            if (announce && r.Problem == null && game != null) Summarize(r, first);
            UpdateChrome();
            DrainLog();
            if (!auto) BeginInvokeSafe(delegate { MaybeHardpointNote(); });
            if (pending.Count > 0)
            {
                string[] files = pending.ToArray();
                pending.Clear();
                if (auto) OnFiles(files); else BeginInvokeSafe(delegate { OnFiles(files); });
            }
            else if (!auto && MenuFileMissing(r)) BeginInvokeSafe(delegate { RepairMenu(); });
        }

        bool MenuFileMissing(ScanResult r)
        {
            if (repairTried || r == null || r.Game == null || r.Running || r.Paused || r.Problem != null || r.Owner != MenuState.Owner.None || BaseChangedFor(r)) return false;
            foreach (InstalledMap m in r.Maps) if (m.Kind == MapKind.Card && m.Problems.Count == 0) return true;
            return false;
        }

        void RepairMenu()
        {
            if (busy || repairTried || game == null) return;
            repairTried = true;
            RunOperation("Repairing the map list...", delegate(Installer inst)
            {
                MenuResult m = inst.RegenerateMenu(false);
                if (m.Written) log.Good("The game's map list file was missing; the installer made it again.");
            }, null, null);
        }

        List<MapRow> BuildRows(ScanResult r)
        {
            List<MapRow> rows = new List<MapRow>();
            List<InstalledMap> others = new List<InstalledMap>();
            foreach (InstalledMap m in r.Maps)
            {
                if (m.Kind == MapKind.OtherMod) { others.Add(m); continue; }
                MapRow row = new MapRow();
                row.Map = m;
                row.Key = RowKey(m);
                row.Source = m.Source;
                string modes = ModesText(m.Modes);
                if (m.Kind == MapKind.Card)
                {
                    row.Name = m.DisplayName ?? m.Id;
                    row.Right = string.IsNullOrEmpty(m.Version) ? "" : "v" + m.Version;
                    row.Modes = modes;
                    row.Description = m.Description;
                    row.Author = m.Author;
                }
                else
                {
                    row.Name = m.DisplayName ?? m.Id;
                    row.Right = "single file";
                    row.Modes = modes.Length > 0 ? modes : "Single-file map";
                    row.Description = "Single-file map. Works as is.";
                }
                // any kind: a card's photo, or the sidecar _cards/_legacy/<pak>.png of a single-file pak
                Image img;
                if (r.Thumbs.TryGetValue(row.Key, out img)) row.Thumb = img;
                if (m.Problems.Count > 0)
                {
                    row.Dot = Theme.Red;
                    row.Modes = "missing files";
                    row.ModesColor = Theme.RedText;
                    row.Description = "Some of its files are missing. Remove moves the rest to the backup folder.";
                }
                if (m.Paused) row.Modes += "   " + Theme.Dot + "   hidden";
                rows.Add(row);
            }
            if (others.Count > 0)
            {
                MapRow row = new MapRow();
                row.Key = "OtherMods";
                row.Name = Util.Plural(others.Count, "other mod", "other mods");
                row.Right = "not a map";
                row.Dim = true;
                List<string> names = new List<string>();
                foreach (InstalledMap m in others) names.Add(m.Id);
                row.Modes = string.Join(", ", names.ToArray());
                row.Description = others.Count == 1 ? "Not a map. The installer leaves it alone." : "Not maps. The installer leaves them alone.";
                rows.Add(row);
            }
            return rows;
        }

        static string ModesText(string[] modes)
        {
            if (modes == null) return "";
            List<string> n = new List<string>();
            foreach (string m in modes) n.Add(PackagePaths.ModeLongName(m));
            return string.Join(", ", n.ToArray());
        }

        void Summarize(ScanResult r, bool first)
        {
            int maps = 0, others = 0;
            bool problems = false;
            foreach (InstalledMap m in r.Maps)
            {
                if (m.Kind == MapKind.OtherMod) others++; else maps++;
                if (m.Problems.Count > 0) problems = true;
            }
            if (r.Running) log.Block("Bodycam is open. Close the game to install or remove maps.");
            if (r.Paused) log.Warn("Custom maps are hidden. The game shows only its own maps. \"" + ShowLink + "\" brings them back.");
            else if (BaseChangedFor(r))
            {
                log.Warn("Bodycam changed its map list since this installer was made. Maps still install; their cards wait for a newer installer.");
                if (r.Base != null) foreach (string d in r.Base.Details) log.Detail(d);
            }
            else if (r.Owner == MenuState.Owner.Foreign) log.Warn("Another mod already changed the game's map list. Installing a map here asks before replacing it.");
            else if (maps == 0) log.Info("No custom maps installed yet.");
            else if (!problems && r.Owner != MenuState.Owner.None) log.Good(Util.Plural(maps, "map", "maps") + " installed. The map list is up to date.");
            else if (!problems) log.Good(Util.Plural(maps, "map", "maps") + " installed.");
            foreach (InstalledMap m in r.Maps)
                if (m.Problems.Count > 0)
                {
                    log.Warn(m.Label + " is missing files. Remove moves the rest to the backup folder.");
                    log.Detail(m.Label + ": " + string.Join("; ", m.Problems.ToArray()));
                }
            if (first && others > 0) log.Dim(Util.Plural(others, "other mod in the mods folder is", "other mods in the mods folder are") + " not a map; the installer leaves " + (others == 1 ? "it" : "them") + " alone.");
            if (first && !r.Running && !r.Paused) log.Info(ReadyLine);
        }

        bool BaseChangedFor(ScanResult r)
        {
            if (r.Base == null || r.Game == null) return false;
            return r.Base.Result == BaseCheck.Status.Mismatch || (r.Base.Result == BaseCheck.Status.PaksMissing && !r.Game.IsSelftestFake);
        }

        void PollGame()
        {
            if (game == null || busy || detecting || pollInFlight) return;
            pollInFlight = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool now = false;
                try { now = GameLocator.IsGameRunning(); } catch (Exception) { }
                BeginInvokeSafe(delegate
                {
                    pollInFlight = false;
                    if (now == running) return;
                    running = now;
                    UpdateChrome();
                    if (!now && !busy) StartScan(false, false);
                });
            });
        }

        void MaybeHardpointNote()
        {
            if (noteOpen || busy || last == null || !Visible) return;
            foreach (InstalledMap m in last.Maps)
            {
                if (m.Kind != MapKind.Card || m.Modes == null || Array.IndexOf(m.Modes, "HP") < 0) continue;
                if (settings.HardpointNoted.Contains(m.Id)) continue;
                settings.HardpointNoted.Add(m.Id);
                settings.Save();
                noteOpen = true;
                try
                {
                    string name = m.DisplayName ?? m.Id;
                    DarkDialog.Show(this, name + " also offers Hardpoint", "Hardpoint on custom maps is still being tested. Team Deathmatch and Deathmatch are the safe picks.", "OK");
                }
                finally { noteOpen = false; }
                BeginInvokeSafe(delegate { MaybeHardpointNote(); });
                return;
            }
        }

        public static void AskStyle(string yes, out int defaultIndex, out int cancelIndex, out int dangerIndex)
        {
            defaultIndex = yes == "Replace" ? 1 : 0;
            cancelIndex = 1;
            dangerIndex = (yes == "Remove" || yes == "DELETE") ? 0 : -1;
        }

        bool Ask(string titleText, string body, string yes, string no)
        {
            if (auto)
            {
                log.Dim("(screenshot run) " + titleText + " -> " + yes);
                return true;
            }
            bool answer = false;
            MethodInvoker show = delegate
            {
                DrainLog();
                int d, c, x;
                AskStyle(yes, out d, out c, out x);
                answer = DarkDialog.ShowEx(this, titleText, body, d, c, x, yes, no) == 0;
            };
            if (InvokeRequired) Invoke(show); else show();
            fileLog.Write("ASK", titleText + " -> " + (answer ? yes : no));
            return answer;
        }

        void RunOperation(string status, Work work, string[] filesForAdmin, MethodInvoker after)
        {
            busy = true;
            busyStatus = status;
            busyStart = DateTime.Now;
            resultShown = false;
            log.ResetProgress();
            log.BeginCapture();
            UpdateChrome();
            DrainLog();
            GameInstall g = game;
            if (auto) { RunWork(g, work, filesForAdmin, after, true); return; }
            Thread t = new Thread(delegate() { RunWork(g, work, filesForAdmin, after, false); });
            t.IsBackground = true;
            t.Name = "installer work";
            t.Start();
        }

        void RunWork(GameInstall g, Work work, string[] filesForAdmin, MethodInvoker after, bool sync)
        {
            bool admin = false;
            try
            {
                work(g == null ? null : NewInstaller(g));
            }
            catch (UnauthorizedAccessException ex)
            {
                admin = true;
                log.Block("Windows did not let the installer write in the Bodycam folder.");
                fileLog.Write("DETAIL", ex.ToString());
            }
            catch (Exception ex)
            {
                string plain = Util.PlainReason(ex);
                log.Block(plain != null
                    ? plain + " Anything the installer had moved is in the backup folder."
                    : "The installer stopped at this step. Anything it had moved is in the backup folder. Details are in installer.log.");
                fileLog.Write("DETAIL", ex.ToString());
            }
            ScanResult r = g == null ? null : Scan(g, false);
            MethodInvoker finish = delegate
            {
                busy = false;
                devRunning = false;
                busyStatus = null;
                log.TakeProgress();
                log.ResetProgress();
                DrainLog();
                if (after != null) after();
                if (r != null) ApplyScan(r, false);
                UpdateChrome();
                DrainLog();
                if (admin && !auto) OfferAdmin(filesForAdmin);
            };
            if (sync) finish(); else BeginInvokeSafe(finish);
        }

        void OfferAdmin(string[] files)
        {
            int c = DarkDialog.Show(this, "Windows kept the installer out", "Windows did not let the installer write in the Bodycam folder. Run the installer as administrator to continue.", "Run as administrator", "Cancel");
            if (c != 0) { log.Info("Nothing changed."); return; }
            List<string> args = new List<string>();
            if (game != null) { args.Add("--game"); args.Add(game.Root); }
            if (files != null) args.AddRange(files);
            string joined = "";
            foreach (string a in args) joined += (joined.Length > 0 ? " " : "") + Util.QuoteArg(a);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(Application.ExecutablePath, joined);
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                Process.Start(psi);
                closingConfirmed = true;
                Close();
            }
            catch (Win32Exception) { log.Info("Nothing changed."); }
        }

        void HookDrop(Control c)
        {
            c.AllowDrop = true;
            c.DragEnter += OnDragEnter;
            c.DragOver += OnDragOver;
            c.DragLeave += delegate { drop.SetHot(false, null); };
            c.DragDrop += OnDragDrop;
        }

        public static bool IsMapFileName(string path, out bool project)
        {
            project = false;
            if (string.IsNullOrEmpty(path)) return false;
            string ext = Path.GetExtension(path).ToLowerInvariant();
#if BCMI_DEV
            if (ext == ".uproject") { project = true; return true; }
#endif
            if (ext == ".zip" || ext == ".bcmap" || ext == ".pak") return true;
            try { return Directory.Exists(path); } catch (Exception) { return false; }
        }

        int DropCheck(string[] files, out bool project)
        {
            project = false;
            if (files == null || files.Length == 0 || busy || detecting) return 0;
            foreach (string f in files)
            {
                bool p;
                if (!IsMapFileName(f, out p)) return -1;
                if (p) project = true;
            }
            if (project) return 1;
            return game != null && !running && !Paused ? 1 : 0;
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            ClearDropResult();
            string[] files = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
            bool project;
            int ok = DropCheck(files, out project);
            e.Effect = ok == 1 ? DragDropEffects.Copy : DragDropEffects.None;
            if (ok == -1) drop.SetHotRefused();
            else drop.SetHot(ok == 1, project ? "Let go to build" : "Let go to install");
        }

        void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effect = drop.Hot ? DragDropEffects.Copy : DragDropEffects.None;
        }

        void OnDragDrop(object sender, DragEventArgs e)
        {
            bool wasHot = drop.Hot;
            drop.SetHot(false, null);
            string[] files = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
            if (!wasHot || files == null) return;

            BeginInvokeSafe(delegate { Activate(); OnFiles(files); });
        }

        void OnZoneActivate()
        {
            if (drop.State == DropState.NoGame) { BrowseGame(); return; }
            if ((drop.State != DropState.Ready && drop.State != DropState.Done && drop.State != DropState.Refused) || busy) return;
            ClearDropResult();
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Pick a Bodycam map";
                d.Filter = "Bodycam maps (*.zip;*.bcmap;*.pak)|*.zip;*.bcmap;*.pak|All files (*.*)|*.*";
                d.Multiselect = true;
                d.RestoreDirectory = true;
                if (d.ShowDialog(this) == DialogResult.OK) OnFiles(d.FileNames);
            }
        }

        public void OnFiles(string[] files)
        {
            if (files == null || files.Length == 0) return;
            if (!scanned) { pending.AddRange(files); return; }
            if (busy) { log.Warn("Still working on the last step. Drop the file again when it finishes."); DrainLog(); return; }
            List<string> sorted = new List<string>(files);
            sorted.Sort(delegate(string a, string b) { return StringComparer.OrdinalIgnoreCase.Compare(Path.GetFileName(a), Path.GetFileName(b)); });
#if BCMI_DEV
            string project = null;
            foreach (string f in sorted) if (project == null && f.EndsWith(".uproject", StringComparison.OrdinalIgnoreCase)) project = f;
            if (project != null)
            {
                foreach (string f in sorted) if (f != project) log.Info(Path.GetFileName(f) + " waits: drop it again after the build.");
                RunDev(project);
                return;
            }
#endif
            if (game == null) { log.Warn("Bodycam not found yet. Click Browse... and pick your Bodycam folder, then drop the map again."); DrainLog(); return; }
            if (Paused) { log.Warn(Installer.PausedLine); DrainLog(); return; }
            if (GameLocator.IsGameRunning())
            {
                running = true;
                UpdateChrome();
                log.Block("Bodycam is open. Close it and drop the file again.");
                DrainLog();
                return;
            }
            string[] arr = sorted.ToArray();
            string what = arr.Length == 1 ? Path.GetFileName(arr[0].TrimEnd('\\', '/')) : arr.Length + " files";
            List<MapOutcome> outcomes = new List<MapOutcome>();
            int[] worst = new int[1];
            RunOperation("Checking " + what + "...", delegate(Installer inst)
            {
                bool newMap = false;
                foreach (string f in arr)
                {
                    OperationResult res = inst.InstallDropped(f);
                    lock (outcomes) outcomes.AddRange(res.Outcomes);
                    foreach (MapOutcome o in res.Outcomes) if (!o.AlreadyInstalled) newMap = true;
                    worst[0] = Math.Max(worst[0], res.ExitCode);
                    if (res.ExitCode == Installer.ExitRunning) break;
                }
                if (newMap) log.Dim("Playing with friends: everyone needs the same maps, same versions.");
            }, arr, delegate { ShowDropOutcome(outcomes, worst[0], log.FirstBlock); });
        }

        void ShowDropOutcome(List<MapOutcome> outcomes, int worstCode, string firstBlock)
        {
            MapOutcome lastNew = null, lastAny = null;
            int newCount = 0;
            lock (outcomes)
                foreach (MapOutcome o in outcomes)
                {
                    lastAny = o;
                    if (!o.AlreadyInstalled) { lastNew = o; newCount++; }
                }
            MapOutcome pick = lastNew ?? lastAny;
            if (pick != null) selectAfterScan = RowKey(pick);
            if (firstBlock != null)
            {
                string head = newCount > 0 ? Util.Plural(newCount, "map", "maps") + " installed, not everything" : "Not installed";
                ShowDropResult(false, head, firstBlock);
                return;
            }
            if (pick == null) return;
            if (newCount == 0) { ShowDropResult(true, pick.Name + " is already installed", "Nothing changed. Start Bodycam > Play > Custom > " + pick.PlayMode); return; }
            string done = newCount == 1 ? pick.Name + " installed" : Util.Plural(newCount, "map", "maps") + " installed";
            string sub = worstCode == Installer.ExitMenuWaiting ? "The map list was not updated yet. The lines below say why." : "Start Bodycam > Play > Custom > " + pick.PlayMode;
            ShowDropResult(true, done, sub);
        }

        void BrowseGame()
        {
            if (busy) return;
            string start = game != null ? game.Root : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string picked = FolderPicker.Pick(this, "Pick your Bodycam folder", start);
            if (picked == null) return;
            GameInstall g = GameLocator.FromUserPath(picked);
            if (g == null)
            {
                DarkDialog.Show(this, "That is not the Bodycam folder", "Pick the folder that holds Bodycam.exe, or its Bodycam\\Content\\Paks folder.", "OK");
                return;
            }
            settings.GameOverride = g.Root;
            settings.Save();
            opt.GameArg = null;
            StartScan(true, true);
        }

        void OpenFolder(string dir, string what)
        {
            if (dir == null) return;
            if (!Directory.Exists(dir)) { log.Info(what + " is not there yet. Nothing has been moved aside so far."); log.Detail(dir); DrainLog(); return; }
            try { Process.Start("explorer.exe", Util.QuoteArg(dir)); }
            catch (Exception ex) { log.Info("Windows did not open " + what.ToLowerInvariant() + ". " + Plain(ex, "")); DrainLog(); }
        }

        void OnRemove()
        {
            MapRow row = list.SelectedRow;
            if (row == null || row.Map == null || busy || game == null) return;
            if (GameLocator.IsGameRunning()) { running = true; UpdateChrome(); log.Block("Bodycam is open. Close it to install or remove maps."); DrainLog(); return; }
            InstalledMap m = row.Map;
            bool yes;
            if (m.Kind == MapKind.OtherMod) yes = Ask("DELETE " + m.Id + "?", "This pak is not a map. It moves to the Backups folder and the game stops loading it. You can put it back from Backups.", "DELETE", "Cancel");
            else yes = Ask("DELETE " + row.Name + "?", "It leaves the game. Its files move to the Backups folder, so you can put it back later.", "DELETE", "Cancel");
            if (!yes) return;
            string id = m.Kind == MapKind.Card && m.CardDir != null && m.Card == null ? Path.GetFileName(m.CardDir) : m.Id;
            RunOperation("Removing " + row.Name + "...", delegate(Installer inst) { inst.Remove(id); }, null, null);
        }

        void OnPauseLink()
        {
            if (busy || detecting || running || game == null) return;
            if (Paused)
            {
                RunOperation("Showing custom maps again...", delegate(Installer inst) { inst.Resume(); }, null, null);
                return;
            }
            if (!Ask("Hide custom maps?", "The game will show only its own maps, for public or Casual matches. Your maps move to a side folder and come back with \"" + ShowLink + "\".", "Hide", "Cancel")) return;
            RunOperation("Hiding custom maps...", delegate(Installer inst) { inst.Pause(); }, null, null);
        }

#if BCMI_DEV

        void RunDev(string uproject)
        {
            string file = Path.GetFileName(uproject);
            string project = Path.GetFileNameWithoutExtension(uproject);
            if (auto && !Util.IsUnder(uproject, Path.GetTempPath()))
            {
                log.Block("(screenshot run) " + file + " is not under TEMP; a screenshot run never builds a real project.");
                DrainLog();
                return;
            }
            if (!DevRunner.IsDevProject(uproject))
            {
                log.Warn(file + " is an Unreal project without the map build tools (" + DevRunner.OneclickRelPath + " is not beside it). Nothing changed.");
                DrainLog();
                return;
            }
            if (GameLocator.IsGameRunning()) { log.Block("Bodycam is open. Close it and drop the project again."); DrainLog(); return; }
            string bash = DevRunner.FindGitBash();
            if (bash == null) { log.Block("Building maps needs Git Bash, which is not on this PC. Install Git for Windows, then drop the project again."); DrainLog(); return; }
            int choice;
            if (auto) { choice = 1; log.Dim("(screenshot run) Build the maps of " + project + "? -> Plan only"); }
            else choice = DarkDialog.Show(this, "Build the maps of " + project + "?",
                "The build tools cook every map of this project and install each one with its own card. This can take a long time; keep Bodycam and the Unreal editor closed. Plan only runs the project's build script in plan mode.",
                "Build and install", "Plan only", "Cancel");
            if (choice != 0 && choice != 1) { log.Info("Nothing changed."); DrainLog(); return; }
            bool plan = choice == 1;
            if (plan) log.Info("Plan only: the project's build script runs in plan mode.");
            else log.Info("Project detected: building and installing the maps of " + project + ". This takes a while; keep Bodycam and the Unreal editor closed.");
            string full = Path.GetFullPath(uproject);
            string script = DevRunner.OneclickFor(full);
            string workDir = Path.GetDirectoryName(full);
            Dictionary<string, string> env = new Dictionary<string, string>();
            if (plan) env["ONECLICK_DRY"] = "1";
            else if (Environment.GetEnvironmentVariable("ONECLICK_DRY") != null) env["ONECLICK_DRY"] = "";
            log.Dim("Git Bash: " + bash + "  |  " + script + "  |  " + full + (plan ? "  |  ONECLICK_DRY=1" : ""));
            devRunning = true;
            RunOperation(plan ? "Listing the build plan of " + project + "..." : "Building the maps of " + project + "...", delegate(Installer inst)
            {
                int code = DevRunner.Run(bash, script, full, workDir, env, delegate(string line)
                {
                    if (line.StartsWith("***")) log.Warn(line); else log.Output(line);
                }, null);
                string meaning = "Build finished: " + DevRunner.Explain(code);
                if (code == 0) log.Good(meaning);
                else if (code == 99) log.Block(meaning);
                else log.Warn(meaning);
            }, null, null);
        }
#endif
    }
}
