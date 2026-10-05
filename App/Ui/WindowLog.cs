// Log sink for the window: collects lines from worker threads and writes installer.log.
using System;
using System.Collections.Generic;
using BodycamMapInstaller.Core;

namespace BodycamMapInstaller.Ui
{
    enum LogLevel { Info, Good, Warn, Block, Dim, Output, Detail }

    sealed class WindowLog : ILog
    {
        public sealed class Entry
        {
            public LogLevel Level;
            public string Text;
            public DateTime When;
        }

        readonly object gate = new object();
        readonly Queue<Entry> queue = new Queue<Entry>();
        readonly FileLog file;
        int progressStep, progressTotal;
        string progressWhat;
        bool progressChanged;
        string firstBlock;

        public WindowLog(FileLog file) { this.file = file; }

        public void Add(LogLevel level, string text)
        {
            Entry e = new Entry();
            e.Level = level;
            e.Text = text ?? "";
            e.When = DateTime.Now;
            lock (gate)
            {
                queue.Enqueue(e);
                if (level == LogLevel.Block && firstBlock == null) firstBlock = e.Text;
            }
            if (file != null) file.Write(level.ToString().ToUpperInvariant(), e.Text);
        }

        public void Info(string line) { Add(LogLevel.Info, line); }
        public void Good(string line) { Add(LogLevel.Good, line); }
        public void Warn(string line) { Add(LogLevel.Warn, line); }
        public void Block(string line) { Add(LogLevel.Block, line); }
        public void Dim(string line) { Add(LogLevel.Dim, line); }
        public void Detail(string line) { Add(LogLevel.Detail, line); }
        public void Output(string line) { Add(LogLevel.Output, line); }

        public void Progress(int step, int total, string what)
        {
            lock (gate)
            {
                progressStep = step;
                progressTotal = total;
                progressWhat = what;
                progressChanged = true;
            }
        }

        public List<Entry> Drain()
        {
            List<Entry> list = new List<Entry>();
            lock (gate) while (queue.Count > 0) list.Add(queue.Dequeue());
            return list;
        }

        public string TakeProgress()
        {
            lock (gate)
            {
                if (!progressChanged) return null;
                progressChanged = false;
                if (string.IsNullOrEmpty(progressWhat)) return "";
                return progressTotal > 1 ? progressWhat + "   step " + Math.Min(progressStep, progressTotal) + " of " + progressTotal : progressWhat;
            }
        }

        public float ProgressFraction
        {
            get
            {
                lock (gate)
                {
                    if (progressTotal <= 1 || string.IsNullOrEmpty(progressWhat)) return -1f;
                    return Math.Max(0f, Math.Min(1f, (float)progressStep / progressTotal));
                }
            }
        }

        public void ResetProgress()
        {
            lock (gate) { progressStep = 0; progressTotal = 0; progressWhat = null; progressChanged = false; }
        }

        public void BeginCapture() { lock (gate) firstBlock = null; }
        public string FirstBlock { get { lock (gate) return firstBlock; } }
    }
}
