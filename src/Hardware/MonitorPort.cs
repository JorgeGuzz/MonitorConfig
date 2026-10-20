using System.Collections.Generic;

namespace MonitorConfig
{
    // DDC/CI channel to one monitor. Windows: Dxva2Port. Tests: an in-memory fake, so the services can be
    // exercised without real monitors.
    interface IMonitorPort
    {
        bool Closed { get; }
        bool TryGet(byte code, out VcpValue value);
        bool TrySet(byte code, uint value);
        bool TrySave();
        string ReadCapabilities();
        void Close();
    }

    // Finds the connected monitors, with their identity filled in and a port attached.
    interface IMonitorBus
    {
        List<DdcMonitor> Enumerate();
    }
}
