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

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; pressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color parentBack = Parent != null ? Parent.BackColor : Theme.Window;
            g.Clear(parentBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Color fill = Primary ? Theme.Selected : Theme.PanelRaised;
            if (Enabled && hover) fill = Primary ? Theme.Mix(Theme.Selected, Theme.Green, 0.18f) : Theme.Border;
            if (Enabled && pressed) fill = Theme.Selected;
            if (Solid && Enabled)
            {
                Color solid = Danger ? Theme.Red : Theme.Mix(Theme.Green, Theme.Window, 0.28f);
                fill = pressed ? Theme.Mix(solid, Theme.Window, 0.25f) : (hover ? Theme.Mix(solid, Color.White, 0.12f) : solid);
            }
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath p = Theme.Rounded(r, Theme.Px(4)))
            {
                using (Brush b = new SolidBrush(fill)) g.FillPath(b, p);
                bool keyboardFocus = Focused && ShowFocusCues;
                if (keyboardFocus || (Primary && Enabled && !Solid))
                    using (Pen pen = new Pen(keyboardFocus ? Theme.Text : Theme.Mix(Theme.Selected, Theme.Green, 0.45f), Math.Max(1, Theme.Px(1))))
                        g.DrawPath(pen, p);
            }
            Color text = !Enabled ? Theme.Mix(Theme.PanelRaised, Theme.TextDim, 0.7f) : (Solid ? Color.White : (Danger ? Theme.RedText : (Primary ? Theme.Green : Theme.Text)));
            TextRenderer.DrawText(g, Text, Font, new Rectangle(0, 0, Width, Height), text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
