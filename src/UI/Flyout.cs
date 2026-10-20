using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Quick panel opened with a left click on the tray icon: brightness, volume and input of every monitor + profiles.
    class Flyout : Form
    {
        class Row
        {
            public DdcMonitor M;
            public TrackBar Bright, Volume;
            public Label BrightValue, VolumeValue;
            public Button Mute;
            public readonly Dictionary<byte, Button> Inputs = new Dictionary<byte, Button>();
            public readonly Dictionary<byte, Label> InputNames = new Dictionary<byte, Label>();
        }

        readonly TrayApp app;
        readonly MonitorService monitors;
        readonly TableLayoutPanel table = new TableLayoutPanel { AutoSize = true, ColumnCount = 4, Dock = DockStyle.Fill };
        readonly List<Row> rows = new List<Row>();
        bool updating;
        Color back, fore, dim;
        string builtFor;
        public DateTime HiddenAt = DateTime.MinValue;

        public Flyout(TrayApp app)
        {
            this.app = app;
            monitors = app.Monitors;
            // Rebuilt when monitors or profiles change, so sizes are scaled by hand (Dpi.S) instead of by WinForms.
            AutoScaleMode = AutoScaleMode.None;
            Font = Fonts.Ui;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = Dpi.Pad(14, 8, 14, 10);
            for (int i = 0; i < 4; i++) table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            Controls.Add(table);
            monitors.ValueChanged += OnValueChanged;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_TOOLWINDOW;
                cp.ClassStyle |= 0x20000;   // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void Dispose(bool disposing)
        {
            monitors.ValueChanged -= OnValueChanged;
            base.Dispose(disposing);
        }

        // ---------------------------------------------------------------- build

        void Build()
        {
            AutoSize = true;
            bool dark = Theme.Dark;
            back = dark ? Color.FromArgb(40, 40, 40) : Color.FromArgb(245, 245, 245);
            fore = dark ? Color.White : Color.Black;
            dim = dark ? Color.FromArgb(160, 160, 160) : Color.FromArgb(100, 100, 100);
            BackColor = back;
            ForeColor = fore;

            Lay.SuspendTree(this);
            foreach (Control c in table.Controls.Cast<Control>().ToList()) c.Dispose();
            table.RowStyles.Clear();
            table.RowCount = 0;
            rows.Clear();

            var responsive = monitors.All.Where(m => m.Responsive).ToList();
            if (responsive.Count == 0)
                Wide(new Label { Text = monitors.Detecting ? Strings.Searching : Strings.NoMonitorAnswers, AutoSize = true, ForeColor = dim, Margin = Dpi.Pad(3, 6, 3, 6) });
            foreach (var m in responsive)
            {
                if (rows.Count > 0) Wide(new Label { AutoSize = false, Height = 1, BackColor = Color.FromArgb(dark ? 70 : 210, dark ? 70 : 210, dark ? 70 : 210), Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Dpi.Pad(3, 8, 3, 4) });
                var r = new Row { M = m };
                rows.Add(r);
                Wide(new Label { Text = m.Model + (m == monitors.Active && responsive.Count > 1 ? "  ●" : ""), AutoSize = true, UseMnemonic = false, Font = Fonts.Bold, Margin = Dpi.Pad(3, 6, 3, 2) });
                if (m.Supports(Vcp.Brightness)) r.Bright = Slider(r, Strings.Brightness, Vcp.Brightness, out r.BrightValue);
                if (m.Supports(Vcp.Volume))
                {
                    r.Volume = Slider(r, Strings.Volume, Vcp.Volume, out r.VolumeValue);
                    if (m.Supports(Vcp.Mute))
                    {
                        r.Mute = FlatButton(Strings.MuteButton);
                        r.Mute.Anchor = AnchorStyles.Left;
                        r.Mute.Click += delegate { ToggleMute(r.M); };
                        table.Controls.Add(r.Mute, 3, table.RowCount - 1);
                    }
                }
                if (m.Supports(Vcp.Input)) AddInputs(r);
                UpdateRow(r);
            }

            var profiles = app.Settings.Profiles.Where(p => app.Profiles.MonitorFor(p) != null).ToList();
            if (profiles.Count > 0)
            {
                Wide(new Label { Text = Strings.Profiles, AutoSize = true, Font = Fonts.Bold, Margin = Dpi.Pad(3, 12, 3, 2) });
                var flow = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(Dpi.S(380), 0), Margin = Dpi.Pad(0) };
                flow.SuspendLayout();
                foreach (var p in profiles)
                {
                    var profile = p;
                    var b = FlatButton(p.Name);
                    b.Click += delegate { app.Profiles.Apply(profile); };
                    flow.Controls.Add(b);
                }
                Wide(flow);
            }

            var bottom = new FlowLayoutPanel { AutoSize = true, Margin = Dpi.Pad(0, 12, 0, 0) };
            bottom.SuspendLayout();
            var open = FlatButton(Strings.OpenFullPanel);
            open.Click += delegate { Hide(); app.ShowMain(); };
            bottom.Controls.Add(open);
            Wide(bottom);
            Lay.ResumeTree(this);
            PerformLayout();
            // Fixed size from now on: showing an AutoSize form re-measures everything (~25 % of the open time).
            AutoSize = false;
            Size = PreferredSize;
        }

        TrackBar Slider(Row r, string text, byte code, out Label value)
        {
            int row = Lay.Row(table);
            var label = new Label { Text = text, AutoSize = true, ForeColor = dim, Anchor = AnchorStyles.Left, Margin = Dpi.Pad(3, 0, 10, 0) };
            var bar = new TrackBar { Minimum = 0, Maximum = 100, TickStyle = TickStyle.None, AutoSize = false, Width = Dpi.S(210), Height = Dpi.S(28), BackColor = back, SmallChange = 1, LargeChange = 10, Enabled = false, Anchor = AnchorStyles.Left, Margin = Dpi.Pad(0, 2, 0, 2) };
            var val = new Label { Text = "…", AutoSize = true, ForeColor = dim, Anchor = AnchorStyles.Left, Margin = Dpi.Pad(4, 0, 6, 0) };
            val.MinimumSize = new Size(TextRenderer.MeasureText("100 %", Font).Width + Dpi.S(2), 0);   // the fixed size never clips
            table.Controls.Add(label, 0, row);
            table.Controls.Add(bar, 1, row);
            table.Controls.Add(val, 2, row);
            var mon = r.M;
            bar.ValueChanged += delegate
            {
                if (updating) return;
                monitors.Set(mon, code, (uint)bar.Value);
                val.Text = Percent(mon, code, bar.Value) + " %";
            };
            value = val;
            return bar;
        }

        void AddInputs(Row r)
        {
            int row = Lay.Row(table);
            var label = new Label { Text = Strings.Input, AutoSize = true, ForeColor = dim, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = Dpi.Pad(3, 8, 10, 0) };
            var flow = new FlowLayoutPanel { AutoSize = true, MaximumSize = new Size(Dpi.S(380), 0), Margin = Dpi.Pad(0, 2, 0, 0) };
            flow.SuspendLayout();
            foreach (var o in r.M.InputOptions)
            {
                byte code = o;
                var cell = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Dpi.Pad(0, 0, 4, 0) };
                cell.SuspendLayout();
                var b = FlatButton(Vcp.ValueName(Vcp.Input, o));
                b.MinimumSize = new Size(Dpi.S(88), 0);
                b.Margin = Dpi.Pad(0, 3, 0, 0);
                var mon = r.M;
                b.Click += delegate { Hide(); app.ChangeInput(mon, code, null); };
                b.MouseUp += delegate(object sender, MouseEventArgs e)   // right click: name this monitor's inputs
                {
                    if (e.Button != MouseButtons.Right) return;
                    Hide();
                    app.EditInputNames(null, mon);
                };
                var name = new Label { AutoSize = true, UseMnemonic = false, Font = Fonts.Small, ForeColor = dim, Margin = Dpi.Pad(1, 1, 0, 0) };
                cell.Controls.Add(b);
                cell.Controls.Add(name);
                flow.Controls.Add(cell);
                r.Inputs[o] = b;
                r.InputNames[o] = name;
            }
            table.Controls.Add(label, 0, row);
            table.Controls.Add(flow, 1, row);
            table.SetColumnSpan(flow, 3);
        }

        Button FlatButton(string text)
        {
            var b = new Button { Text = text, AutoSize = true, UseMnemonic = false, FlatStyle = FlatStyle.Flat, ForeColor = fore, BackColor = back, Margin = Dpi.Pad(3), Padding = Dpi.Pad(4, 1, 4, 1) };
            b.FlatAppearance.BorderColor = dim;
            return b;
        }

        void Highlight(Button b, bool on)
        {
            b.BackColor = on ? Theme.Accent : back;
            b.ForeColor = on ? Color.White : fore;
            b.FlatAppearance.BorderColor = on ? Theme.Accent : dim;
        }

        void Wide(Control c)
        {
            int row = Lay.Row(table);
            table.Controls.Add(c, 0, row);
            table.SetColumnSpan(c, 4);
        }

        // ---------------------------------------------------------------- values

        static int Percent(DdcMonitor m, byte code, int value)
        {
            VcpValue v;
            return Vcp.Percent((uint)value, m.Values.TryGetValue(code, out v) ? v.Max : 0);
        }

        void SetBar(DdcMonitor m, byte code, TrackBar bar, Label label)
        {
            VcpValue v;
            if (bar == null || !m.Values.TryGetValue(code, out v)) return;
            updating = true;
            bar.Maximum = (int)Math.Max(1, Math.Min(v.Max == 0 ? 100 : v.Max, 65535));
            bar.Value = (int)Math.Min(v.Current, (uint)bar.Maximum);
            bar.Enabled = true;
            updating = false;
            label.Text = Percent(m, code, bar.Value) + " %";
        }

        void UpdateRow(Row r)
        {
            var m = r.M;
            SetBar(m, Vcp.Brightness, r.Bright, r.BrightValue);
            SetBar(m, Vcp.Volume, r.Volume, r.VolumeValue);
            VcpValue v;
            if (r.Mute != null) Highlight(r.Mute, m.Values.TryGetValue(Vcp.Mute, out v) && v.Current == Vcp.MuteOn);
            var shown = m.CurrentInput();
            foreach (var kv in r.Inputs) Highlight(kv.Value, shown == kv.Key);
            foreach (var kv in r.InputNames) kv.Value.Text = app.Labels.Label(m, kv.Key);
        }

        void ToggleMute(DdcMonitor m)
        {
            VcpValue v;
            bool muted = m.Values.TryGetValue(Vcp.Mute, out v) && v.Current == Vcp.MuteOn;
            monitors.Set(m, Vcp.Mute, muted ? Vcp.MuteOff : Vcp.MuteOn);
        }

        void OnValueChanged(DdcMonitor m, byte code)
        {
            if (!Visible) return;
            var r = rows.FirstOrDefault(x => x.M == m);
            if (r != null) UpdateRow(r);
        }

        // ---------------------------------------------------------------- show / hide

        // What the layout depends on: rebuilding on every open cost ~300 ms of CPU.
        string LayoutKey()
        {
            var sb = new StringBuilder(Theme.Dark ? "d" : "l").Append(Lang.Current).Append(monitors.Detecting);
            foreach (var m in monitors.All.Where(x => x.Responsive))
            {
                sb.Append('|').Append(m.Id).Append(m == monitors.Active ? "*" : "");
                foreach (var c in MonitorService.QuickCodes) sb.Append(m.Supports(c) ? '1' : '0');
                foreach (var o in m.InputOptions) sb.Append(o.ToString("X2")).Append(app.Labels.Label(m, o));
            }
            foreach (var p in app.Settings.Profiles) sb.Append('|').Append(p.Name).Append(app.Profiles.MonitorFor(p) != null);
            return sb.ToString();
        }

        public void ShowNearTray()
        {
            string layout = LayoutKey();
            if (layout != builtFor) { Build(); builtFor = layout; }
            else foreach (var r in rows) UpdateRow(r);
            var cursor = Cursor.Position;
            var area = Screen.FromPoint(cursor).WorkingArea;
            var size = Size;
            int margin = Dpi.S(10);
            int x = Math.Max(area.Left + margin, Math.Min(cursor.X - size.Width / 2, area.Right - size.Width - margin));
            int y = cursor.Y >= area.Bottom ? area.Bottom - size.Height - margin
                : cursor.Y <= area.Top ? area.Top + margin
                : Math.Max(area.Top + margin, Math.Min(cursor.Y - size.Height / 2, area.Bottom - size.Height - margin));
            if (cursor.X >= area.Right) x = area.Right - size.Width - margin;
            if (cursor.X <= area.Left) x = area.Left + margin;
            Location = new Point(x, y);
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            foreach (var r in rows) monitors.RefreshQuick(r.M);
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HiddenAt = DateTime.Now;
            Hide();
            app.ScheduleTrim();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(dim))
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }
    }
}
