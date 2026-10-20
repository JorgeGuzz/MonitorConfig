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

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

        // What DWM composes (dark title bar, themed scrollbars), after a full repaint. Windows it does not compose
        // (the dialogs, placed off-screen) are drawn by WinForms instead.
        public static void Capture(Control c, string path)
        {
            using (var bmp = new Bitmap(c.Width, c.Height))
            {
                var form = c as Form;   // the title bar is what needs DWM; borderless panels draw fine by themselves
                bool onScreen = form != null && form.FormBorderStyle != FormBorderStyle.None && c.Visible
                    && Screen.AllScreens.Any(s => s.Bounds.IntersectsWith(c.RectangleToScreen(c.ClientRectangle)));
                bool ok = false;
                if (onScreen)
                {
                    c.Refresh();
                    using (var g = Graphics.FromImage(bmp))
                    {
                        IntPtr hdc = g.GetHdc();
                        ok = PrintWindow(c.Handle, hdc, 2);   // PW_RENDERFULLCONTENT
                        g.ReleaseHdc(hdc);
                    }
                }
                if (!ok) c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
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
                        var p = new Profile { Name = "Night", Monitor = active.Key };
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
                        osd.ShowValue(active, Strings.ProfileIndicator("Night"), 45);
                        Shot(osd, Path.Combine(dir, "osd.png"));
                        osd.Hide();
                    }
                    File.WriteAllText(Path.Combine(dir, "state.txt"), DumpState(), Encoding.UTF8);   // builds the tray menu
                    menu.CreateControl();   // drawn without opening it
                    menu.Size = menu.GetPreferredSize(Size.Empty);
                    menu.PerformLayout();
                    DevTools.Capture(menu, Path.Combine(dir, "traymenu.png"));
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
            DevTools.Capture(f, path);
        }

        static void DumpMenu(StringBuilder sb, ToolStripItemCollection items, string indent)
        {
            foreach (ToolStripItem item in items)
            {
                var mi = item as ToolStripMenuItem;
                sb.AppendLine(indent + (mi == null ? "----" : item.Text + (mi.Checked ? "  [x]" : "") + (item.Enabled ? "" : "  (disabled)")));
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
                DevTools.Capture(this, Path.Combine(dir, "tab" + i + ".png"));
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
