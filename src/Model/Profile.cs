using System.Collections.Generic;

namespace MonitorConfig
{
    // A named set of values applied to one monitor at once.
    class Profile
    {
        public string Name = "";
        public string Monitor = "";   // DdcMonitor.Key of the monitor it belongs to
        public readonly SortedDictionary<byte, uint> Values = new SortedDictionary<byte, uint>();

        public Profile Clone()
        {
            var p = new Profile { Name = Name, Monitor = Monitor };
            foreach (var kv in Values) p.Values[kv.Key] = kv.Value;
            return p;
        }

        public string Summary()
        {
            var parts = new List<string>();
            foreach (var code in Vcp.ProfileCodes)
            {
                uint v;
                if (!Values.TryGetValue(code, out v)) continue;
                if (Vcp.IsContinuous(code)) parts.Add(Vcp.Name(code) + " " + v);
                else parts.Add(Vcp.ValueName(code, v));
            }
            return string.Join(", ", parts.ToArray());
        }
    }
}
