using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace MonitorConfig
{
    // One physical monitor through Windows' Monitor Configuration API (dxva2.dll). Each call retries once:
    // DDC/CI over the cable occasionally drops a reply.
    class Dxva2Port : IMonitorPort
    {
        readonly IntPtr handle;
        bool closed;
        public static int Transactions;   // requests sent, for the developer benchmark

        public Dxva2Port(IntPtr handle) { this.handle = handle; }

        public bool Closed { get { return closed || handle == IntPtr.Zero; } }

        public bool TryGet(byte code, out VcpValue value)
        {
            value = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (Closed) return false;
                uint type, cur, max;
                Interlocked.Increment(ref Transactions);
                if (Native.GetVCPFeatureAndVCPFeatureReply(handle, code, out type, out cur, out max))
                {
                    value = new VcpValue { Current = cur, Max = max, Type = type };
                    return true;
                }
                Thread.Sleep(40);
            }
            return false;
        }

        public bool TrySet(byte code, uint value)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (Closed) return false;
                Interlocked.Increment(ref Transactions);
                if (Native.SetVCPFeature(handle, code, value)) return true;
                Thread.Sleep(60);
            }
            return false;
        }

        public bool TrySave()
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (Closed) return false;
                Interlocked.Increment(ref Transactions);
                if (Native.SaveCurrentMonitorSettings(handle)) return true;
                Thread.Sleep(100);
            }
            return false;
        }

        public string ReadCapabilities()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (Closed) return null;
                uint len;
                Interlocked.Increment(ref Transactions);
                if (Native.GetCapabilitiesStringLength(handle, out len) && len > 0)
                {
                    var buf = new byte[len];
                    if (Native.CapabilitiesRequestAndCapabilitiesReply(handle, buf, len))
                    {
                        string s = Encoding.ASCII.GetString(buf).TrimEnd('\0');
                        if (s.IndexOf("vcp", StringComparison.OrdinalIgnoreCase) >= 0) return s;
                    }
                }
                Thread.Sleep(100);
            }
            return null;
        }

        public void Close()
        {
            if (!Closed) Native.DestroyPhysicalMonitor(handle);
            closed = true;
        }
    }

    // Windows display info + EDID from the registry + connection type, one Dxva2Port per physical monitor.
    class Dxva2Bus : IMonitorBus
    {
        public List<DdcMonitor> Enumerate()
        {
            var hmons = new List<IntPtr>();
            Native.MonitorEnumProc proc = delegate(IntPtr h, IntPtr dc, ref Native.RECT r, IntPtr d) { hmons.Add(h); return true; };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, proc, IntPtr.Zero);
            GC.KeepAlive(proc);

            var connections = ConnectionTypes();
            var list = new List<DdcMonitor>();
            foreach (var hm in hmons)
            {
                var mi = new Native.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (!Native.GetMonitorInfo(hm, ref mi)) continue;

                var devices = new List<Native.DISPLAY_DEVICE>();
                var all = new List<Native.DISPLAY_DEVICE>();
                for (uint i = 0; i < 16; i++)
                {
                    var dd = new Native.DISPLAY_DEVICE();
                    dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
                    if (!Native.EnumDisplayDevices(mi.szDevice, i, ref dd, 0)) break;
                    all.Add(dd);
                    if ((dd.StateFlags & Native.DISPLAY_DEVICE_ACTIVE) != 0) devices.Add(dd);
                }
                if (devices.Count == 0) devices = all;

                var mode = new Native.DEVMODE();
                mode.dmSize = (short)Marshal.SizeOf(typeof(Native.DEVMODE));
                bool haveMode = Native.EnumDisplaySettings(mi.szDevice, Native.ENUM_CURRENT_SETTINGS, ref mode);

                uint n;
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hm, out n) || n == 0) continue;
                var arr = new Native.PHYSICAL_MONITOR[n];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(hm, n, arr)) continue;
                for (int k = 0; k < n; k++)
                {
                    var m = new DdcMonitor { Port = new Dxva2Port(arr[k].hPhysicalMonitor) };
                    m.DeviceName = mi.szDevice ?? "";
                    m.Primary = (mi.dwFlags & Native.MONITORINFOF_PRIMARY) != 0;
                    m.Left = mi.rcMonitor.Left;
                    m.Top = mi.rcMonitor.Top;
                    long tech;
                    if (connections.TryGetValue(m.DeviceName, out tech)) m.Connection = tech;
                    if (haveMode) { m.Width = mode.dmPelsWidth; m.Height = mode.dmPelsHeight; m.Hz = mode.dmDisplayFrequency; }
                    // Clone mode puts several monitors on one output: devices and physical monitors are paired by order.
                    if (k < devices.Count) m.DeviceId = devices[k].DeviceID ?? "";
                    var parts = m.DeviceId.Split('\\');
                    if (parts.Length >= 2) m.PnpId = parts[1];
                    if (parts.Length >= 4) ReadEdid(m, parts[2] + "\\" + parts[3]);
                    list.Add(m);
                }
            }
            return list;
        }

        // Windows display name (\\.\DISPLAY1) -> output technology of its active path.
        static Dictionary<string, long> ConnectionTypes()
        {
            var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            try
            {
                uint np, nm;
                if (Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out np, out nm) != 0) return result;
                var paths = new Native.DISPLAYCONFIG_PATH_INFO[np];
                var modes = new Native.DISPLAYCONFIG_MODE_INFO[nm];
                if (Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref np, paths, ref nm, modes, IntPtr.Zero) != 0) return result;
                for (int i = 0; i < np; i++)
                {
                    var name = new Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME
                    {
                        type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                        adapterId = paths[i].sourceInfo.adapterId,
                        id = paths[i].sourceInfo.id,
                    };
                    name.size = (uint)Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_SOURCE_DEVICE_NAME));
                    if (Native.DisplayConfigGetDeviceInfo(ref name) == 0 && !string.IsNullOrEmpty(name.viewGdiDeviceName))
                        result[name.viewGdiDeviceName] = paths[i].targetInfo.outputTechnology;
                }
            }
            catch (Exception ex) { Crash.Log(ex); }   // optional data: «Este PC» is just not shown
            return result;
        }

        // The monitor's EDID in the registry (the instance whose "Driver" matches) gives its real name and serial.
        static void ReadEdid(DdcMonitor m, string driver)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + m.PnpId))
                {
                    if (key == null) return;
                    foreach (var name in key.GetSubKeyNames())
                    {
                        using (var inst = key.OpenSubKey(name))
                        {
                            if (inst == null) continue;
                            var drv = inst.GetValue("Driver") as string;
                            if (drv == null || !drv.Equals(driver, StringComparison.OrdinalIgnoreCase)) continue;
                            m.InstanceId = name;
                            using (var dp = inst.OpenSubKey("Device Parameters"))
                                if (dp != null) m.ApplyEdid(dp.GetValue("EDID") as byte[]);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex) { Crash.Log(ex); }   // optional data: the model code is used as the name
        }
    }
}
