// Flat button that keeps the dark style in every state.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BodycamMapInstaller.Ui
{
    class FlatButton : Button
    {
        bool hover, pressed;
        float glow;
        Timer fader;
        // what is behind the rounded corners (the card it sits on)
        public Color Back = Theme.Window;
        public bool Primary;
        public bool Danger;
        // Solid: the whole button is filled green (Primary) or red (Danger) with white text, so a first-time user
        // cannot miss COOK and DELETE (22 Sep 2026). Buttons without it keep the quiet dark style.
        public bool Solid;

        public FlatButton(string text)
        {
            Text = text;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            BackColor = Theme.PanelRaised;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
            UseMnemonic = false;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public int Preferred { get { return TextRenderer.MeasureText(Text, Font).Width + Theme.Px(28); } }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Fade(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; pressed = false; Fade(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }

        // the hover fades in and out (the Animator's calm transitions)
        void Fade()
        {
            if (fader == null)
            {
                fader = new Timer();
                fader.Interval = 16;
                fader.Tick += delegate
                {
                    float goal = hover ? 1f : 0f;
                    glow += Math.Sign(goal - glow) * 0.08f;
                    if (Math.Abs(goal - glow) < 0.08f) { glow = goal; fader.Stop(); }
                    Invalidate();
                };
            }
            fader.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && fader != null) fader.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Color fill = Theme.PanelRaised, line = Theme.Border, text = Theme.Text;
            if (Enabled)
            {
                line = Theme.Mix(Theme.Border, Theme.Accent, glow);
                if (Solid && !Danger) { fill = Theme.Accent; line = Theme.Accent; text = Theme.OnAccent; }
                else if (Solid) { fill = Theme.Red; line = Theme.Red; text = Color.White; }
                else if (Danger) text = Theme.RedText;
                else if (Primary) text = Theme.Accent;
                if (Solid) fill = Theme.Mix(fill, Color.White, 0.10f * glow);
                if (pressed) fill = Theme.Mix(fill, Theme.Window, 0.25f);
            }
            else text = Theme.Mix(Theme.PanelRaised, Theme.TextDim, 0.7f);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Rounded(r, Theme.Px(9)))
            {
                using (Brush b = new SolidBrush(fill)) g.FillPath(b, p);
                bool keyboardFocus = Focused && ShowFocusCues;
                using (Pen pen = new Pen(keyboardFocus ? Theme.Accent : line, Math.Max(1, Theme.Px(1)))) g.DrawPath(pen, p);
            }
            Font f = Solid ? Theme.BodySemi : Font;
            TextRenderer.DrawText(g, Text, f, new Rectangle(0, 0, Width, Height), text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
