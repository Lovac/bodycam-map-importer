// Dark-styled question dialogs with keyboard-safe default buttons.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Windows.Forms;

namespace BodycamMapInstaller.Ui
{
    class DarkDialog : Form
    {
        int answer;
        readonly int cancelIndex, defaultIndex;
        readonly List<FlatButton> made = new List<FlatButton>();

        DarkDialog(string title, string body, string[] buttons) : this(title, body, buttons, 0, buttons.Length - 1, -1) { }

        DarkDialog(string title, string body, string[] buttons, int defaultIdx, int cancelIdx, int dangerIdx)
        {
            Text = title;
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            KeyPreview = true;
            if (Theme.AppIcon != null) Icon = Theme.AppIcon; else ShowIcon = false;
            cancelIndex = Math.Max(0, Math.Min(buttons.Length - 1, cancelIdx));
            defaultIndex = Math.Max(0, Math.Min(buttons.Length - 1, defaultIdx));
            answer = cancelIndex;

            int pad = Theme.Px(24), width = Theme.Px(470);
            Label head = new Label();
            head.Text = title;
            head.Font = Theme.H2;
            head.ForeColor = Theme.Text;
            head.UseMnemonic = false;
            head.AutoSize = false;
            Size hs = TextRenderer.MeasureText(title, Theme.H2, new Size(width, 0), TextFormatFlags.WordBreak);
            head.SetBounds(pad, pad - Theme.Px(2), width, hs.Height + Theme.Px(2));

            Label text = new Label();
            text.Text = body;
            text.Font = Theme.Body;
            text.ForeColor = Theme.TextDim;
            text.UseMnemonic = false;
            text.AutoSize = false;
            Size bs = TextRenderer.MeasureText(body, Theme.Body, new Size(width, 0), TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
            text.SetBounds(pad, head.Bottom + Theme.Px(10), width, bs.Height + Theme.Px(4));

            int btnH = Theme.Body.Height + Theme.Px(14);
            int by = text.Bottom + Theme.Px(22);
            int total = 0;
            for (int i = 0; i < buttons.Length; i++)
            {
                FlatButton b = new FlatButton(buttons[i]);
                b.Danger = i == dangerIdx;
                b.Primary = i == defaultIndex && buttons.Length > 1 && !b.Danger;
                int idx = i;
                b.Click += delegate { answer = idx; DialogResult = DialogResult.OK; Close(); };
                made.Add(b);
                total += Math.Max(Theme.Px(96), b.Preferred);
            }
            total += Theme.Px(8) * (buttons.Length - 1);
            int bx = pad + width - total;
            foreach (FlatButton b in made)
            {
                int bw = Math.Max(Theme.Px(96), b.Preferred);
                b.SetBounds(bx, by, bw, btnH);
                bx += bw + Theme.Px(8);
            }
            Controls.Add(head);
            Controls.Add(text);
            foreach (FlatButton b in made) Controls.Add(b);
            AcceptButton = made[defaultIndex];
            ClientSize = new Size(pad * 2 + width, by + btnH + pad);
            for (int i = 0; i < made.Count; i++) made[i].TabIndex = i;
            ActiveControl = made[defaultIndex];
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.DarkTitleBar(Handle);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) { answer = cancelIndex; DialogResult = DialogResult.Cancel; Close(); }
        }

        public static bool Render(string png, string which)
        {
            string[] t = which == "dialog-build"
                ? new string[] { "Build the maps of MyMaps?", "The build tools cook every map of this project and install each one with its own card. This can take a long time; keep Bodycam and the Unreal editor closed.", "Build and install", "Plan only", "Cancel" }
                : new string[] { "Remove Panopticon Prison?", "Its files move to the backup folder. Nothing is deleted.", "Remove", "Cancel" };
            string[] buttons = new string[t.Length - 2];
            Array.Copy(t, 2, buttons, 0, buttons.Length);
            using (DarkDialog d = which == "dialog-build" ? new DarkDialog(t[0], t[1], buttons) : new DarkDialog(t[0], t[1], buttons, 0, 1, 0))
            {
                ShotRenderer.CreateHidden(d);
                return ShotRenderer.Save(d, png);
            }
        }

        public static int Show(IWin32Window owner, string title, string body, params string[] buttons)
        {
            if (buttons == null || buttons.Length == 0) buttons = new string[] { "OK" };
            return ShowEx(owner, title, body, 0, buttons.Length - 1, -1, buttons);
        }

        public static int ShowEx(IWin32Window owner, string title, string body, int defaultIndex, int cancelIndex, int dangerIndex, params string[] buttons)
        {
            if (buttons == null || buttons.Length == 0) buttons = new string[] { "OK" };
            using (DarkDialog d = new DarkDialog(title, body, buttons, defaultIndex, cancelIndex, dangerIndex))
            {
                if (owner != null) d.ShowDialog(owner); else { d.StartPosition = FormStartPosition.CenterScreen; d.ShowDialog(); }
                return d.answer;
            }
        }

        public static string Probe(string title, string body, int defaultIndex, int cancelIndex, int dangerIndex, string[] buttons, out int escape, out int enter, out int closeBox)
        {
            using (DarkDialog d = new DarkDialog(title, body, buttons, defaultIndex, cancelIndex, dangerIndex))
            {
                ShotRenderer.CreateHidden(d);
                closeBox = d.answer;
                enter = d.made.IndexOf(d.AcceptButton as FlatButton);
                d.OnKeyDown(new KeyEventArgs(Keys.Escape));
                escape = d.answer;
                string styles = "";
                for (int i = 0; i < d.made.Count; i++) styles += (i > 0 ? ", " : "") + d.made[i].Text + (d.made[i].Primary ? " (default, green)" : "") + (d.made[i].Danger ? " (red text)" : "");
                return styles;
            }
        }
    }
}
