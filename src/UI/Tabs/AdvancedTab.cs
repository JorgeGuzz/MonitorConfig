using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Every VCP code the monitor reports, with direct read and write.
    class AdvancedTab : MonitorTab
    {
        readonly ListView vcpList;
        readonly Dictionary<byte, ListViewItem> rows = new Dictionary<byte, ListViewItem>();
        readonly TextBox codeBox;
        readonly NumericUpDown valueBox;

        public AdvancedTab(TrayApp app) : base(app, Strings.TabAdvanced)
        {
            var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = Dpi.Pad(10) };
            t.SuspendLayout();
            t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var warning = Lay.Note(Strings.AdvancedWarning);
            warning.ForeColor = SystemColors.ControlText;
            t.Controls.Add(warning, 0, 0);
            vcpList = new ListView { View = View.Details, FullRowSelect = true, HideSelection = false, MultiSelect = false, Dock = DockStyle.Fill };
            vcpList.Columns.Add(Strings.CodeColumn, Dpi.S(56));
            vcpList.Columns.Add(Strings.NameColumn, Dpi.S(230));
            vcpList.Columns.Add(Strings.ValueColumn, Dpi.S(150));
            vcpList.Columns.Add(Strings.MaxColumn, Dpi.S(64));
            vcpList.Columns.Add(Strings.OptionsColumn, Dpi.S(170));
            vcpList.SelectedIndexChanged += delegate { OnSelected(); };
            t.Controls.Add(vcpList, 0, 1);
            var bar = Lay.Flow();
            bar.Margin = Dpi.Pad(0, 6, 0, 0);
            bar.Controls.Add(Lay.Button(Strings.ReadAll, delegate { ReadAll(); }));
            bar.Controls.Add(new Label { Text = Strings.CodeHex, AutoSize = true, Margin = Dpi.Pad(16, 3, 3, 3) });
            codeBox = new TextBox { Width = Dpi.S(40), MaxLength = 2, CharacterCasing = CharacterCasing.Upper };
            bar.Controls.Add(codeBox);
            bar.Controls.Add(new Label { Text = Strings.ValueLabel, AutoSize = true, Margin = Dpi.Pad(10, 3, 3, 3) });
            valueBox = new NumericUpDown { Minimum = 0, Maximum = 65535, Width = Dpi.S(76) };
            bar.Controls.Add(valueBox);
            bar.Controls.Add(Lay.Button(Strings.Read, delegate { ReadOne(); }));
            bar.Controls.Add(Lay.Button(Strings.Write, delegate { WriteOne(); }));
            foreach (Control c in bar.Controls) c.Anchor = AnchorStyles.Left;   // vertically centred in the row
            t.Controls.Add(bar, 0, 2);
            Controls.Add(t);
        }

        public override void Bind(DdcMonitor m)
        {
            vcpList.BeginUpdate();
            vcpList.Items.Clear();
            rows.Clear();
            if (m != null && m.Caps != null)
                foreach (var code in m.Caps.Codes) AddRow(m, code);
            vcpList.EndUpdate();
        }

        ListViewItem AddRow(DdcMonitor m, byte code)
        {
            var item = new ListViewItem(code.ToString("X2")) { Tag = code };
            item.SubItems.Add(Vcp.Name(code));
            item.SubItems.Add("");
            item.SubItems.Add("");
            var opts = m.OptionsOf(code);
            item.SubItems.Add(opts == null ? "" : string.Join(" ", opts.Select(o => o.ToString("X2")).ToArray()));
            vcpList.Items.Add(item);
            rows[code] = item;
            return item;
        }

        public override void OnValue(DdcMonitor m, byte code)
        {
            ListViewItem item;
            VcpValue v;
            bool have = m.Values.TryGetValue(code, out v);
            if (!rows.TryGetValue(code, out item))
            {
                if (!have && !m.Failed.Contains(code)) return;
                item = AddRow(m, code);
            }
            item.SubItems[2].Text = have ? Vcp.Format(code, v) : (m.Failed.Contains(code) ? Strings.ReadError : "");
            item.SubItems[3].Text = have ? v.Max.ToString() : "";
        }

        void OnSelected()
        {
            if (vcpList.SelectedItems.Count == 0 || M == null) return;
            byte code = (byte)vcpList.SelectedItems[0].Tag;
            codeBox.Text = code.ToString("X2");
            VcpValue v;
            if (M.Values.TryGetValue(code, out v)) valueBox.Value = Math.Min(v.Current, (uint)valueBox.Maximum);
        }

        void ReadAll()
        {
            if (M == null) return;
            Monitors.Status(Strings.ReadingAll);
            Monitors.ReadAll(M, delegate { Monitors.Status(Strings.ReadAllDone); });
        }

        bool ParseCode(out byte code)
        {
            code = 0;
            int c;
            if (int.TryParse(codeBox.Text.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out c) && c >= 0 && c <= 0xFF)
            {
                code = (byte)c;
                return true;
            }
            MessageBox.Show(FindForm(), Strings.InvalidCodeText, Strings.InvalidCodeTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        void ReadOne()
        {
            byte code;
            if (M == null || !ParseCode(out code)) return;
            Monitors.Read(M, new[] { code }, delegate
            {
                VcpValue v;
                if (M != null && M.Values.TryGetValue(code, out v)) valueBox.Value = Math.Min(v.Current, (uint)valueBox.Maximum);
            }, false);
        }

        void WriteOne()
        {
            byte code;
            if (M == null || !ParseCode(out code)) return;
            uint value = (uint)valueBox.Value;
            if (!Confirm(Strings.WriteVcpQuestion(value, code, Vcp.Name(code)), Strings.WriteVcpTitle, MessageBoxIcon.Warning)) return;
            Monitors.Set(M, code, value);
            Monitors.Read(M, new[] { code }, null, false);
        }
    }
}
