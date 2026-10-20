#if DEVTOOLS
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MonitorConfig
{
    // --bench <file>: scripted measurements (memory, handles, GDI/USER objects, CPU, DDC traffic) for performance work.
    partial class TrayApp
    {
        [DllImport("user32.dll")]
        static extern int GetGuiResources(IntPtr process, int flags);

        class Sample { public double Ws, Priv, Heap, Cpu; public int Handles, Gdi, User, Threads, Ddc; }

        static Sample Take(Process p, bool collect)
        {
            if (collect) { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
            p.Refresh();
            return new Sample
            {
                Ws = p.WorkingSet64 / 1048576.0, Priv = p.PrivateMemorySize64 / 1048576.0, Heap = GC.GetTotalMemory(false) / 1048576.0,
                Cpu = p.TotalProcessorTime.TotalMilliseconds, Handles = p.HandleCount, Gdi = GetGuiResources(p.Handle, 0),
                User = GetGuiResources(p.Handle, 1), Threads = p.Threads.Count, Ddc = Dxva2Port.Transactions,
            };
        }

        Sample last;

        void Row(StringBuilder sb, Process p, string label, bool collect)
        {
            var s = Take(p, collect);
            sb.AppendLine(string.Format("{0,-44}{1,7:0.0}{2,8:0.0}{3,7:0.0}{4,8}{5,6}{6,6}{7,6}{8,9:0}{9,7}",
                label, s.Ws, s.Priv, s.Heap, s.Handles, s.Gdi, s.User, s.Threads,
                last == null ? s.Cpu : s.Cpu - last.Cpu, last == null ? s.Ddc : s.Ddc - last.Ddc));
            last = s;
        }

        static async Task<double> Until(Func<bool> condition, int timeoutMs)
        {
            var sw = Stopwatch.StartNew();
            while (!condition() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(20);
            return sw.Elapsed.TotalMilliseconds;
        }

        public async void RunBench(string file)
        {
            var sb = new StringBuilder();
            var p = Process.GetCurrentProcess();
            try
            {
                sb.AppendLine("MonitorConfig " + Application.ProductVersion + " · .NET " + Environment.Version + " · " + (Environment.Is64BitProcess ? "64" : "32")
                    + " bits · exe " + (new FileInfo(Application.ExecutablePath).Length / 1024) + " KB");
                sb.AppendLine("Arranque hasta la bandeja: " + (DateTime.Now - p.StartTime).TotalMilliseconds.ToString("0") + " ms");
                await Until(() => !Monitors.Detecting && Monitors.All.Count > 0, 20000);
                sb.AppendLine("Arranque hasta detectar monitores: " + (DateTime.Now - p.StartTime).TotalMilliseconds.ToString("0") + " ms");
                await Until(() => Monitors.DdcIdle, 30000);
                sb.AppendLine("Arranque hasta terminar el DDC/CI: " + (DateTime.Now - p.StartTime).TotalMilliseconds.ToString("0") + " ms");
                sb.AppendLine();
                sb.AppendLine(string.Format("{0,-44}{1,7}{2,8}{3,7}{4,8}{5,6}{6,6}{7,6}{8,9}{9,7}", "Escenario", "WS MB", "Priv MB", "GC MB", "Handles", "GDI", "USER", "Hilos", "CPU ms", "DDC"));
                Row(sb, p, "Arranque (en la bandeja)", false);
                Row(sb, p, "  idem tras recolectar basura", true);
                await Task.Delay(20000);
                Row(sb, p, "20 s en reposo", false);

                ShowMain();
                double t = await Until(() => Monitors.DdcIdle, 30000);
                Row(sb, p, "Ventana principal abierta (" + t.ToString("0") + " ms DDC)", false);
                if (main != null) main.HideToTray();
                await Task.Delay(300);
                Row(sb, p, "Ventana cerrada a la bandeja", true);
                await Task.Delay(10000);
                Row(sb, p, "  10 s después (memoria devuelta)", false);
                for (int i = 0; i < 20; i++) { ShowMain(); await Task.Delay(120); if (main != null) main.HideToTray(); await Task.Delay(60); }
                t = await Until(() => Monitors.DdcIdle, 60000);
                Row(sb, p, "20 aperturas de la ventana (" + t.ToString("0") + " ms DDC)", true);

                if (flyout == null || flyout.IsDisposed) flyout = new Flyout(this);
                for (int i = 0; i < 30; i++) { flyout.ShowNearTray(); await Task.Delay(80); flyout.Hide(); await Task.Delay(40); }
                t = await Until(() => Monitors.DdcIdle, 120000);
                Row(sb, p, "30 aperturas del panel (" + t.ToString("0") + " ms DDC)", true);

                for (int i = 0; i < 50; i++) BuildMenu(menu, new CancelEventArgs());
                t = await Until(() => Monitors.DdcIdle, 120000);
                Row(sb, p, "50 aperturas del menú (" + t.ToString("0") + " ms DDC)", true);

                if (Monitors.Active != null)
                {
                    if (osd == null || osd.IsDisposed) osd = new OsdForm();
                    for (int i = 0; i < 30; i++) { osd.ShowValue(Monitors.Active, "Brillo", i * 3); await Task.Delay(30); }
                    osd.Hide();
                    Row(sb, p, "30 indicadores en pantalla", true);
                }

                sb.AppendLine();
                foreach (var what in new[] { "el panel de la bandeja", "la ventana principal" })
                {
                    p.Refresh();
                    double cpu0 = p.TotalProcessorTime.TotalMilliseconds;
                    var sw = Stopwatch.StartNew();
                    for (int i = 0; i < 10; i++)
                    {
                        if (what.StartsWith("el panel")) { flyout.ShowNearTray(); flyout.Update(); flyout.Hide(); }
                        else { ShowMain(); main.Update(); main.HideToTray(); }
                    }
                    double wall = sw.Elapsed.TotalMilliseconds;
                    p.Refresh();
                    sb.AppendLine(string.Format("Abrir {0}: {1:0} ms de reloj y {2:0} ms de CPU (media de 10)", what, wall / 10, (p.TotalProcessorTime.TotalMilliseconds - cpu0) / 10));
                }
                await Until(() => Monitors.DdcIdle, 30000);
                sb.AppendLine();
                await Task.Delay(30000);
                Row(sb, p, "30 s en reposo al final", false);
                Row(sb, p, "  idem tras recolectar basura", true);
            }
            catch (Exception ex) { sb.AppendLine(ex.ToString()); }
            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
            ExitApp();
        }
    }
}
#endif
