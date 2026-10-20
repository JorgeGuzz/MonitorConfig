using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace MonitorConfig
{
    // One physical monitor: identity (from Windows and its EDID), capabilities and last known values.
    // The port is only used from the DDC queue's thread; Values/Failed belong to the UI thread.
    class DdcMonitor
    {
        static int nextId;
        public readonly int Id = Interlocked.Increment(ref nextId);   // this object (a re-detection creates new ones)
        public IMonitorPort Port;

        public string DeviceName = "";   // \\.\DISPLAY1
        public string DeviceId = "";     // MONITOR\VSC0E39\{4d36e96e-...}\0001
        public string PnpId = "";        // VSC0E39
        public string EdidName = "";     // XG2705
        public string EdidSerial = "";
        public uint EdidNumSerial;       // EDID header serial (bytes 12-15), 0 when the maker leaves it empty
        public string InstanceId = "";   // Windows device instance for this connection, e.g. 5&1ce46661&0&UID4354
        public int Left, Top;            // desktop position (monitors are listed left to right)
        public int Side;                 // 1 = left, 2 = right (two monitors side by side), 0 = not said
        public string Position { get { return Side == 1 ? Strings.LeftScreen : Side == 2 ? Strings.RightScreen : ""; } }
        public bool Primary;
        public int Width, Height, Hz;
        public long Connection = -1;     // DISPLAYCONFIG output technology of this PC's cable (10 = DisplayPort, 5 = HDMI...)
        public Capabilities Caps;
        public bool CapsFromCache;

        public readonly Dictionary<byte, VcpValue> Values = new Dictionary<byte, VcpValue>();
        public readonly HashSet<byte> Failed = new HashSet<byte>();
        public DateTime LastRead = DateTime.MinValue, QuickReadAt = DateTime.MinValue;

        // Codes tried when the monitor does not report its capabilities.
        static readonly byte[] FallbackCodes = { 0x10, 0x12, 0x14, 0x16, 0x18, 0x1A, 0x60, 0x62, 0x87, 0x8D, 0xD6 };

        // Inputs offered when the monitor does not list its own: DisplayPort, HDMI 1, HDMI 2.
        public static readonly byte[] DefaultInputs = { 0x0F, 0x11, 0x12 };

        // ---------------------------------------------------------------- identity

        // Individual identity, independent of the order Windows numbers the displays in at each boot:
        // model code + serial number from the EDID; monitors without a serial use Windows' id for the
        // connection they are plugged into (stable unless the cable moves to another port).
        public static string MakeKey(string pnpId, string serial, uint numSerial, string instanceId, string deviceName)
        {
            if (pnpId.Length == 0) return deviceName;
            if (serial.Length > 0) return pnpId + "-" + serial;
            if (numSerial > 1 && numSerial != 0x01010101 && numSerial != 0xFFFFFFFF) return pnpId + "-" + numSerial.ToString("X8");   // 0/1/0101... = filler (the MD27PRO says 1)
            if (instanceId.Length > 0) return pnpId + "-" + instanceId;
            return pnpId;
        }

        public string Key { get { return MakeKey(PnpId, EdidSerial, EdidNumSerial, InstanceId, DeviceName); } }
        public string LegacyKey { get { return PnpId.Length > 0 ? PnpId : DeviceName; } }   // key used up to 2026-10-20
        public string CacheKey { get { return Key; } }
        public string Vendor { get { return VendorName(PnpId.Length >= 3 ? PnpId.Substring(0, 3) : ""); } }
        public bool Responsive { get { return Caps != null || Values.Count > 0; } }

        public string Model
        {
            get
            {
                if (EdidName.Length > 0) return EdidName;
                if (Caps != null && !string.IsNullOrEmpty(Caps.Model)) return Caps.Model;
                return PnpId.Length > 0 ? PnpId : Strings.GenericMonitor;
            }
        }

        public string DisplayName
        {
            get
            {
                string s = Model;
                if (Vendor.Length > 0) s += " (" + Vendor + ")";
                if (Width > 0) s += " · " + Width + "×" + Height;
                if (Position.Length > 0) s += " · " + Position;
                if (!Responsive) s += " · " + Strings.NoDdcSuffix;
                return s;
            }
        }

        // ---------------------------------------------------------------- capabilities

        public bool Supports(byte code)
        {
            if (Caps != null) return Caps.Codes.Contains(code);
            return Array.IndexOf(FallbackCodes, code) >= 0;
        }

        public List<byte> OptionsOf(byte code)
        {
            return Caps == null ? null : Caps.ValuesOf(code);
        }

        public IEnumerable<byte> InputOptions
        {
            get
            {
                var list = OptionsOf(Vcp.Input);
                return list != null && list.Count > 0 ? (IEnumerable<byte>)list : DefaultInputs;
            }
        }

        // Changes made over DDC/CI only live in the monitor's RAM until it is told to store them (MCCS command 0x0C).
        public bool CanSave { get { return Caps != null && Caps.Commands.Contains(0x0C); } }

        // sRGB presets usually fix the luminance and ignore the brightness setting, which still reads back its old value
        // (the XG2705 says 100 while much dimmer; writing 99 over DDC/CI then overrides the calibration).
        public bool InSrgb
        {
            get
            {
                VcpValue p;
                return Values.TryGetValue(Vcp.ColorPreset, out p) && p.Current == Vcp.PresetSrgb;
            }
        }

        // ---------------------------------------------------------------- inputs

        // Monitor input codes that match a cable type. DDC/CI cannot tell what is plugged into each input,
        // but Windows knows the type of its own cable: if only one input of that type exists, that is this PC.
        static byte[] InputsForConnection(long tech)
        {
            switch (tech)
            {
                case 0: return new byte[] { 0x01, 0x02 };              // VGA
                case 4: return new byte[] { 0x03, 0x04 };              // DVI
                case 5: return new byte[] { 0x11, 0x12 };              // HDMI
                case 10: case 11: case 18: return new byte[] { 0x0F, 0x10, 0x1B };   // DisplayPort (also USB-C)
                default: return null;
            }
        }

        // Input being shown, or null if unknown or not one of the monitor's inputs
        // (the MD27PRO answers 07 = S-Video, which it does not have).
        public byte? CurrentInput()
        {
            VcpValue v;
            if (!Values.TryGetValue(Vcp.Input, out v) || v.Current > 0xFF) return null;
            var options = OptionsOf(Vcp.Input);
            if (options != null && !options.Contains((byte)v.Current)) return null;
            return (byte)v.Current;
        }

        public byte? PcInput()
        {
            var codes = InputsForConnection(Connection);
            if (codes == null) return null;
            var options = OptionsOf(Vcp.Input);
            var candidates = options == null ? codes.ToList() : codes.Where(options.Contains).ToList();
            if (candidates.Count == 1) return candidates[0];
            var cur = CurrentInput();   // several inputs of that type: the one being shown, if it is one of them
            return cur.HasValue && candidates.Contains(cur.Value) ? cur : null;
        }

        // ---------------------------------------------------------------- DDC/CI (through the port, queue thread only)

        public bool Closed { get { return Port == null || Port.Closed; } }

        public bool TryGet(byte code, out VcpValue value)
        {
            value = null;
            return Port != null && Port.TryGet(code, out value);
        }

        public bool TrySet(byte code, uint value) { return Port != null && Port.TrySet(code, value); }
        public bool TrySave() { return Port != null && Port.TrySave(); }
        public string ReadCapabilities() { return Port == null ? null : Port.ReadCapabilities(); }
        public void Close() { if (Port != null) Port.Close(); }

        // ---------------------------------------------------------------- EDID

        // Real model name (descriptor FC), serial string (FF) and header serial number.
        public void ApplyEdid(byte[] edid)
        {
            if (edid == null || edid.Length < 128) return;
            EdidNumSerial = BitConverter.ToUInt32(edid, 12);
            for (int off = 54; off <= 108; off += 18)
            {
                if (edid[off] != 0 || edid[off + 1] != 0 || edid[off + 2] != 0) continue;
                byte tag = edid[off + 3];
                if (tag != 0xFC && tag != 0xFF) continue;
                var sb = new StringBuilder();
                for (int i = off + 5; i < off + 18; i++)
                {
                    if (edid[i] == 0x0A || edid[i] == 0) break;
                    sb.Append((char)edid[i]);
                }
                string text = sb.ToString().Trim();
                if (tag == 0xFC) EdidName = text; else EdidSerial = text;
            }
        }

        static string VendorName(string code)
        {
            switch (code)
            {
                case "VSC": return "ViewSonic";
                case "SAM": return "Samsung";
                case "GSM": return "LG";
                case "DEL": return "Dell";
                case "ACR": return "Acer";
                case "AUS": return "ASUS";
                case "BNQ": return "BenQ";
                case "AOC": return "AOC";
                case "PHL": return "Philips";
                case "LEN": return "Lenovo";
                case "HWP": case "HPN": return "HP";
                case "MSI": return "MSI";
                case "GBT": return "Gigabyte";
                default: return code;
            }
        }
    }
}
