#if DEVTOOLS
using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Developer build only (build.cmd -> tools\MonitorConfig.dev.exe):
    //   --ini <file>     use these settings instead of the ones next to the exe (point it at a copy)
    //   --shots <dir>    render every tab, dialog, the tray panel and the indicator to PNG + state.txt, then exit
    //   --bench <file>   resource measurements (see Bench.cs), then exit
    static class DevTools
    {
        public static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static bool RunsHidden(string[] args)
        {
            return args.Contains("--shots") || args.Contains("--bench");
        }

        public static void Start(TrayApp app, string[] args)
        {
            string shots = Arg(args, "--shots"), bench = Arg(args, "--bench");
            if (shots != null) app.RunShots(shots);
            if (bench != null) app.RunBench(bench);
        }
    }

    partial class TrayApp
    {
        public void RunShots(string dir)
        {
            Directory.CreateDirectory(dir);
            ShowMain();
            var timer = new System.Windows.Forms.Timer { Interval = 7000 };
            timer.Tick += delegate
            {
                timer.Stop();
                try
                {
                    main.SaveTabShots(dir);
                    using (var f = new SettingsForm(Settings)) Shot(f, Path.Combine(dir, "settings.png"));
                    var active = Monitors.Active;
                    if (active != null)
                    {
                        var p = new Profile { Name = "Noche", Monitor = active.Key };
                        p.Values[Vcp.Brightness] = 20;
                        p.Values[Vcp.ColorPreset] = 0x04;
                        using (var f = new ProfileForm(active, p, x => null)) Shot(f, Path.Combine(dir, "profile.png"));
                        using (var f = new InputNamesForm(Settings, active)) Shot(f, Path.Combine(dir, "inputnames.png"));
                    }
                    if (flyout == null) flyout = new Flyout(this);
                    flyout.ShowNearTray();
                    Application.DoEvents();
                    Shot(flyout, Path.Combine(dir, "flyout.png"));
                    flyout.Hide();
                    if (active != null)
                    {
                        if (osd == null) osd = new OsdForm();
                        osd.ShowValue(active, "Perfil: Noche", 45);
                        Shot(osd, Path.Combine(dir, "osd.png"));
                        osd.Hide();
                    }
                    File.WriteAllText(Path.Combine(dir, "state.txt"), DumpState(), Encoding.UTF8);
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(dir, "error.txt"), ex.ToString()); }
                ExitApp();
            };
            timer.Start();
        }

        static void Shot(Form f, string path)
        {
            if (!f.Visible) { f.StartPosition = FormStartPosition.Manual; f.Location = new Point(-32000, -32000); f.Show(); }
            Application.DoEvents();
            using (var bmp = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        static void DumpMenu(StringBuilder sb, ToolStripItemCollection items, string indent)
        {
            foreach (ToolStripItem item in items)
            {
                var mi = item as ToolStripMenuItem;
                sb.AppendLine(indent + (mi == null ? "----" : item.Text + (mi.Checked ? "  [x]" : "") + (item.Enabled ? "" : "  (gris)")));
                if (mi != null) DumpMenu(sb, mi.DropDownItems, indent + "    ");
            }
        }

        string DumpState()
        {
            var sb = new StringBuilder();
            BuildMenu(menu, new CancelEventArgs());
            DumpMenu(sb, menu.Items, "  ");
            foreach (var m in Monitors.All)
            {
                sb.AppendLine((m == Monitors.Active ? "* " : "  ") + m.DisplayName + " | key=" + m.Key + " | legacy=" + m.LegacyKey
                    + " | numserial=" + m.EdidNumSerial.ToString("X8") + " | instance=" + m.InstanceId + " | pos=" + m.Left + "," + m.Top
                    + " | serial=" + m.EdidSerial + " | caps=" + (m.Caps == null ? "none" : m.Caps.Codes.Count + " codes, model=" + m.Caps.Model + ", fw=" + m.Caps.Firmware));
                foreach (var kv in m.Values.OrderBy(k => k.Key))
                    sb.AppendLine("    " + kv.Key.ToString("X2") + " " + Vcp.Name(kv.Key) + " = " + Vcp.Format(kv.Key, kv.Value));
                foreach (var f in m.Failed) sb.AppendLine("    " + f.ToString("X2") + " FAILED");
            }
            return sb.ToString();
        }
    }

    partial class MainForm
    {
        public void SaveTabShots(string dir)
        {
            for (int i = 0; i < tabs.TabPages.Count; i++)
            {
                tabs.SelectedIndex = i;
                Application.DoEvents();
                using (var bmp = new Bitmap(Width, Height))
                {
                    DrawToBitmap(bmp, new Rectangle(0, 0, Width, Height));
                    bmp.Save(Path.Combine(dir, "tab" + i + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            tabs.SelectedIndex = 0;
            var list = languageButton.DropDown;   // drawn without opening it
            list.CreateControl();
            list.Size = list.GetPreferredSize(Size.Empty);
            list.PerformLayout();
            using (var bmp = new Bitmap(list.Width, list.Height))
            {
                list.DrawToBitmap(bmp, new Rectangle(0, 0, list.Width, list.Height));
                bmp.Save(Path.Combine(dir, "languages.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}
#endif
