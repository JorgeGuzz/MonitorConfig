// Simulated monitors for testing the services without hardware.
using System;
using System.Collections.Generic;
using MonitorConfig;

// The monitor itself: its registers survive re-detections (each one creates new ports, like Windows does).
class FakeScreen
{
    public readonly Dictionary<byte, VcpValue> Registers = new Dictionary<byte, VcpValue>();
    public readonly List<string> Log = new List<string>();   // "get 10", "set 10=50", "save", "caps", "close"
    public string Caps;
    public Func<byte, uint, bool> OnSet;   // side effects of a write; false = the monitor rejects it
    public Func<byte, uint?> OnGet;        // overrides an answer (null = the register)

    public FakeScreen Reg(byte code, uint current, uint max = 100)
    {
        Registers[code] = new VcpValue { Current = current, Max = max };
        return this;
    }

    public int Count(string entry) { return Log.FindAll(e => e == entry).Count; }
    public int IndexOf(string entry) { return Log.IndexOf(entry); }
    public int LastIndexOf(string entry) { return Log.LastIndexOf(entry); }
}

class FakePort : IMonitorPort
{
    readonly FakeScreen screen;
    bool closed;

    public FakePort(FakeScreen screen) { this.screen = screen; }

    public bool Closed { get { return closed; } }

    public bool TryGet(byte code, out VcpValue value)
    {
        value = null;
        if (closed) return false;
        screen.Log.Add("get " + code.ToString("X2"));
        VcpValue r;
        if (!screen.Registers.TryGetValue(code, out r)) return false;
        uint current = r.Current;
        if (screen.OnGet != null)
        {
            var over = screen.OnGet(code);
            if (over.HasValue) current = over.Value;
        }
        value = new VcpValue { Current = current, Max = r.Max };
        return true;
    }

    public bool TrySet(byte code, uint value)
    {
        if (closed) return false;
        screen.Log.Add("set " + code.ToString("X2") + "=" + value);
        if (screen.OnSet != null && !screen.OnSet(code, value)) return false;
        VcpValue r;
        if (screen.Registers.TryGetValue(code, out r)) r.Current = value;
        else screen.Registers[code] = new VcpValue { Current = value, Max = 100 };
        return true;
    }

    public bool TrySave()
    {
        if (closed) return false;
        screen.Log.Add("save");
        return true;
    }

    public string ReadCapabilities()
    {
        screen.Log.Add("caps");
        return screen.Caps;
    }

    public void Close()
    {
        if (!closed) screen.Log.Add("close");
        closed = true;
    }
}

class FakeBus : IMonitorBus
{
    public Func<List<DdcMonitor>> Make;   // called on each detection: new objects, like Windows gives
    public List<DdcMonitor> Enumerate() { return Make(); }
}

// Services wired as in TrayApp, but with a manual queue, an immediate UI dispatcher and a controllable clock.
class Rig : IDisposable
{
    public readonly DdcQueue Queue = DdcQueue.Manual();
    public readonly FakeBus Bus = new FakeBus();
    public readonly Settings Settings;
    public readonly MonitorService Service;
    public readonly SaveScheduler Saver;
    public readonly InputWatcher Inputs;
    public readonly ProfileService Profiles;
    public readonly InputLabels Labels;
    public DateTime Now = new DateTime(2026, 10, 20, 12, 0, 0);

    public Rig(Func<List<DdcMonitor>> make, Settings settings = null)
    {
        Bus.Make = make;
        Settings = settings ?? new Settings();
        Settings.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mc-rig-" + Guid.NewGuid().ToString("N") + ".ini");
        Service = new MonitorService(Settings, Bus, Queue, a => a(), () => Now) { FollowUpDelayMs = 0, ResetDelayMs = 0 };
        Labels = new InputLabels(Settings);
        Saver = new SaveScheduler(Service, () => Now);
        Inputs = new InputWatcher(Service, Saver, Labels, () => Now);
        Profiles = new ProfileService(Service);
    }

    public void Run() { Queue.RunPending(); }

    public void Detect()
    {
        Service.Detect(false);
        Run();
    }

    public void Dispose()
    {
        Saver.Dispose();
        Inputs.Dispose();
        Service.Dispose();
        try { System.IO.File.Delete(Settings.Path); } catch (Exception) { }
    }
}
