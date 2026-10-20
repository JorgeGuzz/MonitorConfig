// Console checks: model, services against simulated monitors, and (unless --no-ddc) one harmless DDC write on the
// real XG2705 (it rewrites the current brightness). Build and run: test.cmd [--no-ddc]
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using MonitorConfig;

static class Test
{
    static int failures;

    static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "  ok   " : "  FAIL ") + what);
        if (!ok) failures++;
    }

    static int Main(string[] args)
    {
        Lang.PackPath = @"src\lang.pack";   // built by test.cmd
        Lang.Load("en");
        var xg = Capabilities.Parse("(prot(monitor)type(LCD)model(XG2705)cmds(01 02 03 07 0C E3 F3)vcp(02 04 05 08  0B 0C 10 12 14(01 04 05 06 08 0B) 16 18 1A 60(0F 11 12)  62 87 8D(01 02) AC AE B6 C6 C8 C9 CC(01 02 03 04 05 06 07 09 0A 0B 0C 0D 12 16 ) D6(01 05) CA(01 02) E1 E9(00 02) DC(01 02 03 04 05 06 07 08) DF)mswhql(1)mccs_ver(2.1)asset_eep(64)mpu_ver(V009))");
        var lhc = Capabilities.Parse("(prot(monitor)type(lcd)MStarcmds(01 02 03 07 0C E3 F3)vcp(02 04 05 08 10 12 14(05 08 0B 0C) 16 18 1A 52 60( 11 12 0F) AA(01 02) AC AE B2 B6 C6 C8 C9 D6(01 04 05) DC(00 02 03 05 ) DF FD)mccs_ver(2.1)mswhql(1))");
        ModelTests(xg, lhc);
        ServiceTests(xg, lhc);
        CommandTests(xg, lhc);
        LanguageTests();
        if (!args.Contains("--no-ddc")) RealMonitorTest();
        Console.WriteLine(failures == 0 ? "All passed." : failures + " failure(s).");
        return failures == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- model

    static void ModelTests(Capabilities xg, Capabilities lhc)
    {
        Console.WriteLine("Capabilities:");
        Check(xg.Codes.Count == 29, "XG2705: 29 codes (" + xg.Codes.Count + ")");
        Check(xg.Model == "XG2705" && xg.MccsVersion == "2.1" && xg.Firmware == "V009", "XG2705: model, MCCS and firmware");
        Check(xg.Commands.Contains(0x0C) && new DdcMonitor { Caps = xg }.CanSave, "XG2705: can save settings (command 0C)");
        Check(string.Join(",", xg.ValuesOf(0x60).Select(b => b.ToString("X2")).ToArray()) == "0F,11,12", "XG2705: inputs 0F 11 12");
        Check(xg.ValuesOf(0xCC).Count == 14 && xg.ValuesOf(0x14).Contains(0x0B), "XG2705: 14 languages and the User preset");
        Check(xg.ValuesOf(0x10) == null && xg.Codes.Contains(0xDF), "XG2705: brightness without a value list, DF at the end");
        Check(lhc.Codes.Count == 24 && lhc.ValuesOf(0x60).Count == 3 && lhc.Model == null, "MD27PRO: 24 codes, 3 inputs, no model");
        Check(lhc.Commands.Contains(0x0C) && !new DdcMonitor { Caps = Capabilities.Parse("vcp(10 12)") }.CanSave, "MD27PRO can save; without cmds() it is not tried");
        var broken = Capabilities.Parse("vcp(10 12 14(01 02");
        Check(broken.Codes.Count == 3 && broken.ValuesOf(0x14).Count == 2, "truncated string: the readable part is used");

        var sm = new DdcMonitor();
        sm.Values[Vcp.ColorPreset] = new VcpValue { Current = Vcp.PresetSrgb };
        Check(sm.InSrgb && !new DdcMonitor().InSrgb, "sRGB: only once preset 01 has been read");

        Console.WriteLine("Inputs:");
        var dm = new DdcMonitor { PnpId = "VSC0E39", Caps = xg, Connection = 10 };
        Check(dm.PcInput() == 0x0F, "XG2705 over DisplayPort: This PC = DisplayPort");
        dm.Connection = 5;
        Check(dm.PcInput() == null, "over HDMI with two HDMI inputs and the current one unknown: no guess");
        dm.Values[0x60] = new VcpValue { Current = 0x12 };
        Check(dm.PcInput() == 0x12, "over HDMI while showing HDMI 2: This PC = HDMI 2");
        var lm = new DdcMonitor { PnpId = "LHC2700", Caps = lhc, Connection = 10 };
        lm.Values[0x60] = new VcpValue { Current = 7 };
        Check(lm.CurrentInput() == null && lm.PcInput() == 0x0F, "MD27PRO: code 07 = unknown input; This PC = DisplayPort");
        Check(new DdcMonitor { Caps = lhc }.InputOptions.Count() == 3 && new DdcMonitor().InputOptions.SequenceEqual(DdcMonitor.DefaultInputs), "inputs: the monitor's, or DP/HDMI1/HDMI2");
        var labels = new InputLabels(new Settings());
        var dx = new DdcMonitor { Caps = xg, Connection = 10, PnpId = "VSC0E39" };
        Check(labels.Label(dx, 0x0F) == Strings.ThisPc && labels.Label(dx, 0x11) == "" && labels.Text(dx, 0x0F) == "DisplayPort · " + Strings.ThisPc, "labels: This PC automatically, empty when unknown");

        Console.WriteLine("Identity of each monitor:");
        Check(DdcMonitor.MakeKey("VSC0E39", "W9R000000001", 0, "5&1ce46661&0&UID4352", @"\\.\DISPLAY1") == "VSC0E39-W9R000000001", "with a serial number: model + serial");
        Check(DdcMonitor.MakeKey("LHC2700", "", 0x1234ABCD, "x", "d") == "LHC2700-1234ABCD", "numeric EDID serial");
        Check(DdcMonitor.MakeKey("LHC2700", "", 0x01010101, "5&1ce46661&0&UID4354", "d") == "LHC2700-5&1ce46661&0&UID4354"
            && DdcMonitor.MakeKey("LHC2700", "", 1, "5&1ce46661&0&UID4354", "d") == "LHC2700-5&1ce46661&0&UID4354", "no serial or a filler one (0, 1, 01010101): the Windows connection");
        Check(DdcMonitor.MakeKey("", "", 0, "", @"\\.\DISPLAY2") == @"\\.\DISPLAY2", "no monitor data: the display name");
        Check(new DdcMonitor().Id != new DdcMonitor().Id, "each monitor object has its own queue id");
        var mig = new Settings { ActiveMonitor = "LHC2700" };
        mig.InputNames["LHC2700"] = new Dictionary<byte, string> { { 0x11, "Switch" } };
        mig.Profiles.Add(new Profile { Name = "Night", Monitor = "LHC2700" });
        mig.CapsCache["LHC2700"] = "vcp(10)";
        Check(mig.MigrateMonitorKey("LHC2700", "LHC2700-X") && mig.ActiveMonitor == "LHC2700-X" && mig.InputName("LHC2700-X", 0x11) == "Switch"
            && mig.Profiles[0].Monitor == "LHC2700-X" && mig.CapsCache.ContainsKey("LHC2700-X") && !mig.CapsCache.ContainsKey("LHC2700")
            && !mig.MigrateMonitorKey("LHC2700", "LHC2700-X"), "migration of the old key (only once)");

        Console.WriteLine("Values:");
        Check(Vcp.Format(Vcp.VFreq, new VcpValue { Current = 14381, Max = 0 }).StartsWith("143"), "vertical frequency 143.81 Hz");
        Check(Vcp.Format(Vcp.HFreq, new VcpValue { Current = 26828, Max = 2 }).StartsWith("157"), "horizontal frequency 157.9 kHz");
        Check(Vcp.Format(Vcp.Version, new VcpValue { Current = 513 }) == "2.1", "MCCS version 2.1");
        Check(Vcp.ValueName(Vcp.OsdLanguage, 0x16) == "Suomi" && Vcp.ValueName(Vcp.OsdLanguage, 0x12) == "Čeština", "the monitor's menu languages, each in its own language (12 and 16)");
        Check(Vcp.Percent(45, 100) == 45 && Vcp.Percent(50, 0) == 50 && Vcp.Percent(127, 255) == 50, "one percentage rule (maximum 0 = 0-100 scale)");
        Check(Vcp.IsAction(Vcp.FactoryReset) && !Vcp.IsAction(Vcp.Brightness), "resets are commands, not values");

        Console.WriteLine("Settings:");
        var s = new Settings { StartMinimized = true, CloseToTray = false, ActiveMonitor = "VSC0E39", KeepInput = false, Language = "de", WindowTheme = "dark" };
        var p = new Profile { Name = Settings.CleanProfileName(" Night [warm]\n= "), Monitor = "VSC0E39" };
        p.Values[0x10] = 20; p.Values[0x14] = 4; p.Values[0x16] = 90;
        s.Profiles.Add(p);
        s.Profiles.Add(new Profile { Name = "Café", Monitor = "LHC2700" });
        s.CapsCache["VSC0E39-W9R000000001"] = xg.Raw;
        s.InputNames["VSC0E39"] = new Dictionary<byte, string> { { 0x11, "PS5" }, { 0x12, "Switch [2] = dock" } };
        var back = new Settings();
        back.Parse(s.Serialize());
        Check(back.StartMinimized && !back.KeepInput && !back.CloseToTray && back.ActiveMonitor == "VSC0E39" && back.Language == "de" && back.WindowTheme == "dark",
            "general options");
        Check(back.Profiles.Count == 2 && back.Profiles[0].Name == "Night warm", "profiles: clean name");
        Check(back.Profiles[0].Values.Count == 3 && back.Profiles[0].Values[0x16] == 90 && back.Profiles[1].Name == "Café", "profiles: values and accents");
        Check(back.CapsCache["VSC0E39-W9R000000001"] == xg.Raw, "capabilities cache");
        Check(back.InputName("VSC0E39", 0x11) == "PS5" && back.InputName("VSC0E39", 0x12) == "Switch [2] = dock" && back.InputName("VSC0E39", 0x0F) == null, "input names");
        var v1 = new Settings();   // an .ini written by 1.0, with Spanish names
        v1.Parse("[General]\nIniciarMinimizado=1\nCerrarALaBandeja=0\nMantenerEntrada=0\nMonitor=VSC0E39\nIdioma=fr\nVentanaX=10\n"
            + "[Perfil:Noche]\nMonitor=VSC0E39\n10=30\n[Entradas:VSC0E39]\n11=PS5\n[Capacidades]\nVSC0E39=vcp(10)\n");
        string v1Out = v1.Serialize();
        Check(v1.StartMinimized && !v1.CloseToTray && !v1.KeepInput && v1.Language == "fr" && v1.WindowX == 10 && v1.Profiles.Count == 1
            && v1.Profiles[0].Values[0x10] == 30 && v1.InputName("VSC0E39", 0x11) == "PS5" && v1.CapsCache["VSC0E39"] == "vcp(10)",
            "an .ini from 1.0 (Spanish names) is read");
        Check(v1Out.Contains("[Profile:Noche]") && v1Out.Contains("[Inputs:VSC0E39]") && v1Out.Contains("Language=fr")
            && !v1Out.Contains("Perfil") && !v1Out.Contains("Idioma"), "...and saved with English names");
        var legacy = new Settings();
        legacy.Parse("[General]\nAtajoSubirBrillo=Ctrl+Alt+Down\nPasoBrillo=10\n[Perfil:Viejo]\nAtajo=Ctrl+Alt+N\n10=30\n");
        Check(legacy.Profiles.Count == 1 && legacy.Profiles[0].Values.Count == 1 && !legacy.Serialize().Contains("Atajo"), "an .ini with the old hotkeys is read and they are dropped");
        string tmpIni = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mc-test-" + Guid.NewGuid().ToString("N") + ".ini");
        var saved = new Settings { Path = tmpIni, ActiveMonitor = "A" };
        bool firstSave = saved.Save();
        saved.ActiveMonitor = "B";
        bool secondSave = saved.Save();
        Check(firstSave && secondSave && Settings.Load(tmpIni).ActiveMonitor == "B" && !System.IO.File.Exists(tmpIni + ".tmp"), "atomic save: replaces without leaving temp files");
        string profileIni = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mc-profile.ini");
        Check(Settings.Locate(tmpIni, profileIni) == tmpIni && Settings.Locate(tmpIni + ".none", profileIni) == profileIni,
            "settings: an .ini next to the exe wins (portable); otherwise the one in %APPDATA%");
        System.IO.File.Delete(tmpIni);
        Check(Startup.ExeOf("\"F:\\My folder\\MonitorConfig.exe\" --tray") == @"F:\My folder\MonitorConfig.exe" && Startup.ExeOf(@"C:\x.exe --tray") == @"C:\x.exe", "start with Windows: path of the registered exe");
        Crash.Folder = System.IO.Path.GetTempPath();
        string marker = "test-" + Guid.NewGuid().ToString("N");
        Crash.Log(new InvalidOperationException(marker));
        Check(System.IO.File.ReadAllText(Crash.LogPath).Contains(marker), "unexpected errors end up in MonitorConfig.log");

        var u = new Settings { CheckUpdates = false, LastUpdateCheck = new DateTime(2026, 10, 20), UpdateNotified = "v1.2.0" };
        var uBack = new Settings();
        uBack.Parse(u.Serialize());
        Check(!uBack.CheckUpdates && uBack.LastUpdateCheck == new DateTime(2026, 10, 20) && uBack.UpdateNotified == "v1.2.0" && new Settings().CheckUpdates,
            "update check: option (on by default), last check date and the version already announced");

        Console.WriteLine("Updates:");
        Check(UpdateChecker.ParseTag("v1.2.0") == new Version(1, 2, 0) && UpdateChecker.ParseTag("1.3") == new Version(1, 3, 0)
            && UpdateChecker.ParseTag("v1.2.0-beta") == null && UpdateChecker.ParseTag("latest") == null, "release tags: v1.2.0 and 1.3 are versions; beta and names are not");
        var json = "{\"url\":\"x\",\"html_url\":\"https://github.com/a\",\"tag_name\": \"v9.0.1\",\"name\":\"MonitorConfig 9.0.1\"}";
        var latest = UpdateChecker.Parse(json);
        Check(latest != null && latest.Version == new Version(9, 0, 1) && latest.Url.EndsWith("/releases/tag/v9.0.1"), "GitHub's answer: version and download page");
        var current = UpdateChecker.Current;
        Check(UpdateChecker.IsNewer(latest) && !UpdateChecker.IsNewer(new UpdateChecker.Release { Version = current, Tag = "v" + current })
            && !UpdateChecker.IsNewer(UpdateChecker.Parse("{}")), "newer than " + current + ": 9.0.1 yes; the same version or no answer, no");

        Console.WriteLine("DDC/CI queue (real thread):");
        using (var queue = DdcQueue.Threaded())
        {
            int calls = 0; uint last = 0;
            var gate = new ManualResetEvent(false);
            queue.Post(delegate { gate.WaitOne(); });   // keeps the queue busy while the "slider" moves
            for (uint v = 0; v <= 100; v++) queue.PostCoalesced("brightness", v, delegate(uint x) { calls++; last = x; Thread.Sleep(5); });
            gate.Set();
            var done = new ManualResetEvent(false);
            queue.Post(delegate { done.Set(); });
            done.WaitOne(3000);
            Check(calls == 1 && last == 100, "101 moves -> 1 write with the last value (" + calls + " writes, value " + last + ")");
        }
    }

    // ---------------------------------------------------------------- services against simulated monitors

    static FakeScreen XgScreen(Capabilities caps)
    {
        return new FakeScreen { Caps = caps.Raw }.Reg(0x10, 100).Reg(0x12, 70).Reg(0x14, 0x01, 11).Reg(0x16, 100).Reg(0x18, 100).Reg(0x1A, 100)
            .Reg(0x60, 0x0F, 18).Reg(0x62, 4).Reg(0x8D, 2, 2).Reg(0x87, 50).Reg(0x0C, 35, 85);
    }

    static FakeScreen MdScreen(Capabilities caps)
    {
        return new FakeScreen { Caps = caps.Raw }.Reg(0x10, 68).Reg(0x12, 50).Reg(0x14, 5, 12).Reg(0x60, 7, 14).Reg(0x16, 49).Reg(0x18, 50).Reg(0x1A, 51);
    }

    static DdcMonitor Xg(FakeScreen s)
    {
        return new DdcMonitor
        {
            Port = new FakePort(s), PnpId = "VSC0E39", EdidName = "XG2705", EdidSerial = "W9R000000001", EdidNumSerial = 0x01010101,
            DeviceName = @"\\.\DISPLAY1", Left = -1920, Connection = 10, InstanceId = "5&1ce46661&0&UID4352",
        };
    }

    static DdcMonitor Md(FakeScreen s)
    {
        return new DdcMonitor
        {
            Port = new FakePort(s), PnpId = "LHC2700", EdidName = "MD27PRO", EdidNumSerial = 1,
            DeviceName = @"\\.\DISPLAY2", Left = 0, Primary = true, Connection = 10, InstanceId = "5&1ce46661&0&UID4354",
        };
    }

    static void ServiceTests(Capabilities xgCaps, Capabilities lhcCaps)
    {
        Console.WriteLine("Services with simulated monitors:");

        // Detection, caching, migration, reads, writes, saving
        var xs = XgScreen(xgCaps);
        var ms = MdScreen(lhcCaps);
        var settings = new Settings { ActiveMonitor = "LHC2700" };
        settings.CapsCache["VSC0E39-W9R000000001"] = xgCaps.Raw;
        settings.CapsCache["LHC2700"] = lhcCaps.Raw;
        settings.InputNames["LHC2700"] = new Dictionary<byte, string> { { 0x11, "Laptop" } };
        using (var rig = new Rig(() => new List<DdcMonitor> { Md(ms), Xg(xs) }, settings))   // Windows gives the right one first
        {
            rig.Detect();
            var all = rig.Service.All;
            Check(all.Count == 2 && all[0].EdidName == "XG2705" && all[0].Position == Strings.LeftScreen && all[1].Position == Strings.RightScreen,
                "detection: left to right even when Windows lists them the other way round");
            Check(xs.Count("caps") == 0 && ms.Count("caps") == 0 && all.All(m => m.CapsFromCache), "capabilities from the saved copy (also under the old key): not requested");
            Check(rig.Settings.ActiveMonitor == "LHC2700-5&1ce46661&0&UID4354" && rig.Service.Active == all[1] && rig.Settings.InputName(all[1].Key, 0x11) == "Laptop",
                "migration: the chosen monitor and input names move to the individual key");
            Check(all[0].Values.ContainsKey(Vcp.Brightness) && all[0].Values.ContainsKey(Vcp.Input) && !all[0].Values.ContainsKey(Vcp.Contrast),
                "at startup only what the tray shows is read (brightness, preset, volume, mute, input)");

            var x = all[0];
            int before = xs.Count("get 12");
            rig.Service.Read(x, new[] { Vcp.Contrast }, null);
            rig.Service.Read(x, new[] { Vcp.Contrast }, null);
            rig.Run();
            Check(xs.Count("get 12") - before == 1, "two identical queued reads: the monitor gets one");

            rig.Service.Set(x, Vcp.Brightness, 30);
            rig.Service.Set(x, Vcp.Brightness, 40);
            rig.Service.Set(x, Vcp.Brightness, 50);
            Check(x.Values[Vcp.Brightness].Current == 50, "the window shows the new value at once");
            rig.Run();
            Check(xs.Count("set 10=30") == 0 && xs.Count("set 10=40") == 0 && xs.Count("set 10=50") == 1, "slider: 3 moves -> 1 write with the last value");

            xs.OnSet = (code, v) => { if (code == Vcp.ColorPreset) xs.Registers[Vcp.Brightness].Current = 80; return true; };
            rig.Service.Set(x, Vcp.ColorPreset, 0x0B);
            rig.Run();
            xs.OnSet = null;
            Check(x.Values[Vcp.Brightness].Current == 80, "changing the preset re-reads what the monitor changed by itself");

            xs.Registers[Vcp.ColorPreset].Current = Vcp.PresetSrgb;
            rig.Service.Read(x, new[] { Vcp.ColorPreset }, null);
            rig.Run();
            Check(x.InSrgb, "sRGB detected: the brightness read may not be the real one");
            xs.OnSet = (code, v) => { if (code == Vcp.Brightness) xs.Registers[Vcp.ColorPreset].Current = Vcp.PresetUser1; return true; };
            rig.Service.Set(x, Vcp.Brightness, 45);
            rig.Run();
            xs.OnSet = null;
            Check(!x.InSrgb && x.Values[Vcp.ColorPreset].Current == Vcp.PresetUser1, "a monitor that leaves sRGB when the brightness changes: the preset is re-read");

            xs.OnSet = (code, v) => code != Vcp.Sharpness;
            string status = null;
            rig.Service.StatusChanged += (text, error) => { if (error) status = text; };
            rig.Service.Set(x, Vcp.Sharpness, 10);
            rig.Run();
            xs.OnSet = null;
            Check(status == Strings.WriteRejected(Vcp.Name(Vcp.Sharpness)) && x.Values[Vcp.Sharpness].Current == 50, "a change the monitor rejects: warning, and the real value is re-read");

            rig.Now = rig.Now.AddSeconds(10);
            rig.Saver.Tick(rig.Now);   // whatever was pending is due now
            rig.Run();
            int saves = xs.Count("save");
            rig.Service.Set(x, Vcp.Brightness, 55);
            rig.Run();
            rig.Saver.Tick(rig.Now.AddSeconds(1));
            rig.Run();
            bool notYet = xs.Count("save") == saves;
            rig.Saver.Tick(rig.Now.AddSeconds(3));
            rig.Run();
            Check(notYet && xs.Count("save") == saves + 1, "saved to the monitor 2 s after the last change, not before");
            rig.Service.Set(x, Vcp.Input, 0x11);
            rig.Run();
            Check(!rig.Saver.IsPending(x), "changing the input does not count as a setting to save");

            rig.Service.Set(x, Vcp.Brightness, 65);
            rig.Inputs.Change(x, 0x12);
            rig.Run();
            int save = xs.LastIndexOf("save");
            Check(save > xs.LastIndexOf("set 10=65") && save < xs.LastIndexOf("set 60=18"), "pending changes are saved before switching input (the monitor reloads its memory)");
        }

        // Exit: pending changes are stored before the monitors close
        var xs2 = XgScreen(xgCaps);
        var rig2 = new Rig(() => new List<DdcMonitor> { Xg(xs2) });
        rig2.Detect();
        rig2.Service.Set(rig2.Service.All[0], Vcp.Brightness, 42);
        rig2.Run();
        rig2.Dispose();
        Check(xs2.LastIndexOf("save") >= 0 && xs2.LastIndexOf("save") < xs2.IndexOf("close"), "on exit, pending changes are saved before the monitors close");

        // A write right after a re-detection
        var xs3 = XgScreen(xgCaps);
        using (var rig3 = new Rig(() => new List<DdcMonitor> { Xg(xs3) }))
        {
            rig3.Detect();
            var old = rig3.Service.All[0];
            bool once = false;
            rig3.Service.MonitorsChanged += () => { if (once) return; once = true; rig3.Service.Set(rig3.Service.All[0], Vcp.Brightness, 33); };
            rig3.Service.Detect(false);                  // Windows reported a display change
            rig3.Service.Set(old, Vcp.Brightness, 20);   // meanwhile the slider still points at the old object
            rig3.Run();
            Check(xs3.Count("set 10=33") == 1, "a write right after a re-detection is not lost");
        }

        // Profiles
        var xs4 = XgScreen(xgCaps);
        var ms4 = MdScreen(lhcCaps);
        bool xgOn = true;
        using (var rig4 = new Rig(() => xgOn ? new List<DdcMonitor> { Xg(xs4), Md(ms4) } : new List<DdcMonitor> { Md(ms4) }))
        {
            rig4.Detect();
            var xgm = rig4.Service.All.First(m => m.EdidName == "XG2705");
            var night = new Profile { Name = "Night", Monitor = xgm.Key };
            night.Values[Vcp.Brightness] = 20;
            night.Values[Vcp.ColorPreset] = 0x04;
            string shown = null;
            rig4.Profiles.Indicator += (m, title, pct) => shown = title;
            Check(rig4.Profiles.Apply(night) && shown == Strings.ProfileIndicator("Night"), "applying a profile shows it on screen");
            rig4.Run();
            int preset = xs4.IndexOf("set 14=4"), bright = xs4.IndexOf("set 10=20");
            Check(preset >= 0 && preset < bright && xs4.LastIndexOf("get 10") > bright, "profile: preset first, then brightness, then re-read");
            xgOn = false;
            rig4.Detect();
            Check(rig4.Profiles.MonitorFor(night) == null && !rig4.Profiles.Apply(night) && ms4.Count("set 10=20") == 0, "with its monitor disconnected, a profile is not applied to another one");
        }

        // Input that bounces back
        foreach (var keep in new[] { true, false })
        {
            var xs5 = XgScreen(xgCaps);
            using (var rig5 = new Rig(() => new List<DdcMonitor> { Xg(xs5) }, new Settings { KeepInput = keep }))
            {
                rig5.Detect();
                var m5 = rig5.Service.All[0];
                xs5.OnGet = code => code == Vcp.Input ? (uint?)0x0F : null;   // the console gives no picture: back to DisplayPort
                string note = null;
                rig5.Inputs.Notify += text => note = text;
                rig5.Inputs.Change(m5, 0x11);
                rig5.Run();
                for (int i = 0; i < 4; i++)
                {
                    rig5.Now = rig5.Now.AddSeconds(1.5);
                    rig5.Inputs.Tick(rig5.Now);
                    rig5.Run();
                }
                if (keep) Check(xs5.Count("set 60=17") == 3 && note != null && note.Contains("HDMI 1"), "when the monitor jumps back: 2 retries and a notice");
                else Check(xs5.Count("set 60=17") == 1 && note == null, "with the option off it does not insist");
            }
        }
        Check(InputWatcher.Decide(0x11, 0x11, 0) == InputWatcher.Step.Wait && InputWatcher.Decide(null, 0x11, 0) == InputWatcher.Step.Wait
            && InputWatcher.Decide(0x0F, 0x11, 1) == InputWatcher.Step.Retry && InputWatcher.Decide(0x0F, 0x11, 2) == InputWatcher.Step.GiveUp,
            "retry decision: wait / retry / give up");
    }

    // ---------------------------------------------------------------- command line

    static void CommandTests(Capabilities xgCaps, Capabilities lhcCaps)
    {
        Console.WriteLine("Command line:");
        Check(CommandRunner.IsCommand(new[] { "--brightness", "40" }) && CommandRunner.IsCommand(new[] { "--LIST" })
            && !CommandRunner.IsCommand(new[] { "--tray" }) && !CommandRunner.IsCommand(new string[0]), "commands are told apart from --tray and a plain start");
        var xs = XgScreen(xgCaps);
        var ms = MdScreen(lhcCaps);
        var settings = new Settings();
        settings.InputNames["VSC0E39-W9R000000001"] = new Dictionary<byte, string> { { 0x11, "Switch" } };
        using (var rig = new Rig(() => new List<DdcMonitor> { Xg(xs), Md(ms) }, settings))
        {
            rig.Detect();
            Func<string, CommandRunner.Result> run = line =>
            {
                var r = CommandRunner.Run(line.Split(' '), rig.Service, rig.Profiles, rig.Labels, rig.Settings);
                rig.Run();
                return r;
            };
            var all = run("--brightness 40");
            Check(all.Code == 0 && xs.Count("set 10=40") == 1 && ms.Count("set 10=40") == 1, "--brightness 40: every monitor");
            var one = run("--monitor xg2705 --brightness +15");
            Check(one.Code == 0 && xs.Count("set 10=55") == 1 && ms.Count("set 10=55") == 0 && one.Text.Contains("55 %"), "--monitor by model, relative change from the current value");
            Check(run("--monitor 2 --brightness -100").Code == 0 && ms.Count("set 10=0") == 1, "--monitor by position from the left; relative changes stop at 0");
            var volume = run("--volume 30");
            Check(volume.Code == 0 && xs.Count("set 62=30") == 1 && volume.Text.Contains("MD27PRO: has no volume"), "a control only one monitor has: the other one is named and skipped");
            Check(run("--monitor 1 --mute toggle").Code == 0 && xs.Count("set 8D=1") == 1 && run("--monitor 1 --mute toggle").Code == 0 && xs.Count("set 8D=2") == 1, "--mute toggle, twice");
            var ambiguous = run("--input hdmi1");
            Check(ambiguous.Code == 2 && xs.Count("set 60=17") == 0, "--input without --monitor and two monitors: refused");
            Check(run("--monitor XG2705 --input HDMI-1").Code == 0 && xs.Count("set 60=17") == 1 && run("--monitor 1 --input switch").Code == 0 && xs.Count("set 60=17") == 2
                && run("--monitor 1 --input dp").Code == 0 && xs.Count("set 60=15") == 1, "--input by name (HDMI-1 = HDMI 1), by the name you gave it, and dp");
            Check(run("--monitor 1 --input vga").Code == 2, "an input the monitor does not have");
            Check(run("--monitor 1 --set 0x87=60").Code == 0 && xs.Count("set 87=60") == 1, "--set with a raw VCP code");
            var night = new Profile { Name = "Night", Monitor = rig.Service.All[0].Key };
            night.Values[Vcp.Brightness] = 12;
            rig.Settings.Profiles.Add(night);
            Check(run("--profile night").Code == 0 && xs.Count("set 10=12") == 1, "--profile by name (any case)");
            Check(run("--profile nope").Code == 2 && run("--monitor nope --brightness 1").Code == 2 && run("--brightness 101").Code == 2
                && run("--brightness").Code == 2 && run("--frobnicate 1").Code == 2 && run("--set 10").Code == 2, "mistakes: exit code 2 and nothing written");
            var list = run("--list");
            Check(list.Code == 0 && list.Text.Contains("1. XG2705") && list.Text.Contains("2. MD27PRO") && list.Text.Contains("Switch") && list.Text.Contains("Night"),
                "--list: monitors left to right, inputs with their names, profiles");

            string pipe = "MonitorConfig.Test." + Guid.NewGuid().ToString("N");
            var server = new CommandServer(pipe, a => a(), a => CommandRunner.Run(a, rig.Service, rig.Profiles, rig.Labels, rig.Settings));
            var remote = CommandServer.Send(pipe, new[] { "--monitor", "1", "--brightness", "77" }, 3000);
            rig.Run();
            server.Dispose();
            Check(remote != null && remote.Code == 0 && remote.Text.Contains("77 %") && xs.Count("set 10=77") == 1, "through the pipe to the running copy: same answer, same writes");
            Check(CommandServer.Send(pipe + "x", new[] { "--list" }, 200) == null, "no copy listening: the caller does it itself");
        }
    }

    // ---------------------------------------------------------------- languages

    static object Dummy(Type t)
    {
        if (t == typeof(string)) return "x";
        if (t == typeof(int)) return 1;
        if (t == typeof(uint)) return 1u;
        if (t == typeof(byte)) return (byte)1;
        if (t == typeof(bool)) return true;
        return null;
    }

    static void LanguageTests()
    {
        Console.WriteLine("Languages:");
        Check(Lang.Order.Length == 9 && Lang.Order[0] == "en" && Lang.Order[1] == "es", "9 languages: English first, Spanish second");
        Check(Lang.Resolve("") == Lang.SystemLanguage && Lang.Resolve("xx") == Lang.SystemLanguage && Lang.Resolve("de") == "de",
            "automatic = the Windows language (" + Lang.SystemLanguage + "); a chosen one wins");
        var props = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static);
        var methods = typeof(Strings).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(string) && !m.IsSpecialName).ToList();
        byte[] vcpCodes = { 0x10, 0x14, 0x60, 0x8D, 0xCA, 0xD6, 0xDC, 0xB6 };
        var keys = System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(@"src\Strings.cs"), @"[TF]\(""([A-Za-z]+)""")
            .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
        Check(keys.Count > 150, "keys used by the code: " + keys.Count);
        foreach (var code in Lang.Order)
        {
            Lang.Load(code);
            var bad = keys.Where(k => !Lang.Has(k)).ToList();   // every key the code uses exists in this language
            foreach (var p in props)
                if (string.IsNullOrEmpty((string)p.GetValue(null, null))) bad.Add(p.Name);
            foreach (var m in methods)
            {
                var text = (string)m.Invoke(null, m.GetParameters().Select(x => Dummy(x.ParameterType)).ToArray());
                if (string.IsNullOrEmpty(text) || text.Contains("{")) bad.Add(m.Name + "()");   // placeholders all filled
            }
            foreach (var c in vcpCodes)
                if (Vcp.Name(c).StartsWith("vcp.") || Vcp.Name(c) == Strings.VcpCode(c)) bad.Add("vcp." + c.ToString("X2"));
            if (Vcp.ValueName(Vcp.ColorPreset, 0x0B) == Strings.VcpValueUnknown(0x0B)) bad.Add("vcp.14.0B");
            Check(Lang.Current == code && Lang.NameOf(code) != code && bad.Count == 0,
                code + " (" + Lang.NameOf(code) + "): " + props.Length + " texts and " + methods.Count + " texts with data" + (bad.Count > 0 ? " — MISSING: " + string.Join(", ", bad.ToArray()) : ""));
        }
        Lang.Load("en");
    }

    // ---------------------------------------------------------------- real monitor

    static void RealMonitorTest()
    {
        Console.WriteLine("Real DDC/CI (XG2705):");
        var mons = new Dxva2Bus().Enumerate();
        var m = mons.FirstOrDefault(x => x.EdidName == "XG2705");
        Check(m != null, "found by EDID (" + string.Join(", ", mons.Select(x => x.EdidName + "/" + x.PnpId).ToArray()) + ")");
        if (m != null)
        {
            VcpValue before, after;
            var sw = Stopwatch.StartNew();
            Check(m.TryGet(Vcp.Brightness, out before), "reads the brightness: " + (before != null ? before.Current + "/" + before.Max : "-") + " (" + sw.ElapsedMilliseconds + " ms)");
            if (before != null)
            {
                sw.Restart();
                Check(m.TrySet(Vcp.Brightness, before.Current), "rewrites the same brightness (" + sw.ElapsedMilliseconds + " ms)");
                Thread.Sleep(100);
                Check(m.TryGet(Vcp.Brightness, out after) && after.Current == before.Current, "unchanged after the write: " + (after != null ? after.Current.ToString() : "-"));
            }
        }
        foreach (var x in mons) x.Close();
    }
}
