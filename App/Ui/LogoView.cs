// The animated logo: the Split Studio frosted-glass ball with a B. The same per-pixel swirl as the Studio's logo, in sunflower colours:
// the colour turns round the arms of a swirl, the far side of the glass shows through turning the other way, slow sparkles.
// On start, and while a map installs, the colour grows from grey out of the middle. Every change is a slow fade: nothing flashes.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace BodycamMapInstaller.Ui
{
    class LogoView : Control
    {
        // Same formula as the Studio. Spin and Wave are stronger than its 0.35 / 0.30: at 44 px those read as a still ball (measured 2.5 vs 7.0 luminance change a second).
        const float Spin = 0.70f, Wave = 0.45f, Twist = 5.5f, BackLight = 0.24f;
        const int Fps = 24, MaxSparks = 40;
        static readonly float[,] Bands = { { 120, 72, 0 }, { 176, 112, 0 }, { 222, 156, 0 }, { 255, 203, 5 }, { 255, 220, 70 }, { 255, 236, 140 }, { 255, 250, 214 } };
        static readonly float[] BColor = { 34, 16, 4 }, BShadow = { 150, 78, 10 };

        readonly Timer timer = new Timer();
        readonly DateTime start = DateTime.UtcNow;
        readonly Random rnd = new Random(7);
        Bitmap frame;
        int n;
        float[] band, seed, sheen, dn, ca, gs, hs;
        byte[] on;
        int[] pixels, glass;
        DateTime growFrom = DateTime.UtcNow;
        double growTime = 1.4;
        bool working, finishing;
        double finishAt, finishFrom;
        DateTime lastSpark = DateTime.UtcNow;

        struct Spark { public float X, Y, Vx, Vy, Age, Life; public int Band; public bool Live; }
        readonly Spark[] sparks = new Spark[MaxSparks];
        float sparkDebt;

        public bool Still { get; set; }
        public double LastPaintMs { get; private set; }

        public LogoView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Window;
            TabStop = false;
            AccessibleName = "Bodycam Map Importer logo";
            AccessibleRole = AccessibleRole.Graphic;
            Load();
            timer.Interval = 1000 / Fps;
            timer.Tick += delegate { if (Visible && !Still && Width > 0) Invalidate(); };
        }

        /// <summary>The colour grows out of the middle while a map installs, then finishes smoothly.</summary>
        public bool Working
        {
            get { return working; }
            set
            {
                if (working == value) return;
                working = value;
                if (value) { finishing = false; growFrom = DateTime.UtcNow; growTime = 6; }
                else { finishFrom = Grow(); finishing = true; finishAt = Clock() + 0.6; }
            }
        }

        public void StartAnimating() { if (!Still && SystemInformation.UIEffectsEnabled) timer.Start(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { timer.Dispose(); if (frame != null) frame.Dispose(); }
            base.Dispose(disposing);
        }

        double Clock() { return (DateTime.UtcNow - start).TotalSeconds; }

        void Load()
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo_map.png"))
            {
                if (s == null) return;
                using (Bitmap b = new Bitmap(s))
                {
                    n = b.Width;
                    int count = n * n;
                    int[] src = new int[count];
                    Rectangle all = new Rectangle(0, 0, n, n);
                    BitmapData d = b.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                    Marshal.Copy(d.Scan0, src, 0, count);
                    b.UnlockBits(d);
                    band = new float[count]; seed = new float[count]; sheen = new float[count]; dn = new float[count]; ca = new float[count];
                    gs = new float[count]; hs = new float[count]; on = new byte[count]; pixels = new int[count];
                    double r = n * 0.46, c = n / 2.0 - 0.5, m = Math.Sqrt(0.45 * 0.45 + 0.55 * 0.55 + 0.7 * 0.7);
                    double lx = c - 0.45 / m * r, ly = c - 0.55 / m * r;
                    System.Collections.Generic.List<int> spots = new System.Collections.Generic.List<int>();
                    Random pr = new Random(12345);
                    for (int i = 0; i < count; i++)
                    {
                        int v = src[i], x = i % n, y = i / n;
                        byte a = (byte)((v >> 24) & 255);
                        on[i] = a;
                        band[i] = Math.Min(6, (int)Math.Round(((v >> 16) & 255) / 36.0)) / 6f;
                        gs[i] = ((v >> 8) & 255) / 255f;
                        hs[i] = (v & 255) / 255f;
                        seed[i] = (float)pr.NextDouble();
                        double q = Math.Sqrt((x - lx) * (x - lx) + (y - ly) * (y - ly)) / (r * 0.9);
                        double sh = Math.Max(0, 1 - q);
                        sheen[i] = (float)(sh * sh * sh);
                        dn[i] = (float)(Math.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / r);
                        ca[i] = (float)Math.Atan2(y - c, x - c);
                        if (a != 0 && gs[i] == 0 && hs[i] == 0 && dn[i] < 0.92f) spots.Add(i);
                    }
                    glass = spots.ToArray();
                    frame = new Bitmap(n, n, PixelFormat.Format32bppArgb);
                }
            }
        }

        double Grow()
        {
            if (Still) return 9;
            double now = Clock();
            if (finishing)
            {
                double left = finishAt - now;
                if (left <= 0) { finishing = false; growTime = 0; return 9; }
                double t = 1 - left / 0.6, e = t * t * (3 - 2 * t);
                return finishFrom + (1.3 - finishFrom) * e;
            }
            if (growTime <= 0) return 9;
            double el = (DateTime.UtcNow - growFrom).TotalSeconds;
            double g = 1.25 * (1 - Math.Exp(-3 * el / growTime));
            if (!working && g > 1.24) { growTime = 0; return 9; }
            return g;
        }

        void PaintFrame(double t, double grow)
        {
            float ph = Still ? 0 : (float)(t * Spin);
            double sk = 0.5 + 0.5 * Math.Sin(t * 0.25), sx = Still ? 0 : sk * 2;
            int si = Math.Min(1, (int)sx);
            float sf = (float)(sx - si);
            float[] a0 = si == 0 ? BColor : new float[] { Bands[0, 0], Bands[0, 1], Bands[0, 2] };
            float[] a1 = si == 0 ? new float[] { Bands[0, 0], Bands[0, 1], Bands[0, 2] } : new float[] { Bands[1, 0], Bands[1, 1], Bands[1, 2] };
            float sc0 = a0[0] + (a1[0] - a0[0]) * sf, sc1 = a0[1] + (a1[1] - a0[1]) * sf, sc2 = a0[2] + (a1[2] - a0[2]) * sf;
            float grown = (float)grow;
            for (int p = 0; p < pixels.Length; p++)
            {
                if (on[p] == 0) { pixels[p] = 0; continue; }
                float dnp = dn[p], sw = ca[p] - dnp * Twist;
                float s1 = Fast(sw + ph), s2 = Fast(-sw - ph * 0.8f);
                float u = band[p] + Wave * s1, ub = band[p] * 0.7f + 0.15f + Wave * s2;
                u = u < 0 ? 0 : (u > 1 ? 1 : u); ub = ub < 0 ? 0 : (ub > 1 ? 1 : ub);
                float fx = u * 6, kx = ub * 6;
                int fi = (int)fx; if (fi > 5) fi = 5;
                int ki = (int)kx; if (ki > 5) ki = 5;
                float ff = fx - fi, kf = kx - ki;
                float f0 = Bands[fi, 0] + (Bands[fi + 1, 0] - Bands[fi, 0]) * ff, f1 = Bands[fi, 1] + (Bands[fi + 1, 1] - Bands[fi, 1]) * ff, f2 = Bands[fi, 2] + (Bands[fi + 1, 2] - Bands[fi, 2]) * ff;
                float k0 = Bands[ki, 0] + (Bands[ki + 1, 0] - Bands[ki, 0]) * kf, k1 = Bands[ki, 1] + (Bands[ki + 1, 1] - Bands[ki, 1]) * kf, k2 = Bands[ki, 2] + (Bands[ki + 1, 2] - Bands[ki, 2]) * kf;
                float s = gs[p], h = hs[p], o = 1 - s - h, w = sheen[p] * 70;
                if (o < 0) o = 0;
                float r = (f0 + (k0 - f0) * BackLight + w) * o + BShadow[0] * h + sc0 * s;
                float g = (f1 + (k1 - f1) * BackLight + w) * o + BShadow[1] * h + sc1 * s;
                float b = (f2 + (k2 - f2) * BackLight + w) * o + BShadow[2] * h + sc2 * s;
                if (grown < 2)
                {
                    float arm = (sw + ph) / 6.2832f; arm -= (float)Math.Floor(arm);
                    float cm = (grown - dnp - arm * 0.35f - seed[p] * 0.08f) / 0.12f;
                    cm = cm < 0 ? 0 : (cm > 1 ? 1 : cm);
                    if (cm < 1) { float lum = 0.299f * r + 0.587f * g + 0.114f * b; r = lum + (r - lum) * cm; g = lum + (g - lum) * cm; b = lum + (b - lum) * cm; }
                }
                int ir = r > 255 ? 255 : (int)r, ig = g > 255 ? 255 : (int)g, ib = b > 255 ? 255 : (int)b;
                float glassA = o * 0.80f + s + h; if (glassA > 1) glassA = 1;
                int ia = (int)(on[p] * glassA);
                pixels[p] = (ia << 24) | (ir << 16) | (ig << 8) | ib;
            }
            BitmapData d = frame.LockBits(new Rectangle(0, 0, n, n), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            Marshal.Copy(pixels, 0, d.Scan0, pixels.Length);
            frame.UnlockBits(d);
        }

        /// <summary>For the UI selftest: ms per frame over many frames, and whether time and the grey start really change the picture.</summary>
        public double Bench(int frames, out bool turns, out bool greyStart)
        {
            turns = greyStart = false;
            if (frame == null) return -1;
            bool keep = Still;
            Still = false;
            PaintFrame(10, 9);
            int[] a = (int[])pixels.Clone();
            PaintFrame(14, 9);
            int same = 0, seen = 0;
            for (int i = 0; i < a.Length; i++) if (on[i] != 0) { seen++; if (a[i] == pixels[i]) same++; }
            turns = seen > 0 && same < seen / 2;
            PaintFrame(10, 0);
            int grey = 0, solid = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (on[i] == 0 || dn[i] > 0.9f) continue;
                solid++;
                int v = pixels[i], r = (v >> 16) & 255, g = (v >> 8) & 255, b = v & 255;
                if (Math.Abs(r - g) < 3 && Math.Abs(g - b) < 3) grey++;
            }
            greyStart = solid > 0 && grey > solid * 9 / 10;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < frames; i++) PaintFrame(20 + i / (double)Fps, 9);
            double ms = sw.Elapsed.TotalMilliseconds / frames;
            Still = keep;
            return ms;
        }

        static float Fast(float x) { return (float)Math.Sin(x); }

        void StepSparks(Graphics g, Rectangle r)
        {
            DateTime now = DateTime.UtcNow;
            float dt = (float)Math.Min(0.1, (now - lastSpark).TotalSeconds);
            lastSpark = now;
            sparkDebt += dt * 5;
            float k = r.Width / 64f;
            while (sparkDebt >= 1 && glass != null && glass.Length > 0)
            {
                sparkDebt -= 1;
                for (int i = 0; i < MaxSparks; i++)
                    if (!sparks[i].Live)
                    {
                        int p = glass[rnd.Next(glass.Length)];
                        double a = rnd.NextDouble() * 6.2832, sp = 1.5 + rnd.NextDouble() * 2.5;
                        sparks[i].X = (p % n + 0.5f) * 64f / n; sparks[i].Y = (p / n + 0.5f) * 64f / n;
                        sparks[i].Vx = (float)(Math.Cos(a) * sp); sparks[i].Vy = (float)(Math.Sin(a) * sp);
                        sparks[i].Age = 0; sparks[i].Life = 1.8f + (float)rnd.NextDouble(); sparks[i].Band = 3 + rnd.Next(4); sparks[i].Live = true;
                        break;
                    }
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 0; i < MaxSparks; i++)
            {
                if (!sparks[i].Live) continue;
                sparks[i].Age += dt;
                if (sparks[i].Age >= sparks[i].Life) { sparks[i].Live = false; continue; }
                float drag = 1 - 0.3f * dt;
                sparks[i].Vx *= drag; sparks[i].Vy *= drag; sparks[i].X += sparks[i].Vx * dt; sparks[i].Y += sparks[i].Vy * dt;
                float life = sparks[i].Age / sparks[i].Life, alpha = 0.85f * (1 - life), size = (2 - life) * k * 0.9f;
                float x = r.X + sparks[i].X * k, y = r.Y + sparks[i].Y * k;
                int b = sparks[i].Band;
                using (Pen pen = new Pen(Color.FromArgb((int)(alpha * 255), (int)Bands[b, 0], (int)Bands[b, 1], (int)Bands[b, 2]), Math.Max(1f, k * 0.45f)))
                {
                    g.DrawLine(pen, x - size, y, x + size, y);
                    g.DrawLine(pen, x, y - size, x, y + size);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(BackColor);
            if (frame == null) return;
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            PaintFrame(Clock(), Grow());
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            int side = Math.Min(Width, Height);
            Rectangle r = new Rectangle((Width - side) / 2, (Height - side) / 2, side, side);
            g.DrawImage(frame, r);
            if (!Still) StepSparks(g, r);
            LastPaintMs = sw.Elapsed.TotalMilliseconds;
        }
    }
}
