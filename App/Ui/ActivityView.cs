// Activity panel: the log lines shown at the bottom of the window.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace BodycamMapInstaller.Ui
{
    class ActivityView : Control
    {
        sealed class Line { public string Stamp, FullStamp, Text; public Color Color; public bool Hidden; }
        readonly List<Line> lines = new List<Line>();
        readonly List<Line> shown = new List<Line>();
        int offsetFromBottom;
        const int MaxLines = 5000;

        public ActivityView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Panel;
            TabStop = true;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Activity";
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Copy all", null, delegate
            {
                try { Clipboard.SetText(AllText()); }
                catch (Exception) { }
            });
            ContextMenuStrip = menu;
        }

        int LineH { get { return Theme.Small.Height + Theme.Px(4); } }
        int VisibleLines { get { return Math.Max(1, (Height - Theme.Px(12)) / LineH); } }

        public int Count { get { return shown.Count; } }

        static string ShortStamp(DateTime when)
        {
            string pattern = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern;
            try { return when.ToString(pattern, CultureInfo.CurrentCulture); }
            catch (FormatException) { return when.ToString("H:mm", CultureInfo.InvariantCulture); }
        }

        public void Add(string text, Color color, DateTime when, bool hidden)
        {
            string[] parts = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string part in parts)
            {
                Line l = new Line();
                l.Stamp = ShortStamp(when);
                l.FullStamp = when.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                l.Text = part.Replace("\t", "    ");
                l.Color = color;
                l.Hidden = hidden;
                lines.Add(l);
                if (!hidden)
                {
                    shown.Add(l);
                    if (offsetFromBottom > 0) offsetFromBottom++;
                }
            }
            if (lines.Count > MaxLines)
            {
                List<Line> drop = lines.GetRange(0, lines.Count - MaxLines);
                lines.RemoveRange(0, lines.Count - MaxLines);
                foreach (Line d in drop) if (!d.Hidden) shown.Remove(d);
            }
            offsetFromBottom = Math.Min(offsetFromBottom, Math.Max(0, shown.Count - VisibleLines));
            Invalidate();
        }

        public string ShownText()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Line l in shown) sb.Append(l.Stamp).Append("  ").Append(l.Text).Append("\r\n");
            return sb.ToString();
        }

        public string AllText()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Line l in lines) sb.Append(l.FullStamp).Append(l.Hidden ? "  (detail) " : "  ").Append(l.Text).Append("\r\n");
            return sb.ToString();
        }

        void ScrollBy(int delta)
        {
            offsetFromBottom = Math.Max(0, Math.Min(Math.Max(0, shown.Count - VisibleLines), offsetFromBottom + delta));
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(e.Delta > 0 ? 3 : -3);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.PageUp || keyData == Keys.PageDown || keyData == Keys.Home || keyData == Keys.End || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Up) ScrollBy(1);
            else if (e.KeyCode == Keys.Down) ScrollBy(-1);
            else if (e.KeyCode == Keys.PageUp) ScrollBy(VisibleLines - 1);
            else if (e.KeyCode == Keys.PageDown) ScrollBy(-(VisibleLines - 1));
            else if (e.KeyCode == Keys.Home) ScrollBy(shown.Count);
            else if (e.KeyCode == Keys.End) ScrollBy(-shown.Count);
            else if (e.Control && e.KeyCode == Keys.C) { try { Clipboard.SetText(AllText()); } catch (Exception) { } }
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            int lh = LineH, pad = Theme.Px(12);
            int visible = VisibleLines;
            int last = shown.Count - 1 - offsetFromBottom;
            int first = Math.Max(0, last - visible + 1);
            float stampW = 0;
            for (int i = first; i <= last; i++) stampW = Math.Max(stampW, g.MeasureString(shown[i].Stamp, Theme.Small).Width);
            stampW += Theme.Px(8);
            int y = Theme.Px(6);
            bool more = shown.Count > visible;
            int barW = more ? Theme.Px(4) : 0;
            Color stampColor = Theme.TextDim;
            for (int i = first; i <= last; i++)
            {
                Theme.Draw(g, shown[i].Stamp, Theme.Small, stampColor, new RectangleF(pad, y, stampW, lh), StringAlignment.Near, StringAlignment.Center);
                Theme.Draw(g, shown[i].Text, Theme.Small, shown[i].Color, new RectangleF(pad + stampW, y, Width - pad * 2 - stampW - barW, lh), StringAlignment.Near, StringAlignment.Center);
                y += lh;
            }
            if (more)
            {
                int trackH = Height - Theme.Px(8);
                int thumbH = Math.Max(Theme.Px(20), trackH * visible / shown.Count);
                int maxOff = Math.Max(1, shown.Count - visible);
                int ty = Theme.Px(4) + (trackH - thumbH) * (maxOff - Math.Min(offsetFromBottom, maxOff)) / maxOff;
                using (Brush b = new SolidBrush(Theme.Border)) g.FillRectangle(b, Width - barW - Theme.Px(3), ty, barW, thumbH);
            }
            if (Focused && ShowFocusCues)
                using (Pen p = new Pen(Theme.Border, Math.Max(1, Theme.Px(1)))) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }
}
