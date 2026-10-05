// The drop box: shows the current state and the result of the last drop.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BodycamMapInstaller.Ui
{
    enum DropState { Ready, Busy, Locked, NoGame, Paused, Detecting, Done, Refused }

    class DropZone : Control
    {
        DropState state = DropState.Detecting;
        bool hot, hotRefused;
        string hotHead = "Let go to install";
        string resultHead = "", resultSub = "";
        float progress = -1f;
        int phase;
        public event EventHandler Activate;

        public const string ReadyHead = "Drop a map here";
        public const string ReadySub = "The map you downloaded (.zip, .bcmap or .pak)";

        public DropZone()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.PanelRaised;
            TabStop = true;
            AccessibleRole = AccessibleRole.PushButton;
            AccessibleName = "Drop a map here, or press Enter to pick a file";
        }

        public DropState State
        {
            get { return state; }
            set
            {
                if (state == value) return;
                state = value;
                Cursor = value == DropState.Ready || value == DropState.NoGame || value == DropState.Done || value == DropState.Refused ? Cursors.Hand : Cursors.Default;
                AccessibleDescription = value == DropState.Done || value == DropState.Refused ? resultHead + ". " + resultSub : null;
                Invalidate();
            }
        }

        public bool Hot { get { return hot; } }
        public bool HotRefused { get { return hotRefused; } }
        public string ResultHead { get { return resultHead; } }
        public string ResultSub { get { return resultSub; } }

        public void SetHot(bool on, string head)
        {
            hot = on;
            hotRefused = false;
            if (head != null) hotHead = head;
            Invalidate();
        }

        public void SetHotRefused()
        {
            hot = false;
            hotRefused = true;
            Invalidate();
        }

        public void ShowResult(bool done, string head, string sub)
        {
            resultHead = head ?? "";
            resultSub = sub ?? "";
            state = DropState.Detecting;
            State = done ? DropState.Done : DropState.Refused;
        }

        public float Progress
        {
            get { return progress; }
            set { if (Math.Abs(value - progress) > 0.001f) { progress = value; Invalidate(); } }
        }

        public void Tick()
        {
            if (state != DropState.Busy && state != DropState.Detecting) return;
            phase = (phase + 1) % 30;
            if (phase % 5 == 0) Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
            if (Activate != null) Activate(this, EventArgs.Empty);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Enter || keyData == Keys.Space || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if ((e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) && Activate != null) { e.Handled = true; Activate(this, EventArgs.Empty); }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override AccessibleObject CreateAccessibilityInstance() { return new ZoneAccessible(this); }

        sealed class ZoneAccessible : ControlAccessibleObject
        {
            readonly DropZone zone;
            public ZoneAccessible(DropZone zone) : base(zone) { this.zone = zone; }
            public override AccessibleRole Role { get { return AccessibleRole.PushButton; } }
            public override string DefaultAction { get { return "Press"; } }
            public override void DoDefaultAction() { if (zone.Activate != null) zone.Activate(zone, EventArgs.Empty); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Window);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            bool refusedLook = hotRefused || (state == DropState.Refused && !hot);
            bool doneLook = state == DropState.Done && !hot && !hotRefused;
            bool dim = state != DropState.Ready && state != DropState.Done && state != DropState.Refused && !hot && !hotRefused;
            Color frame = hot ? Theme.Green : refusedLook ? Theme.Red : doneLook ? Theme.Mix(Theme.Border, Theme.Green, 0.7f)
                        : (state == DropState.Locked ? Theme.Red : (Focused && ShowFocusCues ? Theme.TextDim : Theme.Border));
            Color glyph = hot || doneLook ? Theme.Green : refusedLook ? Theme.RedText
                        : (state == DropState.Locked ? Theme.Mix(Theme.Border, Theme.Red, 0.5f) : (dim ? Theme.Border : Theme.TextDim));
            Rectangle r = new Rectangle(1, 1, Width - 3, Height - 3);
            using (GraphicsPath p = Theme.Rounded(r, Theme.Px(14)))
            {
                Color fill = hot || doneLook ? Theme.Selected : (dim ? Theme.Mix(Theme.PanelRaised, Theme.Window, 0.45f) : Theme.PanelRaised);
                using (Brush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(frame, Theme.Px(2)))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawPath(pen, p);
                }
            }
            int k = Theme.Px(1);
            int cx = Width / 2, cy = Height / 2 - Theme.Px(44);
            using (Pen pen = new Pen(glyph, Theme.Px(3)))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                if (refusedLook)
                {
                    g.DrawLine(pen, cx - 12 * k, cy - 18 * k, cx + 12 * k, cy + 6 * k);
                    g.DrawLine(pen, cx + 12 * k, cy - 18 * k, cx - 12 * k, cy + 6 * k);
                    g.DrawLine(pen, cx - 26 * k, cy + 24 * k, cx + 26 * k, cy + 24 * k);
                }
                else if (doneLook)
                {
                    g.DrawLine(pen, cx - 16 * k, cy - 4 * k, cx - 5 * k, cy + 8 * k);
                    g.DrawLine(pen, cx - 5 * k, cy + 8 * k, cx + 17 * k, cy - 16 * k);
                    g.DrawLine(pen, cx - 26 * k, cy + 24 * k, cx + 26 * k, cy + 24 * k);
                }
                else if (!hot && (state == DropState.Busy || state == DropState.Detecting))
                {
                    int dot = Theme.Px(7);
                    int lit = (phase / 10) % 3;
                    for (int i = -1; i <= 1; i++)
                        using (Brush b = new SolidBrush(i + 1 == lit ? Theme.Mix(glyph, Theme.Text, 0.6f) : glyph))
                            g.FillEllipse(b, cx + i * Theme.Px(18) - dot / 2, cy - dot / 2, dot, dot);
                    if (state == DropState.Busy && progress >= 0)
                    {
                        int bw = Theme.Px(180), bh = Theme.Px(4), bx = cx - bw / 2, byy = cy + 22 * k;
                        using (GraphicsPath track = Theme.Rounded(new Rectangle(bx, byy, bw, bh), bh / 2))
                        using (Brush b = new SolidBrush(Theme.Border)) g.FillPath(b, track);
                        int fw = Math.Max(bh, (int)(bw * progress));
                        using (GraphicsPath bar = Theme.Rounded(new Rectangle(bx, byy, fw, bh), bh / 2))
                        using (Brush b = new SolidBrush(Theme.Green)) g.FillPath(b, bar);
                    }
                    else g.DrawLine(pen, cx - 26 * k, cy + 24 * k, cx + 26 * k, cy + 24 * k);
                }
                else
                {
                    g.DrawLine(pen, cx, cy - 26 * k, cx, cy + 10 * k);
                    g.DrawLine(pen, cx - 14 * k, cy - 4 * k, cx, cy + 10 * k);
                    g.DrawLine(pen, cx + 14 * k, cy - 4 * k, cx, cy + 10 * k);
                    g.DrawLine(pen, cx - 26 * k, cy + 24 * k, cx + 26 * k, cy + 24 * k);
                }
            }
            string head = ReadyHead, sub = ReadySub, hint = "or click to browse";
            Color headColor = dim ? Theme.TextDim : Theme.Text, subColor = Theme.TextDim;
            bool wrapSub = false;
            if (state == DropState.Busy) { head = "Working..."; sub = "Keep Bodycam closed until this finishes"; hint = ""; }
            if (state == DropState.Detecting) { head = "Looking for Bodycam..."; sub = ""; hint = ""; }
            if (state == DropState.Locked) { head = "Close Bodycam first"; sub = "Maps can be added or removed while the game is closed"; hint = ""; }
            if (state == DropState.NoGame) { head = "Bodycam not found yet"; sub = "Click Browse... above and pick your Bodycam folder"; hint = ""; }
            if (state == DropState.Paused) { head = "Custom maps are hidden"; sub = "Show them again to install or remove maps"; hint = ""; }
            if (state == DropState.Done) { head = resultHead; sub = resultSub; hint = "or drop another map"; headColor = Theme.Green; subColor = Theme.Text; }
            if (state == DropState.Refused) { head = resultHead; sub = resultSub; hint = "or click to pick another file"; headColor = Theme.RedText; subColor = Theme.Text; wrapSub = true; }
            if (hot) { head = hotHead; sub = ""; hint = ""; headColor = Theme.Green; }
            if (hotRefused) { head = "Not a map file"; sub = "Drop the .zip you downloaded, or a .bcmap or .pak"; hint = ""; headColor = Theme.RedText; subColor = Theme.Text; wrapSub = false; }
            float y = cy + Theme.Px(40);
            float th = Theme.Title.GetHeight(g), bh2 = Theme.Body.GetHeight(g), sh = Theme.Small.GetHeight(g);
            Theme.Draw(g, head, Theme.Title, headColor, new RectangleF(8, y, Width - 16, th + 4), StringAlignment.Center, StringAlignment.Center);
            y += th + Theme.Px(8);
            if (wrapSub)
            {
                RectangleF box = new RectangleF(Theme.Px(24), y, Width - Theme.Px(48), bh2 * 2 + 6);
                Theme.DrawWrapped(g, sub, Theme.Body, subColor, box, StringAlignment.Center);
                y += bh2 * 2 + Theme.Px(4);
            }
            else
            {
                Theme.Draw(g, sub, Theme.Body, subColor, new RectangleF(8, y, Width - 16, bh2 + 4), StringAlignment.Center, StringAlignment.Center);
                y += bh2 + Theme.Px(2);
            }
            Theme.Draw(g, hint, Theme.Small, Theme.TextDim, new RectangleF(8, y, Width - 16, sh + 4), StringAlignment.Center, StringAlignment.Center);
        }
    }
}
