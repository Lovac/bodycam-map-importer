// Details panel: photo, name and description of the selected map.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BodycamMapInstaller.Ui
{
    class DetailsView : Control
    {
        MapRow row;

        public DetailsView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Window;
            AccessibleRole = AccessibleRole.StaticText;
            AccessibleName = "Select a map to see its details.";
        }

        // The photo is clickable: choosing a picture is the only way to change a card's image (22 Sep 2026).
        public event EventHandler ThumbClick;
        Rectangle thumbRect = Rectangle.Empty;
        bool hoverThumb;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = thumbRect.Contains(e.Location) && ThumbClick != null;
            Cursor = over ? Cursors.Hand : Cursors.Default;
            if (over != hoverThumb) { hoverThumb = over; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverThumb) { hoverThumb = false; Invalidate(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button == MouseButtons.Left && thumbRect.Contains(e.Location) && ThumbClick != null)
                ThumbClick(this, EventArgs.Empty);
        }

        public MapRow Row
        {
            get { return row; }
            set
            {
                row = value;
                AccessibleName = row == null ? "Select a map to see its details." : row.Name + ". " + row.Description + (string.IsNullOrEmpty(row.Author) ? "" : " By " + row.Author + ".");
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            if (row == null)
            {
                Theme.Draw(g, "Select a map to see its details.", Theme.Small, Theme.TextDim, new RectangleF(0, 0, Width, Theme.Small.Height + Theme.Px(4)), StringAlignment.Near, StringAlignment.Center);
                return;
            }

            bool photo = row.Thumb != null || (row.Map != null && row.Map.Kind == BodycamMapInstaller.Core.MapKind.Card);
            int tw = Theme.Px(128), th = Theme.Px(64);
            int x = 0;
            thumbRect = photo ? new Rectangle(0, 0, tw, th) : Rectangle.Empty;
            if (photo)
            {
                using (Brush b = new SolidBrush(Theme.PanelRaised)) g.FillRectangle(b, new Rectangle(0, 0, tw, th));
                if (row.Thumb != null)
                {
                    float s = Math.Min((float)tw / row.Thumb.Width, (float)th / row.Thumb.Height);
                    float w = row.Thumb.Width * s, h = row.Thumb.Height * s;
                    g.DrawImage(row.Thumb, new RectangleF((tw - w) / 2f, (th - h) / 2f, w, h));
                }
                else
                    using (Pen p = new Pen(Theme.Border, Math.Max(1, Theme.Px(2))))
                    {
                        p.StartCap = LineCap.Round; p.EndCap = LineCap.Round;
                        int cx = tw / 2, cy = th / 2, k = Theme.Px(1);
                        g.DrawLine(p, cx - 14 * k, cy + 8 * k, cx - 4 * k, cy - 4 * k);
                        g.DrawLine(p, cx - 4 * k, cy - 4 * k, cx + 4 * k, cy + 4 * k);
                        g.DrawLine(p, cx + 4 * k, cy + 4 * k, cx + 14 * k, cy - 6 * k);
                    }
                // hovering the photo says what a click does, so nobody has to guess that it is a button
                if (hoverThumb && ThumbClick != null)
                {
                    int sh = Theme.Small.Height + Theme.Px(6);
                    using (Brush b = new SolidBrush(Color.FromArgb(200, Theme.Window))) g.FillRectangle(b, new Rectangle(0, th - sh, tw, sh));
                    Theme.Draw(g, "change picture", Theme.Small, Theme.Text, new RectangleF(0, th - sh, tw, sh), StringAlignment.Center, StringAlignment.Center);
                }
                x = tw + Theme.Px(12);
            }
            float y = -Theme.Px(2);
            float nameH = Theme.BodySemi.GetHeight(g) + Theme.Px(2), smallH = Theme.Small.GetHeight(g) + Theme.Px(2);
            Theme.Draw(g, row.Name, Theme.BodySemi, Theme.Text, new RectangleF(x, y, Width - x, nameH), StringAlignment.Near, StringAlignment.Center);
            y += nameH;
            Theme.Draw(g, row.Description, Theme.Small, Theme.TextDim, new RectangleF(x, y, Width - x, smallH), StringAlignment.Near, StringAlignment.Center);
            y += smallH;
            string by = "";
            if (!string.IsNullOrEmpty(row.Author)) by = "by " + row.Author;
            if (!string.IsNullOrEmpty(row.Source)) by += (by.Length > 0 ? "   " + Theme.Dot + "   " : "") + row.Source;
            Theme.Draw(g, by, Theme.Small, Theme.TextDim, new RectangleF(x, y, Width - x, smallH), StringAlignment.Near, StringAlignment.Center);
        }
    }
}
