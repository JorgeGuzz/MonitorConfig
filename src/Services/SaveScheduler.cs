using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Changes made over DDC/CI live in the monitor's RAM until it is told to store them, and the XG2705 reloads its
    // stored settings when a new signal arrives. A moment after the last change the monitor is asked to save
    // (not on every slider step: that would wear its memory).
    class SaveScheduler : IDisposable
    {
        public const int DelayMs = 2000;
        readonly MonitorService service;
        readonly Func<DateTime> clock;
        readonly Dictionary<DdcMonitor, DateTime> due = new Dictionary<DdcMonitor, DateTime>();
        readonly Timer timer = new Timer { Interval = 500 };

        public SaveScheduler(MonitorService service, Func<DateTime> clock)
        {
            this.service = service;
            this.clock = clock;
            service.Written += OnWritten;
            timer.Tick += delegate { Tick(clock()); };
        }

        // Inputs, power and restores are not settings to keep.
        static bool Persists(byte code)
        {
            return !Vcp.IsAction(code) && code != Vcp.Input && code != Vcp.Power && code != Vcp.NewControlValue;
        }

        void OnWritten(DdcMonitor m, byte code)
        {
            if (!m.CanSave || !Persists(code)) return;
            due[m] = clock().AddMilliseconds(DelayMs);
            timer.Start();
        }

        public bool IsPending(DdcMonitor m) { return due.ContainsKey(m); }

        public void Tick(DateTime now)
        {
            foreach (var kv in due.Where(x => x.Value <= now).ToList()) Flush(kv.Key);
            if (due.Count == 0) timer.Stop();
        }

        // Saves now if something is waiting (before an input change, which makes the monitor reload what it stored).
        public void Flush(DdcMonitor m)
        {
            if (due.Remove(m)) service.StoreSettings(m);
        }

        public void Dispose()
        {
            foreach (var m in due.Keys.ToList()) Flush(m);
            service.Written -= OnWritten;
            timer.Dispose();
        }
    }
}
