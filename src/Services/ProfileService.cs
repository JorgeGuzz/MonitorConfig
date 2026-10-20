using System;
using System.Linq;

namespace MonitorConfig
{
    class ProfileService
    {
        readonly MonitorService service;
        public event Action<DdcMonitor, string, int> Indicator;   // on-screen indicator (percent -1 = no bar)

        public ProfileService(MonitorService service) { this.service = service; }

        // The monitor a profile belongs to. Profiles without one (very old settings) go to the only monitor connected;
        // a profile is never applied to a different monitor just because its own is disconnected.
        public DdcMonitor MonitorFor(Profile p)
        {
            var all = service.All;
            if (p.Monitor.Length > 0) return all.FirstOrDefault(m => m.Key == p.Monitor);
            return all.Count == 1 ? all[0] : null;
        }

        public bool Apply(Profile p)
        {
            var m = MonitorFor(p);
            if (m == null || !m.Responsive)
            {
                service.Status(Strings.ProfileMonitorMissing(p.Name), true);
                return false;
            }
            // Preset first: it can change the RGB gains and the brightness that come after it.
            foreach (var code in Vcp.ProfileCodes.Concat(p.Values.Keys.Where(c => Array.IndexOf(Vcp.ProfileCodes, c) < 0)))
            {
                uint v;
                if (p.Values.TryGetValue(code, out v) && m.Supports(code)) service.Set(m, code, v);
            }
            // Queued after the writes: the window shows what the monitor ended up with.
            service.Read(m, p.Values.Keys.ToList(), null, skipQueued: false);
            service.Status(Strings.ProfileApplied(p.Name, m.Model));
            var h = Indicator;
            if (h != null) h(m, Strings.ProfileIndicator(p.Name), -1);
            return true;
        }
    }
}
