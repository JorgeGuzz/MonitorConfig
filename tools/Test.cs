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
        Lang.Load("es");
        var xg = Capabilities.Parse("(prot(monitor)type(LCD)model(XG2705)cmds(01 02 03 07 0C E3 F3)vcp(02 04 05 08  0B 0C 10 12 14(01 04 05 06 08 0B) 16 18 1A 60(0F 11 12)  62 87 8D(01 02) AC AE B6 C6 C8 C9 CC(01 02 03 04 05 06 07 09 0A 0B 0C 0D 12 16 ) D6(01 05) CA(01 02) E1 E9(00 02) DC(01 02 03 04 05 06 07 08) DF)mswhql(1)mccs_ver(2.1)asset_eep(64)mpu_ver(V009))");
        var lhc = Capabilities.Parse("(prot(monitor)type(lcd)MStarcmds(01 02 03 07 0C E3 F3)vcp(02 04 05 08 10 12 14(05 08 0B 0C) 16 18 1A 52 60( 11 12 0F) AA(01 02) AC AE B2 B6 C6 C8 C9 D6(01 04 05) DC(00 02 03 05 ) DF FD)mccs_ver(2.1)mswhql(1))");
        ModelTests(xg, lhc);
        ServiceTests(xg, lhc);
        LanguageTests();
        if (!args.Contains("--no-ddc")) RealMonitorTest();
        Console.WriteLine(failures == 0 ? "Todo correcto." : failures + " fallo(s).");
        return failures == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- model

    static void ModelTests(Capabilities xg, Capabilities lhc)
    {
        Console.WriteLine("Capacidades:");
        Check(xg.Codes.Count == 29, "XG2705: 29 códigos (" + xg.Codes.Count + ")");
        Check(xg.Model == "XG2705" && xg.MccsVersion == "2.1" && xg.Firmware == "V009", "XG2705: modelo, MCCS y firmware");
        Check(xg.Commands.Contains(0x0C) && new DdcMonitor { Caps = xg }.CanSave, "XG2705: admite guardar ajustes (orden 0C)");
        Check(string.Join(",", xg.ValuesOf(0x60).Select(b => b.ToString("X2")).ToArray()) == "0F,11,12", "XG2705: entradas 0F 11 12");
        Check(xg.ValuesOf(0xCC).Count == 14 && xg.ValuesOf(0x14).Contains(0x0B), "XG2705: 14 idiomas y preajuste Usuario");
        Check(xg.ValuesOf(0x10) == null && xg.Codes.Contains(0xDF), "XG2705: brillo sin lista, DF al final");
        Check(lhc.Codes.Count == 24 && lhc.ValuesOf(0x60).Count == 3 && lhc.Model == null, "MD27PRO: 24 códigos, 3 entradas, sin modelo");
        Check(lhc.Commands.Contains(0x0C) && !new DdcMonitor { Caps = Capabilities.Parse("vcp(10 12)") }.CanSave, "MD27PRO admite guardar; sin cmds() no se intenta");
        var broken = Capabilities.Parse("vcp(10 12 14(01 02");
        Check(broken.Codes.Count == 3 && broken.ValuesOf(0x14).Count == 2, "cadena cortada: se aprovecha lo legible");

        Console.WriteLine("Entradas:");
        var dm = new DdcMonitor { PnpId = "VSC0E39", Caps = xg, Connection = 10 };
        Check(dm.PcInput() == 0x0F, "XG2705 por DisplayPort: «Este PC» = DisplayPort");
        dm.Connection = 5;
        Check(dm.PcInput() == null, "por HDMI con dos HDMI y entrada desconocida: no adivina");
        dm.Values[0x60] = new VcpValue { Current = 0x12 };
        Check(dm.PcInput() == 0x12, "por HDMI mostrando HDMI 2: «Este PC» = HDMI 2");
        var lm = new DdcMonitor { PnpId = "LHC2700", Caps = lhc, Connection = 10 };
        lm.Values[0x60] = new VcpValue { Current = 7 };
        Check(lm.CurrentInput() == null && lm.PcInput() == 0x0F, "MD27PRO: código 07 = entrada desconocida; «Este PC» = DisplayPort");
        Check(new DdcMonitor { Caps = lhc }.InputOptions.Count() == 3 && new DdcMonitor().InputOptions.SequenceEqual(DdcMonitor.DefaultInputs), "entradas: las del monitor o DP/HDMI1/HDMI2");
        var labels = new InputLabels(new Settings());
        var dx = new DdcMonitor { Caps = xg, Connection = 10, PnpId = "VSC0E39" };
        Check(labels.Label(dx, 0x0F) == Strings.ThisPc && labels.Label(dx, 0x11) == "" && labels.Text(dx, 0x0F) == "DisplayPort · " + Strings.ThisPc, "nombres: «Este PC» automático, vacío si no se sabe");

        Console.WriteLine("Identidad de cada monitor:");
        Check(DdcMonitor.MakeKey("VSC0E39", "W9R000000001", 0, "5&1ce46661&0&UID4352", @"\\.\DISPLAY1") == "VSC0E39-W9R000000001", "con número de serie: modelo + serie");
        Check(DdcMonitor.MakeKey("LHC2700", "", 0x1234ABCD, "x", "d") == "LHC2700-1234ABCD", "serie numérica del EDID");
        Check(DdcMonitor.MakeKey("LHC2700", "", 0x01010101, "5&1ce46661&0&UID4354", "d") == "LHC2700-5&1ce46661&0&UID4354"
            && DdcMonitor.MakeKey("LHC2700", "", 1, "5&1ce46661&0&UID4354", "d") == "LHC2700-5&1ce46661&0&UID4354", "sin serie o de relleno (0, 1, 01010101): la conexión de Windows");
        Check(DdcMonitor.MakeKey("", "", 0, "", @"\\.\DISPLAY2") == @"\\.\DISPLAY2", "sin datos del monitor: el nombre de pantalla");
        Check(new DdcMonitor().Id != new DdcMonitor().Id, "cada objeto de monitor tiene su propio id de cola");
        var mig = new Settings { ActiveMonitor = "LHC2700" };
        mig.InputNames["LHC2700"] = new Dictionary<byte, string> { { 0x11, "Switch" } };
        mig.Profiles.Add(new Profile { Name = "Noche", Monitor = "LHC2700" });
        mig.CapsCache["LHC2700"] = "vcp(10)";
        Check(mig.MigrateMonitorKey("LHC2700", "LHC2700-X") && mig.ActiveMonitor == "LHC2700-X" && mig.InputName("LHC2700-X", 0x11) == "Switch"
            && mig.Profiles[0].Monitor == "LHC2700-X" && mig.CapsCache.ContainsKey("LHC2700-X") && !mig.CapsCache.ContainsKey("LHC2700")
            && !mig.MigrateMonitorKey("LHC2700", "LHC2700-X"), "migración de la clave antigua (una sola vez)");

        Console.WriteLine("Valores:");
        Check(Vcp.Format(Vcp.VFreq, new VcpValue { Current = 14381, Max = 0 }).StartsWith("143"), "frecuencia vertical 143,81 Hz");
        Check(Vcp.Format(Vcp.HFreq, new VcpValue { Current = 26828, Max = 2 }).StartsWith("157"), "frecuencia horizontal 157,9 kHz");
        Check(Vcp.Format(Vcp.Version, new VcpValue { Current = 513 }) == "2.1", "versión MCCS 2.1");
        Check(Vcp.ValueName(Vcp.OsdLanguage, 0x16) == "Suomi" && Vcp.ValueName(Vcp.OsdLanguage, 0x12) == "Čeština", "idiomas del menú del monitor, cada uno en su lengua (12 y 16)");
        Check(Vcp.Percent(45, 100) == 45 && Vcp.Percent(50, 0) == 50 && Vcp.Percent(127, 255) == 50, "porcentaje único (máximo 0 = escala 0-100)");
        Check(Vcp.IsAction(Vcp.FactoryReset) && !Vcp.IsAction(Vcp.Brightness), "las restauraciones son órdenes, no valores");

        Console.WriteLine("Configuración:");
        var s = new Settings { StartMinimized = true, CloseToTray = false, ActiveMonitor = "VSC0E39", KeepInput = false };
        var p = new Profile { Name = Settings.CleanProfileName(" Noche [cálida]\n= "), Monitor = "VSC0E39" };
        p.Values[0x10] = 20; p.Values[0x14] = 4; p.Values[0x16] = 90;
        s.Profiles.Add(p);
        s.Profiles.Add(new Profile { Name = "Día", Monitor = "LHC2700" });
        s.CapsCache["VSC0E39-W9R000000001"] = xg.Raw;
        s.InputNames["VSC0E39"] = new Dictionary<byte, string> { { 0x11, "PS5" }, { 0x12, "Switch [2] = dock" } };
        var back = new Settings();
        back.Parse(s.Serialize());
        Check(back.StartMinimized && !back.KeepInput && !back.CloseToTray && back.ActiveMonitor == "VSC0E39", "opciones generales");
        Check(back.Profiles.Count == 2 && back.Profiles[0].Name == "Noche cálida", "perfiles: nombre limpio");
        Check(back.Profiles[0].Values.Count == 3 && back.Profiles[0].Values[0x16] == 90 && back.Profiles[1].Name == "Día", "perfiles: valores y acentos");
        Check(back.CapsCache["VSC0E39-W9R000000001"] == xg.Raw, "copia de capacidades");
        Check(back.InputName("VSC0E39", 0x11) == "PS5" && back.InputName("VSC0E39", 0x12) == "Switch [2] = dock" && back.InputName("VSC0E39", 0x0F) == null, "nombres de las entradas");
        var legacy = new Settings();
        legacy.Parse("[General]\nAtajoSubirBrillo=Ctrl+Alt+Down\nPasoBrillo=10\n[Perfil:Viejo]\nAtajo=Ctrl+Alt+N\n10=30\n");
        Check(legacy.Profiles.Count == 1 && legacy.Profiles[0].Values.Count == 1 && !legacy.Serialize().Contains("Atajo"), "un .ini con atajos antiguos se lee y los olvida");
        string tmpIni = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "mc-test-" + Guid.NewGuid().ToString("N") + ".ini");
        var saved = new Settings { Path = tmpIni, ActiveMonitor = "A" };
        bool firstSave = saved.Save();
        saved.ActiveMonitor = "B";
        bool secondSave = saved.Save();
        Check(firstSave && secondSave && Settings.Load(tmpIni).ActiveMonitor == "B" && !System.IO.File.Exists(tmpIni + ".tmp"), "guardado atómico: reemplaza sin dejar temporales");
        System.IO.File.Delete(tmpIni);
        Check(Startup.ExeOf("\"F:\\Mi carpeta\\MonitorConfig.exe\" --tray") == @"F:\Mi carpeta\MonitorConfig.exe" && Startup.ExeOf(@"C:\x.exe --tray") == @"C:\x.exe", "inicio con Windows: ruta del exe registrado");
        Crash.Folder = System.IO.Path.GetTempPath();
        string marker = "prueba-" + Guid.NewGuid().ToString("N");
        Crash.Log(new InvalidOperationException(marker));
        Check(System.IO.File.ReadAllText(Crash.LogPath).Contains(marker), "los errores inesperados quedan en MonitorConfig.log");

        Console.WriteLine("Cola DDC/CI (hilo real):");
        using (var queue = DdcQueue.Threaded())
        {
            int calls = 0; uint last = 0;
            var gate = new ManualResetEvent(false);
            queue.Post(delegate { gate.WaitOne(); });   // keeps the queue busy while the "slider" moves
            for (uint v = 0; v <= 100; v++) queue.PostCoalesced("brillo", v, delegate(uint x) { calls++; last = x; Thread.Sleep(5); });
            gate.Set();
            var done = new ManualResetEvent(false);
            queue.Post(delegate { done.Set(); });
            done.WaitOne(3000);
            Check(calls == 1 && last == 100, "101 movimientos -> 1 escritura con el último valor (" + calls + " escrituras, valor " + last + ")");
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
        Console.WriteLine("Servicios con monitores simulados:");

        // Detection, caching, migration, reads, writes, saving
        var xs = XgScreen(xgCaps);
        var ms = MdScreen(lhcCaps);
        var settings = new Settings { ActiveMonitor = "LHC2700" };
        settings.CapsCache["VSC0E39-W9R000000001"] = xgCaps.Raw;
        settings.CapsCache["LHC2700"] = lhcCaps.Raw;
        settings.InputNames["LHC2700"] = new Dictionary<byte, string> { { 0x11, "Portátil" } };
        using (var rig = new Rig(() => new List<DdcMonitor> { Md(ms), Xg(xs) }, settings))   // Windows gives the right one first
        {
            rig.Detect();
            var all = rig.Service.All;
            Check(all.Count == 2 && all[0].EdidName == "XG2705" && all[0].Position == Strings.LeftScreen && all[1].Position == Strings.RightScreen,
                "detección: de izquierda a derecha aunque Windows los dé al revés");
            Check(xs.Count("caps") == 0 && ms.Count("caps") == 0 && all.All(m => m.CapsFromCache), "capacidades de la copia guardada (también con la clave antigua): sin pedirlas");
            Check(rig.Settings.ActiveMonitor == "LHC2700-5&1ce46661&0&UID4354" && rig.Service.Active == all[1] && rig.Settings.InputName(all[1].Key, 0x11) == "Portátil",
                "migración: monitor elegido y nombres pasan a la clave individual");
            Check(all[0].Values.ContainsKey(Vcp.Brightness) && all[0].Values.ContainsKey(Vcp.Input) && !all[0].Values.ContainsKey(Vcp.Contrast),
                "al arrancar solo se lee lo de la bandeja (brillo, volumen, silencio, entrada)");

            var x = all[0];
            int before = xs.Count("get 12");
            rig.Service.Read(x, new[] { Vcp.Contrast }, null);
            rig.Service.Read(x, new[] { Vcp.Contrast }, null);
            rig.Run();
            Check(xs.Count("get 12") - before == 1, "dos lecturas iguales en cola: el monitor recibe una");

            rig.Service.Set(x, Vcp.Brightness, 30);
            rig.Service.Set(x, Vcp.Brightness, 40);
            rig.Service.Set(x, Vcp.Brightness, 50);
            Check(x.Values[Vcp.Brightness].Current == 50, "la ventana muestra el valor nuevo al instante");
            rig.Run();
            Check(xs.Count("set 10=30") == 0 && xs.Count("set 10=40") == 0 && xs.Count("set 10=50") == 1, "deslizador: 3 movimientos -> 1 escritura con el último valor");

            xs.OnSet = (code, v) => { if (code == Vcp.ColorPreset) xs.Registers[Vcp.Brightness].Current = 80; return true; };
            rig.Service.Set(x, Vcp.ColorPreset, 0x0B);
            rig.Run();
            xs.OnSet = null;
            Check(x.Values[Vcp.Brightness].Current == 80, "cambiar el preajuste relee lo que el monitor cambió por su cuenta");

            xs.OnSet = (code, v) => code != Vcp.Sharpness;
            string status = null;
            rig.Service.StatusChanged += (text, error) => { if (error) status = text; };
            rig.Service.Set(x, Vcp.Sharpness, 10);
            rig.Run();
            xs.OnSet = null;
            Check(status == Strings.WriteRejected(Vcp.Name(Vcp.Sharpness)) && x.Values[Vcp.Sharpness].Current == 50, "si el monitor rechaza un cambio: aviso y se relee el valor real");

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
            Check(notYet && xs.Count("save") == saves + 1, "se guarda en el monitor 2 s después del último cambio, no antes");
            rig.Service.Set(x, Vcp.Input, 0x11);
            rig.Run();
            Check(!rig.Saver.IsPending(x), "cambiar la entrada no cuenta como ajuste que guardar");

            rig.Service.Set(x, Vcp.Brightness, 65);
            rig.Inputs.Change(x, 0x12);
            rig.Run();
            int save = xs.LastIndexOf("save");
            Check(save > xs.LastIndexOf("set 10=65") && save < xs.LastIndexOf("set 60=18"), "antes de cambiar de entrada se guarda lo pendiente (el monitor recarga su memoria)");
        }

        // Exit: pending changes are stored before the monitors close
        var xs2 = XgScreen(xgCaps);
        var rig2 = new Rig(() => new List<DdcMonitor> { Xg(xs2) });
        rig2.Detect();
        rig2.Service.Set(rig2.Service.All[0], Vcp.Brightness, 42);
        rig2.Run();
        rig2.Dispose();
        Check(xs2.LastIndexOf("save") >= 0 && xs2.LastIndexOf("save") < xs2.IndexOf("close"), "al salir, lo pendiente se guarda antes de cerrar los monitores");

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
            Check(xs3.Count("set 10=33") == 1, "una escritura justo después de redetectar no se pierde");
        }

        // Profiles
        var xs4 = XgScreen(xgCaps);
        var ms4 = MdScreen(lhcCaps);
        bool xgOn = true;
        using (var rig4 = new Rig(() => xgOn ? new List<DdcMonitor> { Xg(xs4), Md(ms4) } : new List<DdcMonitor> { Md(ms4) }))
        {
            rig4.Detect();
            var xgm = rig4.Service.All.First(m => m.EdidName == "XG2705");
            var night = new Profile { Name = "Noche", Monitor = xgm.Key };
            night.Values[Vcp.Brightness] = 20;
            night.Values[Vcp.ColorPreset] = 0x04;
            string shown = null;
            rig4.Profiles.Indicator += (m, title, pct) => shown = title;
            Check(rig4.Profiles.Apply(night) && shown == Strings.ProfileIndicator("Noche"), "aplicar un perfil avisa en pantalla");
            rig4.Run();
            int preset = xs4.IndexOf("set 14=4"), bright = xs4.IndexOf("set 10=20");
            Check(preset >= 0 && preset < bright && xs4.LastIndexOf("get 10") > bright, "perfil: primero el preajuste, después el brillo, y luego relee");
            xgOn = false;
            rig4.Detect();
            Check(rig4.Profiles.MonitorFor(night) == null && !rig4.Profiles.Apply(night) && ms4.Count("set 10=20") == 0, "con su monitor desconectado, el perfil no se aplica a otro");
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
                if (keep) Check(xs5.Count("set 60=17") == 3 && note != null && note.Contains("HDMI 1"), "si el monitor vuelve solo: 2 reintentos y aviso");
                else Check(xs5.Count("set 60=17") == 1 && note == null, "con la opción desactivada no insiste");
            }
        }
        Check(InputWatcher.Decide(0x11, 0x11, 0) == InputWatcher.Step.Wait && InputWatcher.Decide(null, 0x11, 0) == InputWatcher.Step.Wait
            && InputWatcher.Decide(0x0F, 0x11, 1) == InputWatcher.Step.Retry && InputWatcher.Decide(0x0F, 0x11, 2) == InputWatcher.Step.GiveUp,
            "decisión de reintento: esperar / reintentar / rendirse");
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
        Console.WriteLine("Idiomas:");
        Check(Lang.Order.Length == 9 && Lang.Order[0] == "en" && Lang.Order[1] == "es", "9 idiomas: inglés primero, español segundo");
        Check(Lang.Resolve("") == Lang.SystemLanguage && Lang.Resolve("xx") == Lang.SystemLanguage && Lang.Resolve("de") == "de",
            "automático = idioma de Windows (" + Lang.SystemLanguage + "); uno elegido manda");
        var props = typeof(Strings).GetProperties(BindingFlags.Public | BindingFlags.Static);
        var methods = typeof(Strings).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == typeof(string) && !m.IsSpecialName).ToList();
        byte[] vcpCodes = { 0x10, 0x14, 0x60, 0x8D, 0xCA, 0xD6, 0xDC, 0xB6 };
        var keys = System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(@"src\Strings.cs"), @"[TF]\(""([A-Za-z]+)""")
            .Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
        Check(keys.Count > 150, "claves usadas por el código: " + keys.Count);
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
                code + " (" + Lang.NameOf(code) + "): " + props.Length + " textos y " + methods.Count + " frases con datos" + (bad.Count > 0 ? " — FALTAN: " + string.Join(", ", bad.ToArray()) : ""));
        }
        Lang.Load("es");
    }

    // ---------------------------------------------------------------- real monitor

    static void RealMonitorTest()
    {
        Console.WriteLine("DDC/CI real (XG2705):");
        var mons = new Dxva2Bus().Enumerate();
        var m = mons.FirstOrDefault(x => x.EdidName == "XG2705");
        Check(m != null, "encontrado por EDID (" + string.Join(", ", mons.Select(x => x.EdidName + "/" + x.PnpId).ToArray()) + ")");
        if (m != null)
        {
            VcpValue before, after;
            var sw = Stopwatch.StartNew();
            Check(m.TryGet(Vcp.Brightness, out before), "lee el brillo: " + (before != null ? before.Current + "/" + before.Max : "-") + " (" + sw.ElapsedMilliseconds + " ms)");
            if (before != null)
            {
                sw.Restart();
                Check(m.TrySet(Vcp.Brightness, before.Current), "reescribe el mismo brillo (" + sw.ElapsedMilliseconds + " ms)");
                Thread.Sleep(100);
                Check(m.TryGet(Vcp.Brightness, out after) && after.Current == before.Current, "sigue igual tras escribir: " + (after != null ? after.Current.ToString() : "-"));
            }
        }
        foreach (var x in mons) x.Close();
    }
}
