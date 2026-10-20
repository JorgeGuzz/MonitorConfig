using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace MonitorConfig
{
    // The connected monitors and everything read from or written to them. DDC/CI work runs on the queue;
    // results come back through the UI dispatcher, so state and events always live on the UI thread.
    class MonitorService : IDisposable
    {
        public readonly Settings Settings;
        readonly IMonitorBus bus;
        readonly DdcQueue queue;
        readonly Action<Action> ui;
        readonly Func<DateTime> clock;
        readonly Dictionary<string, string> sessionCaps = new Dictionary<string, string>();   // queue thread only
        readonly HashSet<string> pendingReads = new HashSet<string>();                         // UI thread
        bool detectAgain;

        // Monitors need a moment after a write before the side effects can be read back (tests use 0).
        public int FollowUpDelayMs = 400, ResetDelayMs = 1500;

        public List<DdcMonitor> All { get; private set; }
        public DdcMonitor Active { get; private set; }
        public bool Detecting { get; private set; }
        public bool DdcIdle { get { return queue.Pending == 0; } }

        public event Action MonitorsChanged;
        public event Action<DdcMonitor, byte> ValueChanged;
        public event Action<string, bool> StatusChanged;
        public event Action<DdcMonitor, byte> Written;   // a value was sent (the save scheduler listens)

        // Everything the main window shows.
        public static readonly byte[] MainCodes =
        {
            Vcp.Brightness, Vcp.Contrast, Vcp.Sharpness, Vcp.ColorPreset, Vcp.RedGain, Vcp.GreenGain, Vcp.BlueGain,
            Vcp.Input, Vcp.Volume, Vcp.Mute, Vcp.OsdLanguage, Vcp.Osd, Vcp.Power,
            Vcp.ColorTempIncrement, Vcp.ColorTemp, Vcp.VFreq, Vcp.HFreq, Vcp.Firmware, Vcp.Controller, Vcp.Version, Vcp.DisplayTech
        };

        // What the tray panel and menu show for every monitor.
        public static readonly byte[] QuickCodes = { Vcp.Brightness, Vcp.Volume, Vcp.Mute, Vcp.Input };

        public MonitorService(Settings settings, IMonitorBus bus, DdcQueue queue, Action<Action> ui, Func<DateTime> clock)
        {
            Settings = settings;
            this.bus = bus;
            this.queue = queue;
            this.ui = ui;
            this.clock = clock;
            All = new List<DdcMonitor>();
            queue.Error += ex => { Crash.Log(ex); Ui(() => Status(Strings.DdcError(ex.Message), true)); };
        }

        public void Ui(Action action) { ui(action); }

        public void Status(string text, bool error = false)
        {
            var h = StatusChanged;
            if (h != null) h(text, error);
        }

        void RaiseValue(DdcMonitor m, byte code)
        {
            var h = ValueChanged;
            if (h != null) h(m, code);
        }

        void RaiseMonitors()
        {
            var h = MonitorsChanged;
            if (h != null) h();
        }

        // ---------------------------------------------------------------- detection

        // Re-enumerates the monitors. Capabilities come from the copy in the settings unless a fresh read is asked for.
        public void Detect(bool rereadCaps)
        {
            if (Detecting) { detectAgain = true; return; }
            Detecting = true;
            Status(Strings.Searching);
            var old = All;
            var fileCaps = new Dictionary<string, string>(Settings.CapsCache);
            queue.Post(delegate
            {
                foreach (var m in old) m.Close();
                var found = bus.Enumerate();
                foreach (var m in found)
                {
                    // Capabilities are fixed per monitor and asking takes 1-2 s each.
                    string raw = null;
                    if (!rereadCaps && !sessionCaps.TryGetValue(m.CacheKey, out raw) && TryFileCaps(fileCaps, m, out raw)) m.CapsFromCache = true;
                    if (raw == null)
                    {
                        raw = m.ReadCapabilities();
                        if (raw != null) sessionCaps[m.CacheKey] = raw;
                    }
                    if (raw == null && TryFileCaps(fileCaps, m, out raw)) m.CapsFromCache = true;
                    m.Caps = raw != null ? Capabilities.Parse(raw) : null;
                    if (m.Caps == null)
                    {
                        VcpValue v;   // no capabilities: does it answer at all?
                        if (m.TryGet(Vcp.Brightness, out v)) Ui(() => m.Values[Vcp.Brightness] = v);
                    }
                }
                Ui(() => OnDetected(found));
            });
        }

        static bool TryFileCaps(Dictionary<string, string> fileCaps, DdcMonitor m, out string raw)
        {
            return fileCaps.TryGetValue(m.CacheKey, out raw) || fileCaps.TryGetValue(m.LegacyKey, out raw);
        }

        void OnDetected(List<DdcMonitor> found)
        {
            Detecting = false;
            // Left to right as they sit on the desk (Windows' DISPLAY numbers change between boots).
            found = found.OrderBy(m => m.Left).ThenBy(m => m.Top).ToList();
            if (found.Count == 2 && found[0].Left != found[1].Left) { found[0].Side = 1; found[1].Side = 2; }
            All = found;

            bool changed = false;
            // Settings saved under the old per-model key move to the individual key (only if that model is connected once).
            foreach (var m in found)
                if (found.Count(x => x.LegacyKey == m.LegacyKey) == 1 && Settings.MigrateMonitorKey(m.LegacyKey, m.Key)) changed = true;
            foreach (var m in found)
            {
                string cached;
                if (m.Caps != null && !m.CapsFromCache && (!Settings.CapsCache.TryGetValue(m.CacheKey, out cached) || cached != m.Caps.Raw))
                {
                    Settings.CapsCache[m.CacheKey] = m.Caps.Raw;
                    changed = true;
                }
            }
            if (changed) Settings.Save();

            Active = found.FirstOrDefault(m => m.Key == Settings.ActiveMonitor && m.Responsive)
                ?? found.FirstOrDefault(m => m.Primary && m.Responsive)
                ?? found.FirstOrDefault(m => m.Responsive)
                ?? found.FirstOrDefault();

            RaiseMonitors();
            if (found.Count == 0) Status(Strings.NoMonitors, true);
            else if (Active == null || !Active.Responsive) Status(Strings.NoDdc, true);
            else Status(Strings.Ready);
            // Only what the tray shows; the main window reads the rest while it is open.
            foreach (var m in found) RefreshQuick(m);

            if (detectAgain) { detectAgain = false; Detect(false); }
        }

        public void SetActive(DdcMonitor m)
        {
            if (m == null || m == Active) return;
            Active = m;
            Settings.ActiveMonitor = m.Key;
            Settings.Save();
            RaiseMonitors();
            if (m.Responsive) ReadMain(m);
        }

        // ---------------------------------------------------------------- reads

        // Brightness, volume and input for the tray panel and menu, at most every few seconds.
        public void RefreshQuick(DdcMonitor m)
        {
            if (!m.Responsive || (clock() - m.QuickReadAt).TotalSeconds < 5) return;
            m.QuickReadAt = clock();
            Read(m, QuickCodes, null);
        }

        public void ReadMain(DdcMonitor m)
        {
            m.LastRead = m.QuickReadAt = clock();
            Read(m, MainCodes, null);
        }

        public void ReadAll(DdcMonitor m, Action done)
        {
            var codes = m.Caps != null ? m.Caps.Codes.ToArray() : MainCodes;
            Read(m, codes.Where(c => !Vcp.IsAction(c)), done);
        }

        // skipQueued: a code already waiting in the queue is not asked twice (opening the tray menu repeatedly used to
        // pile up reads). Reads that must happen after some writes (profiles) pass false.
        public void Read(DdcMonitor m, IEnumerable<byte> codes, Action done, bool onlySupported = true, bool skipQueued = true)
        {
            var list = codes.Where(c => (!onlySupported || m.Supports(c)) && (pendingReads.Add(m.Id + "/" + c) || !skipQueued)).ToList();
            queue.Post(delegate
            {
                try
                {
                    foreach (var code in list)
                    {
                        if (m.Closed) return;
                        VcpValue v;
                        if (m.TryGet(code, out v)) Ui(() => { m.Values[code] = v; m.Failed.Remove(code); RaiseValue(m, code); });
                        else Ui(() => { m.Failed.Add(code); RaiseValue(m, code); });
                    }
                    if (done != null) Ui(done);
                }
                finally { Ui(() => { foreach (var code in list) pendingReads.Remove(m.Id + "/" + code); }); }
            });
        }

        // ---------------------------------------------------------------- writes

        // Controls whose value can change as a side effect of another write.
        static byte[] FollowUps(byte code)
        {
            switch (code)
            {
                case Vcp.ColorPreset: return new[] { Vcp.RedGain, Vcp.GreenGain, Vcp.BlueGain, Vcp.Brightness, Vcp.Contrast, Vcp.ColorTemp };
                case Vcp.ResetLuminance: return new[] { Vcp.Brightness, Vcp.Contrast };
                case Vcp.ResetColor: return new[] { Vcp.ColorPreset, Vcp.RedGain, Vcp.GreenGain, Vcp.BlueGain, Vcp.ColorTemp };
                case Vcp.FactoryReset: return MainCodes;
                default: return null;
            }
        }

        public void Set(DdcMonitor m, byte code, uint value)
        {
            if (m == null) return;
            if (!Vcp.IsAction(code))
            {
                VcpValue old;
                m.Values.TryGetValue(code, out old);
                m.Values[code] = new VcpValue { Current = value, Max = old != null ? old.Max : 0, Type = old != null ? old.Type : 0 };
                RaiseValue(m, code);
            }
            var follow = FollowUps(code);
            int delay = code == Vcp.FactoryReset ? ResetDelayMs : FollowUpDelayMs;
            // Keyed by the monitor object: a write made right after a re-detection must not merge with one queued
            // for the old (closed) object, which would drop it.
            queue.PostCoalesced(m.Id + "/" + code.ToString("X2"), value, delegate(uint v)
            {
                if (m.Closed) return;
                if (!m.TrySet(code, v))
                {
                    Ui(() => Status(Strings.WriteRejected(Vcp.Name(code)), true));
                    follow = new[] { code };
                }
                if (follow == null) return;
                if (delay > 0) Thread.Sleep(delay);
                foreach (var c in follow)
                {
                    if (m.Closed || !m.Supports(c)) continue;
                    VcpValue r;
                    byte fc = c;
                    if (m.TryGet(fc, out r)) Ui(() => { m.Values[fc] = r; m.Failed.Remove(fc); RaiseValue(m, fc); });
                }
            });
            var h = Written;
            if (h != null) h(m, code);
        }

        // Asks the monitor to store its current settings (queued after the pending writes).
        public void StoreSettings(DdcMonitor m)
        {
            queue.Post(delegate
            {
                if (m.Closed) return;
                if (!m.TrySave()) Ui(() => Status(Strings.SaveRejected(m.Model), true));
            });
        }

        // Raw value for a percentage of the control's range.
        public uint ValueForPercent(DdcMonitor m, byte code, int percent)
        {
            VcpValue v;
            uint max = m.Values.TryGetValue(code, out v) && v.Max > 0 ? v.Max : 100;
            return (uint)Math.Round(max * Math.Max(0, Math.Min(100, percent)) / 100.0);
        }

        // The save scheduler must be disposed first, so pending saves are queued before the monitors close.
        public void Dispose()
        {
            var list = All;
            queue.Post(delegate { foreach (var m in list) m.Close(); });
            queue.Dispose();
        }
    }
}
