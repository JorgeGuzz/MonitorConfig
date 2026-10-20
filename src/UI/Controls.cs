using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Fonts shared by every window: created once, never disposed (they live as long as the app).
    static class Fonts
    {
        public static readonly Font Ui = SystemFonts.MessageBoxFont;
        public static readonly Font Bold = new Font(Ui, FontStyle.Bold);
        public static readonly Font Small = new Font(Ui.FontFamily, Ui.SizeInPoints - 1f);
        public static readonly Font Mono = new Font(FontFamily.GenericMonospace, 8.5f);
        public static readonly Font OsdTitle = new Font("Segoe UI", 11f);
        public static readonly Font OsdSmall = new Font("Segoe UI", 8.5f);
    }

    static class Dpi
    {
        static float scale = -1;

        // Pixels at 96 DPI -> pixels at the system DPI (for sizes WinForms does not auto-scale).
        public static int S(int px)
        {
            if (scale < 0)
                using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            return (int)Math.Round(px * scale);
        }

        public static Padding Pad(int all) { return new Padding(S(all)); }
        public static Padding Pad(int left, int top, int right, int bottom) { return new Padding(S(left), S(top), S(right), S(bottom)); }
        public static Size Size(int width, int height) { return new Size(S(width), S(height)); }
    }

    // TableLayoutPanel helpers: column 0 = label, 1 = control (stretches), 2 = numeric box.
    // Panels are created with layout suspended (each control added to an AutoSize panel re-lays it out:
    // building the window took ~600 ms); the owner calls ResumeTree once everything is in place.
    static class Lay
    {
        public static void SuspendTree(Control root)
        {
            root.SuspendLayout();
            foreach (Control c in root.Controls) SuspendTree(c);
        }

        public static void ResumeTree(Control root)
        {
            foreach (Control c in root.Controls) ResumeTree(c);
            root.ResumeLayout(false);
        }

        public static TableLayoutPanel Table(int columns)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns, Padding = Dpi.Pad(10), AutoScroll = true };
            t.SuspendLayout();
            for (int i = 0; i < columns; i++)
                t.ColumnStyles.Add(i == 1 || columns == 1 ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.AutoSize));
            return t;
        }

        public static int Row(TableLayoutPanel t)
        {
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return t.RowCount++;
        }

        public static void Filler(TableLayoutPanel t)
        {
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowCount++;
        }

        public static Label Label(string text)
        {
            return new Label { Text = text, AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = Dpi.Pad(3, 6, 14, 6) };
        }

        // Wrapping text that spans the row.
        public static Label Note(string text)
        {
            return new Label { Text = text, AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left | AnchorStyles.Right, ForeColor = SystemColors.GrayText, Margin = Dpi.Pad(3, 2, 3, 8) };
        }

        public static void Span(TableLayoutPanel t, Control c, int column = 0)
        {
            int row = Row(t);
            t.Controls.Add(c, column, row);
            t.SetColumnSpan(c, t.ColumnCount - column);
        }

        public static void Pair(TableLayoutPanel t, string label, Control c, out Label labelControl)
        {
            int row = Row(t);
            labelControl = Label(label);
            t.Controls.Add(labelControl, 0, row);
            t.Controls.Add(c, 1, row);
            if (t.ColumnCount > 2) t.SetColumnSpan(c, t.ColumnCount - 1);
        }

        public static void Separator(TableLayoutPanel t)
        {
            var line = new Label { BorderStyle = BorderStyle.Fixed3D, Height = 2, AutoSize = false, Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = Dpi.Pad(3, 8, 3, 8) };
            Span(t, line);
        }

        public static FlowLayoutPanel Flow()
        {
            var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Anchor = AnchorStyles.Left, Margin = Dpi.Pad(0) };
            f.SuspendLayout();
            return f;
        }

        public static Button Button(string text, EventHandler click)
        {
            var b = new Button { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = Dpi.Pad(6, 2, 6, 2) };
            b.Click += click;
            return b;
        }
    }

    class SliderRow
    {
        public readonly byte Code;
        readonly Label label;
        readonly TrackBar bar;
        readonly NumericUpDown num;
        bool updating;
        public event Action<SliderRow, uint> Changed;

        public SliderRow(TableLayoutPanel t, string text, byte code)
        {
            Code = code;
            label = Lay.Label(text);
            bar = new TrackBar { Minimum = 0, Maximum = 100, TickFrequency = 10, SmallChange = 1, LargeChange = 10, TickStyle = TickStyle.BottomRight, Anchor = AnchorStyles.Left | AnchorStyles.Right, BackColor = SystemColors.Window };
            num = new NumericUpDown { Minimum = 0, Maximum = 100, Width = Dpi.S(60), Anchor = AnchorStyles.Left, Margin = Dpi.Pad(6, 3, 3, 3) };
            int row = Lay.Row(t);
            t.Controls.Add(label, 0, row);
            t.Controls.Add(bar, 1, row);
            t.Controls.Add(num, 2, row);
            bar.ValueChanged += delegate
            {
                if (updating) return;
                updating = true; num.Value = bar.Value; updating = false;
                Fire();
            };
            num.ValueChanged += delegate
            {
                if (updating) return;
                updating = true; bar.Value = (int)num.Value; updating = false;
                Fire();
            };
            Enabled = false;
        }

        void Fire()
        {
            var h = Changed;
            if (h != null) h(this, (uint)bar.Value);
        }

        public void SetValue(VcpValue v)
        {
            updating = true;
            int max = (int)Math.Max(1, Math.Min(v.Max == 0 ? 100 : v.Max, 65535));
            bar.Maximum = max;
            num.Maximum = max;
            int cur = (int)Math.Min(v.Current, (uint)max);
            bar.Value = cur;
            num.Value = cur;
            bar.TickFrequency = Math.Max(1, max / 10);
            bar.LargeChange = Math.Max(1, max / 10);
            updating = false;
            Enabled = true;
        }

        public bool Visible { set { label.Visible = bar.Visible = num.Visible = value; } }
        public bool Enabled { set { label.Enabled = bar.Enabled = num.Enabled = value; } }
    }

    class Choice
    {
        public uint Value;
        public string Text;
        public override string ToString() { return Text; }
    }

    class ChoiceRow
    {
        public readonly byte Code;
        readonly Label label;
        public readonly ComboBox Combo;
        bool updating;
        public event Action<ChoiceRow, uint> Changed;

        public ChoiceRow(TableLayoutPanel t, string text, byte code)
        {
            Code = code;
            Combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = Dpi.S(240), MinimumSize = Dpi.Size(240, 0), Anchor = AnchorStyles.Left, MaxDropDownItems = 16 };
            Lay.Pair(t, text, Combo, out label);
            Combo.SelectedIndexChanged += delegate
            {
                var c = Combo.SelectedItem as Choice;
                var h = Changed;
                if (!updating && c != null && h != null) h(this, c.Value);
            };
            Enabled = false;
        }

        public void SetOptions(IEnumerable<byte> values)
        {
            updating = true;
            Combo.Items.Clear();
            foreach (var v in values) Combo.Items.Add(new Choice { Value = v, Text = Vcp.ValueName(Code, v) });
            updating = false;
        }

        public void SetValue(uint value)
        {
            updating = true;
            int index = -1;
            for (int i = 0; i < Combo.Items.Count; i++)
                if (((Choice)Combo.Items[i]).Value == value) index = i;
            if (index < 0)
            {
                Combo.Items.Add(new Choice { Value = value, Text = Vcp.ValueName(Code, value) });
                index = Combo.Items.Count - 1;
            }
            Combo.SelectedIndex = index;
            updating = false;
            Enabled = true;
        }

        public bool Visible { set { label.Visible = Combo.Visible = value; } }
        public bool Enabled { set { label.Enabled = Combo.Enabled = value; } }
    }
}
