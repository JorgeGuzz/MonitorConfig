using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Input changes. Monitors with automatic input detection fall back to an input with picture when the new one
    // has none yet (a console still waking up). For a few seconds after a change the input is read back (reading
    // does not make the monitor switch: tested on the XG2705) and, if it went back on its own, requested again.
    class InputWatcher : IDisposable
    {
        public const int Retries = 2;
        public const int WatchSeconds = 15;

        class Watch { public DdcMonitor M; public byte Target; public DateTime Until; public int Retries; public bool Busy, Confirmed; }

        public enum Step { Wait, Retry, GiveUp }

        readonly MonitorService service;
        readonly SaveScheduler saver;
        readonly InputLabels labels;
        readonly Func<DateTime> clock;
        readonly List<Watch> watches = new List<Watch>();
        readonly Timer timer = new Timer { Interval = 1500 };

        public event Action<string> Notify;   // tray balloon: it happens while you look at another input

        public InputWatcher(MonitorService service, SaveScheduler saver, InputLabels labels, Func<DateTime> clock)
        {
            this.service = service;
            this.saver = saver;
            this.labels = labels;
            this.clock = clock;
            timer.Tick += delegate { Tick(clock()); };
        }

        public static Step Decide(byte? current, byte target, int retries)
        {
            if (!current.HasValue || current.Value == target) return Step.Wait;
            return retries < Retries ? Step.Retry : Step.GiveUp;
        }

        public void Change(DdcMonitor m, byte value)
        {
            saver.Flush(m);   // a new signal makes the monitor reload its stored settings
            service.Set(m, Vcp.Input, value);
            watches.RemoveAll(w => w.M == m);
            watches.Add(new Watch { M = m, Target = value, Until = clock().AddSeconds(WatchSeconds) });
            timer.Start();
        }

        // Settings can change with the input (stored or per-input values): show what the monitor really has.
        void ReadAfterInput(DdcMonitor m)
        {
            if (m.Closed || !m.Responsive) return;
            if (m == service.Active) service.ReadMain(m);
            else service.Read(m, MonitorService.QuickCodes, null);
        }

        public void Tick(DateTime now)
        {
            foreach (var w in watches.Where(x => !x.M.Closed && now > x.Until).ToList()) ReadAfterInput(w.M);
            watches.RemoveAll(w => w.M.Closed || now > w.Until);
            if (watches.Count == 0) { timer.Stop(); return; }
            foreach (var w in watches.ToList())
            {
                if (w.Busy) continue;
                w.Busy = true;
                var watch = w;
                service.Read(w.M, new[] { Vcp.Input }, delegate { watch.Busy = false; Check(watch); });
            }
        }

        void Check(Watch w)
        {
            if (!watches.Contains(w)) return;
            var cur = w.M.CurrentInput();
            if (cur == w.Target && !w.Confirmed)
            {
                w.Confirmed = true;
                ReadAfterInput(w.M);
            }
            var step = Decide(cur, w.Target, w.Retries);
            if (!service.Settings.KeepInput) step = Step.Wait;   // option off: only re-read, never insist
            switch (step)
            {
                case Step.Retry:
                    w.Retries++;
                    w.Until = clock().AddSeconds(WatchSeconds);
                    service.Status(Strings.InputBouncedRetry(w.M.Model, labels.Text(w.M, cur.Value), labels.Text(w.M, w.Target)));
                    service.Set(w.M, Vcp.Input, w.Target);
                    break;
                case Step.GiveUp:
                    watches.Remove(w);
                    ReadAfterInput(w.M);
                    string msg = Strings.InputBouncedGiveUp(w.M.Model, labels.Text(w.M, cur.Value), labels.Text(w.M, w.Target));
                    service.Status(msg, true);
                    var h = Notify;
                    if (h != null) h(msg);
                    break;
            }
        }

        public void Dispose() { timer.Dispose(); }
    }
}
