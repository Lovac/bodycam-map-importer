// Colors, fonts and drawing helpers.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BodycamMapInstaller.Ui
{
    static class Theme
    {
        public static readonly Color Window = ColorTranslator.FromHtml("#16171B");
        public static readonly Color Panel = ColorTranslator.FromHtml("#1F2026");
        public static readonly Color PanelRaised = ColorTranslator.FromHtml("#26282F");
        public static readonly Color Selected = ColorTranslator.FromHtml("#2B3530");
        public static readonly Color Border = ColorTranslator.FromHtml("#3A3D47");
        public static readonly Color Text = ColorTranslator.FromHtml("#E8E8EA");
        public static readonly Color TextDim = ColorTranslator.FromHtml("#9A9CA5");
        public static readonly Color Green = ColorTranslator.FromHtml("#4CC38A");
        public static readonly Color Amber = ColorTranslator.FromHtml("#E0B341");
        public static readonly Color Red = ColorTranslator.FromHtml("#E5484D");
        public static readonly Color RedText = ColorTranslator.FromHtml("#FF8A8D");
        public static readonly Color Blue = ColorTranslator.FromHtml("#5B9DFF");

        public static readonly Font Body = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font BodySemi = new Font("Segoe UI Semibold", 10f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Small = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Title = new Font("Segoe UI Semibold", 15f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font H2 = new Font("Segoe UI Semibold", 11f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly Font Mono = new Font("Consolas", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        public static readonly float Scale = ReadScale();

        public static readonly string Dot = ((char)0xB7).ToString();

        static float ReadScale()
        {
            try { using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return Math.Max(1f, g.DpiX / 96f); }
            catch (Exception) { return 1f; }
        }

        public static int Px(int px) { return (int)Math.Round(px * Scale); }

        public static void Draw(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h, StringAlignment v)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 0 || r.Height <= 0) return;
            using (StringFormat sf = new StringFormat(StringFormatFlags.NoWrap))
            using (Brush b = new SolidBrush(c))
            {
                sf.Alignment = h;
                sf.LineAlignment = v;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(s, f, b, r, sf);
            }
        }

        public static void DrawWrapped(Graphics g, string s, Font f, Color c, RectangleF r, StringAlignment h)
        {
            if (string.IsNullOrEmpty(s) || r.Width <= 0 || r.Height <= 0) return;
            using (StringFormat sf = new StringFormat(StringFormatFlags.LineLimit))
            using (Brush b = new SolidBrush(c))
            {
                sf.Alignment = h;
                sf.LineAlignment = StringAlignment.Near;
                sf.Trimming = StringTrimming.EllipsisWord;
                g.DrawString(s, f, b, r, sf);
            }
        }

        public static GraphicsPath Rounded(Rectangle r, int rad)
        {
            GraphicsPath p = new GraphicsPath();
            int d = Math.Max(1, rad * 2);
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void DarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch (Exception) { }
        }

        static Icon appIcon;

        public static Icon AppIcon
        {
            get
            {
                if (appIcon != null) return appIcon;
                try
                {
                    using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
                        if (s != null) appIcon = new Icon(s);
                }
                catch (Exception) { appIcon = null; }
                return appIcon;
            }
        }
    }
}
