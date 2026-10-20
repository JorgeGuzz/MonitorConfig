using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorConfig
{
    static class Theme
    {
        public static bool Dark
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        var v = k == null ? null : k.GetValue("SystemUsesLightTheme");
                        return v is int && (int)v == 0;
                    }
                }
                catch (Exception) { return false; }
            }
        }

        public static readonly Color Accent = Color.FromArgb(0, 120, 215);
    }

    // Small non-activating indicator shown at the bottom of the monitor when a profile is applied.
    class OsdForm : Form
    {
        readonly Timer timer = new Timer { Interval = 1500 };
        string title = "", subtitle = "";
        int percent = -1;

        public OsdForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(32, 32, 32);
            ForeColor = Color.White;
            Font = Fonts.OsdTitle;
            timer.Tick += delegate { timer.Stop(); Hide(); };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOPMOST;
                return cp;
            }
        }

        public void ShowValue(DdcMonitor m, string title, int percent)
        {
            this.title = title;
            this.percent = percent;
            subtitle = m.Model;
            var screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == m.DeviceName) ?? Screen.PrimaryScreen;
            var size = new Size(Dpi.S(300), Dpi.S(percent >= 0 ? 78 : 56));
            var area = screen.WorkingArea;
            Bounds = new Rectangle(area.Left + (area.Width - size.Width) / 2, area.Bottom - size.Height - Dpi.S(70), size.Width, size.Height);
            Invalidate();
            if (!Visible) Show();
            timer.Stop();
            timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            int pad = Dpi.S(14);
            using (var pen = new Pen(Color.FromArgb(70, 70, 70)))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            {
                // GDI text (TextRenderer) falls back to other fonts for Chinese, Japanese or Cyrillic; GDI+ does not.
                TextRenderer.DrawText(g, title, Font, new Point(pad, Dpi.S(8)), Color.White, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, subtitle, Fonts.OsdSmall, new Point(pad, Dpi.S(31)), Color.FromArgb(170, 170, 170), TextFormatFlags.NoPrefix);
                if (percent >= 0)
                {
                    string text = percent + " %";
                    var sz = TextRenderer.MeasureText(text, Font);
                    TextRenderer.DrawText(g, text, Font, new Point(Width - pad - sz.Width, Dpi.S(8)), Color.White, TextFormatFlags.NoPrefix);
                    var track = new Rectangle(pad, Height - Dpi.S(20), Width - 2 * pad, Dpi.S(6));
                    using (var back = new SolidBrush(Color.FromArgb(80, 80, 80)))
                        g.FillRectangle(back, track);
                    using (var fill = new SolidBrush(Theme.Accent))
                        g.FillRectangle(fill, track.X, track.Y, (int)(track.Width * Math.Max(0, Math.Min(100, percent)) / 100.0), track.Height);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) timer.Dispose();
            base.Dispose(disposing);
        }
    }
}
