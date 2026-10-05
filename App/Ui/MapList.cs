// List of installed maps.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using BodycamMapInstaller.Core;

namespace BodycamMapInstaller.Ui
{
    class MapRow
    {
        public InstalledMap Map;
        public string Key, Name, Right, Modes, Description, Author, Source;
        public Color RightColor = Theme.TextDim, ModesColor = Theme.TextDim;
        public Color Dot = Color.Empty;
        public bool Dim;
        public Image Thumb;
    }

    class MapList : Control
    {
        readonly List<MapRow> rows = new List<MapRow>();
        public event EventHandler SelectionChanged;
        public event EventHandler RemoveRequested;
        int selected = -1;
        int scroll;

        public MapList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            BackColor = Theme.Panel;
            TabStop = true;
            AccessibleRole = AccessibleRole.List;
            AccessibleName = "Installed maps";
        }

        int RowH { get { return Theme.BodySemi.Height + Theme.Small.Height + Theme.Px(14); } }
        int VisibleRows { get { return Math.Max(1, Height / RowH); } }

        public IList<MapRow> Rows { get { return rows; } }

        public void SetRows(IList<MapRow> newRows)
        {
            string keep = SelectedRow != null ? SelectedRow.Key : null;
            foreach (MapRow r in rows)
            {
                bool reused = false;
                foreach (MapRow n in newRows) if (n.Thumb != null && ReferenceEquals(n.Thumb, r.Thumb)) reused = true;
                if (!reused && r.Thumb != null) r.Thumb.Dispose();
            }
            rows.Clear();
            rows.AddRange(newRows);
            int idx = -1;
            for (int i = 0; i < rows.Count; i++) if (keep != null && string.Equals(rows[i].Key, keep, StringComparison.OrdinalIgnoreCase)) idx = i;
            if (idx < 0 && rows.Count > 0) idx = 0;
            selected = idx;
            scroll = Math.Max(0, Math.Min(scroll, rows.Count - VisibleRows));
            EnsureVisible();
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        public int Selected
        {
            get { return selected; }
            set
            {
                int v = rows.Count == 0 ? -1 : Math.Max(0, Math.Min(rows.Count - 1, value));
                if (v == selected) return;
                selected = v;
                EnsureVisible();
                Invalidate();
                if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
            }
        }

        public MapRow SelectedRow { get { return selected >= 0 && selected < rows.Count ? rows[selected] : null; } }

        void EnsureVisible()
        {
            if (selected < 0) return;
            if (selected < scroll) scroll = selected;
            if (selected >= scroll + VisibleRows) scroll = selected - VisibleRows + 1;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            scroll = Math.Max(0, Math.Min(scroll, rows.Count - VisibleRows));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled) return;
            Focus();
            int i = scroll + e.Y / RowH;
            if (i >= 0 && i < rows.Count) Selected = i;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            scroll = Math.Max(0, Math.Min(Math.Max(0, rows.Count - VisibleRows), scroll + (e.Delta > 0 ? -1 : 1)));
            Invalidate();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            return keyData == Keys.Up || keyData == Keys.Down || keyData == Keys.Home || keyData == Keys.End || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!Enabled || rows.Count == 0) return;
            if (e.KeyCode == Keys.Up) Selected = Math.Max(0, selected - 1);
            else if (e.KeyCode == Keys.Down) Selected = selected + 1;
            else if (e.KeyCode == Keys.Home) Selected = 0;
            else if (e.KeyCode == Keys.End) Selected = rows.Count - 1;
            else if (e.KeyCode == Keys.Delete && RemoveRequested != null) RemoveRequested(this, EventArgs.Empty);
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }

        protected override AccessibleObject CreateAccessibilityInstance() { return new ListAccessible(this); }

        sealed class ListAccessible : ControlAccessibleObject
        {
            readonly MapList list;
            public ListAccessible(MapList list) : base(list) { this.list = list; }
            public override AccessibleRole Role { get { return AccessibleRole.List; } }
            public override int GetChildCount() { return list.rows.Count; }
            public override AccessibleObject GetChild(int index) { return index >= 0 && index < list.rows.Count ? new RowAccessible(list, index) : null; }
            public override AccessibleObject GetSelected() { return list.selected >= 0 ? new RowAccessible(list, list.selected) : null; }
        }

        sealed class RowAccessible : AccessibleObject
        {
            readonly MapList list;
            readonly int index;
            public RowAccessible(MapList list, int index) { this.list = list; this.index = index; }
            MapRow R { get { return list.rows[index]; } }
            public override string Name { get { return R.Name + (string.IsNullOrEmpty(R.Right) ? "" : ", " + R.Right) + (string.IsNullOrEmpty(R.Modes) ? "" : ", " + R.Modes); } }
            public override AccessibleRole Role { get { return AccessibleRole.ListItem; } }
            public override AccessibleObject Parent { get { return list.AccessibilityObject; } }
            public override string DefaultAction { get { return "Select"; } }
            public override void DoDefaultAction() { list.Selected = index; }
            public override AccessibleStates State
            {
                get { return AccessibleStates.Selectable | (index == list.selected ? AccessibleStates.Selected : AccessibleStates.None); }
            }
            public override Rectangle Bounds
            {
                get { int rh = list.RowH; return list.RectangleToScreen(new Rectangle(0, (index - list.scroll) * rh, list.Width, rh)); }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            if (rows.Count == 0)
            {
                Theme.Draw(g, "No custom maps installed yet.", Theme.Small, Theme.TextDim, new RectangleF(0, 0, Width, Height), StringAlignment.Center, StringAlignment.Center);
                return;
            }
            int rh = RowH, pad = Theme.Px(14);
            bool more = rows.Count > VisibleRows;
            int barW = more ? Theme.Px(4) : 0;
            for (int i = scroll; i < rows.Count && (i - scroll) * rh < Height; i++)
            {
                MapRow m = rows[i];
                Rectangle r = new Rectangle(0, (i - scroll) * rh, Width - barW, rh);
                if (i == selected)
                {
                    using (Brush b = new SolidBrush(Enabled ? Theme.Selected : Theme.Mix(Theme.Panel, Theme.Selected, 0.5f))) g.FillRectangle(b, r);
                    using (Brush b = new SolidBrush(Enabled ? Theme.Green : Theme.Border)) g.FillRectangle(b, 0, r.Y, Theme.Px(3), rh);
                }
                Color nameColor = !Enabled || m.Dim ? Theme.TextDim : Theme.Text;
                string right = m.Right ?? "";
                float rw = right.Length == 0 ? 0 : g.MeasureString(right, Theme.Small).Width + 2;
                int top = r.Y + Theme.Px(6), line1 = Theme.BodySemi.Height;
                int rightEdge = r.Right - pad;
                Theme.Draw(g, right, Theme.Small, Enabled ? m.RightColor : Theme.TextDim, new RectangleF(rightEdge - rw, top, rw, line1), StringAlignment.Far, StringAlignment.Center);
                float nameRight = rightEdge - rw - Theme.Px(12);
                if (m.Dot != Color.Empty)
                {
                    int dot = Theme.Px(7);
                    float dx = rightEdge - rw - dot - Theme.Px(4);
                    using (Brush b = new SolidBrush(m.Dot)) g.FillEllipse(b, dx, top + (line1 - dot) / 2f, dot, dot);
                    nameRight = dx - Theme.Px(8);
                }
                Theme.Draw(g, m.Name, Theme.BodySemi, nameColor, new RectangleF(pad, top, Math.Max(0, nameRight - pad), line1), StringAlignment.Near, StringAlignment.Center);
                Theme.Draw(g, m.Modes, Theme.Small, Enabled ? m.ModesColor : Theme.TextDim, new RectangleF(pad, top + line1 + Theme.Px(1), r.Width - pad * 2, Theme.Small.Height), StringAlignment.Near, StringAlignment.Center);
                using (Pen p = new Pen(Theme.Window)) g.DrawLine(p, 0, r.Bottom - 1, r.Width, r.Bottom - 1);
            }
            if (more)
            {
                int trackH = Height;
                int thumbH = Math.Max(Theme.Px(24), trackH * VisibleRows / rows.Count);
                int maxScroll = Math.Max(1, rows.Count - VisibleRows);
                int y = (trackH - thumbH) * Math.Min(scroll, maxScroll) / maxScroll;
                using (Brush b = new SolidBrush(Theme.Border)) g.FillRectangle(b, Width - barW, y, barW, thumbH);
            }
            if (Focused && ShowFocusCues)
                using (Pen p = new Pen(Theme.Border, Math.Max(1, Theme.Px(1)))) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }
}
