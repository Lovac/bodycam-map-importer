// Developer build only: the "what does my PC need" window behind the REQUIREMENTS button (22 Sep 2026).
// One row per requirement: OK or MISSING, what was found, what to do, and a link to get it. It opens by itself
// when EDIT or COOK is pressed and something is missing, so nobody has to know it exists to be helped by it.
#if BCMI_DEV
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using BodycamMapInstaller.Core;

namespace BodycamMapInstaller.Ui
{
    class RequirementsForm : Form
    {
        readonly Panel body = new Panel();
        readonly FlatButton again = new FlatButton("Check again");
        readonly FlatButton readme = new FlatButton("Open the instructions");
        readonly FlatButton close = new FlatButton("Close");
        readonly bool bodycamFound;
        readonly string bodycamPath;

        public RequirementsForm(bool bodycamFound, string bodycamPath, string why)
        {
            this.bodycamFound = bodycamFound;
            this.bodycamPath = bodycamPath;
            Text = "Map maker requirements";
            BackColor = Theme.Window;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(Theme.Px(720), Theme.Px(560));

            Label head = new Label();
            head.Text = why ?? "What this PC needs to EDIT and COOK maps. Everything must say OK.";
            head.Font = Theme.BodySemi;
            head.ForeColor = why == null ? Theme.Text : Theme.Amber;
            head.SetBounds(Theme.Px(20), Theme.Px(16), ClientSize.Width - Theme.Px(40), Theme.Px(44));
            Controls.Add(head);

            body.SetBounds(Theme.Px(20), Theme.Px(64), ClientSize.Width - Theme.Px(40), ClientSize.Height - Theme.Px(130));
            body.AutoScroll = true;
            body.BackColor = Theme.Window;
            Controls.Add(body);

            int by = ClientSize.Height - Theme.Px(52);
            int bh = Theme.Px(34);
            again.SetBounds(Theme.Px(20), by, Theme.Px(140), bh);
            readme.SetBounds(again.Right + Theme.Px(10), by, Theme.Px(220), bh);
            close.SetBounds(ClientSize.Width - Theme.Px(20) - Theme.Px(120), by, Theme.Px(120), bh);
            Controls.Add(again);
            Controls.Add(readme);
            Controls.Add(close);
            again.Click += delegate { Fill(); };
            readme.Click += delegate { OpenReadme(); };
            close.Click += delegate { Close(); };
            CancelButton = close;

            Fill();
        }

        void Fill()
        {
            Cursor = Cursors.WaitCursor;
            body.SuspendLayout();
            body.Controls.Clear();
            List<Requirement> list = MakerChecks.CheckAll(bodycamFound, bodycamPath);
            int y = 0;
            int w = body.Width - Theme.Px(24);
            foreach (Requirement r in list)
            {
                Label mark = new Label();
                mark.Text = r.Ok ? "OK" : "MISSING";
                mark.Font = Theme.BodySemi;
                mark.ForeColor = r.Ok ? Theme.Green : Theme.RedText;
                mark.SetBounds(0, y, Theme.Px(84), Theme.Px(24));
                body.Controls.Add(mark);

                Label name = new Label();
                name.Text = r.Name;
                name.Font = Theme.BodySemi;
                name.ForeColor = Theme.Text;
                name.SetBounds(Theme.Px(88), y, w - Theme.Px(88), Theme.Px(24));
                body.Controls.Add(name);
                y += Theme.Px(24);

                Label found = new Label();
                found.Text = r.Found ?? "";
                found.Font = Theme.Small;
                found.ForeColor = Theme.TextDim;
                found.SetBounds(Theme.Px(88), y, w - Theme.Px(88), Theme.Px(20));
                found.AutoEllipsis = true;
                body.Controls.Add(found);
                y += Theme.Px(22);

                if (!r.Ok)
                {
                    Label fix = new Label();
                    fix.Text = r.Fix;
                    fix.Font = Theme.Small;
                    fix.ForeColor = Theme.Text;
                    fix.SetBounds(Theme.Px(88), y, w - Theme.Px(88), Theme.Px(38));
                    body.Controls.Add(fix);
                    y += Theme.Px(40);
                    if (r.Link != null)
                    {
                        LinkLabel link = new LinkLabel();
                        link.Text = "Get it: " + r.Link;
                        link.Font = Theme.Small;
                        link.LinkColor = Theme.Blue;
                        link.ActiveLinkColor = Theme.Text;
                        link.VisitedLinkColor = Theme.Blue;
                        link.SetBounds(Theme.Px(88), y, w - Theme.Px(88), Theme.Px(20));
                        string url = r.Link;
                        link.LinkClicked += delegate { OpenUrl(url); };
                        body.Controls.Add(link);
                        y += Theme.Px(22);
                    }
                }
                y += Theme.Px(14);
            }
            Label tail = new Label();
            tail.Text = MakerChecks.AllOk(list)
                ? "Everything is ready. Close this window, pick a map in the list and press EDIT or COOK."
                : "Install what says MISSING, then press Check again. Restart this installer after installing Python or Git.";
            tail.Font = Theme.Small;
            tail.ForeColor = MakerChecks.AllOk(list) ? Theme.Green : Theme.Amber;
            tail.SetBounds(0, y, w, Theme.Px(40));
            body.Controls.Add(tail);
            body.ResumeLayout();
            Cursor = Cursors.Default;
        }

        static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception) { MessageBox.Show("Open this address in your web browser:\r\n\r\n" + url, "Link"); }
        }

        void OpenReadme()
        {
            string uproject;
            string sandbox = MakerChecks.FindSandbox(out uproject);
            string path = sandbox == null ? null : Path.Combine(sandbox, MakerChecks.ReadmeRel);
            if (path == null || !File.Exists(path))
            {
                MessageBox.Show(this, "The instructions file was not found. It lives in the map project at\r\n" + MakerChecks.ReadmeRel, "Instructions");
                return;
            }
            try { Process.Start(new ProcessStartInfo("notepad.exe", "\"" + path + "\"") { UseShellExecute = true }); }
            catch (Exception) { MessageBox.Show(this, "Open this file: " + path, "Instructions"); }
        }
    }
}
#endif
