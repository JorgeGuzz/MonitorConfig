using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Input buttons (with the name of what is plugged in underneath), volume and mute.
    class InputTab : MonitorTab
    {
        readonly Label inputLabel, inputHint;
        readonly FlowLayoutPanel inputPanel;
        readonly LinkLabel namesLink;
        readonly SliderRow volume;
        readonly CheckBox muteBox;
        readonly List<RadioButton> inputButtons = new List<RadioButton>();
        readonly Dictionary<byte, Label> nameLabels = new Dictionary<byte, Label>();

        public InputTab(TrayApp app) : base(app, Strings.TabInput)
        {
            var t = Lay.Table(3);
            inputPanel = Lay.Flow();
            Lay.Pair(t, Strings.Input, inputPanel, out inputLabel);
            inputHint = Lay.Note("");
            Lay.Span(t, inputHint, 1);
            namesLink = new LinkLabel { Text = Strings.NameInputs, AutoSize = true, Anchor = AnchorStyles.Left, Margin = Dpi.Pad(3, 0, 3, 8) };
            namesLink.LinkClicked += delegate { if (M != null && App.EditInputNames(FindForm(), M)) NamesChanged(); };
            Lay.Span(t, namesLink, 1);
            Lay.Separator(t);
            volume = Slider(t, Strings.Volume, Vcp.Volume);
            muteBox = new CheckBox { Text = Strings.MuteSpeakers, AutoSize = true, Anchor = AnchorStyles.Left, Enabled = false };
            muteBox.CheckedChanged += delegate { if (!Updating && M != null) Monitors.Set(M, Vcp.Mute, muteBox.Checked ? Vcp.MuteOn : Vcp.MuteOff); };
            Lay.Span(t, muteBox, 1);
            Lay.Filler(t);
            Controls.Add(t);
        }

        public override void Bind(DdcMonitor m)
        {
            foreach (Control c in inputPanel.Controls.Cast<Control>().ToList()) c.Dispose();
            inputButtons.Clear();
            nameLabels.Clear();
            bool hasInput = m == null || m.Supports(Vcp.Input);
            inputLabel.Visible = inputPanel.Visible = inputHint.Visible = namesLink.Visible = hasInput;
            inputHint.Text = "";
            foreach (var value in m != null ? m.InputOptions : DdcMonitor.DefaultInputs)
            {
                var cell = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Dpi.Pad(0, 0, 6, 0) };
                var rb = new RadioButton
                {
                    Appearance = Appearance.Button, AutoCheck = false, AutoSize = true, Text = Vcp.ValueName(Vcp.Input, value),
                    TextAlign = ContentAlignment.MiddleCenter, Tag = value, Padding = Dpi.Pad(10, 2, 10, 2), Enabled = false,
                    MinimumSize = Dpi.Size(100, 0), Margin = Dpi.Pad(0, 3, 0, 0),
                };
                rb.Click += delegate(object sender, EventArgs e) { if (M != null) App.ChangeInput(M, (byte)((Control)sender).Tag, FindForm()); };
                var name = new Label { AutoSize = true, UseMnemonic = false, ForeColor = SystemColors.GrayText, Margin = Dpi.Pad(2, 2, 0, 0) };
                cell.Controls.Add(rb);
                cell.Controls.Add(name);
                inputPanel.Controls.Add(cell);
                inputButtons.Add(rb);
                nameLabels[value] = name;
            }
            UpdateNames();
            volume.Visible = m == null || m.Supports(Vcp.Volume);
            volume.Enabled = false;
            muteBox.Visible = m == null || m.Supports(Vcp.Mute);
            muteBox.Enabled = false;
        }

        public override void OnValue(DdcMonitor m, byte code)
        {
            VcpValue v;
            bool have = m.Values.TryGetValue(code, out v);
            if (code == Vcp.Volume)
            {
                if (have) volume.SetValue(v);
                else volume.Enabled = false;
            }
            if (!have) return;
            Updating = true;
            try
            {
                if (code == Vcp.Input)
                {
                    foreach (var rb in inputButtons)
                    {
                        rb.Enabled = true;
                        rb.Checked = (byte)rb.Tag == v.Current;
                    }
                    UpdateNames();
                    var shown = m.CurrentInput();
                    inputHint.Text = shown.HasValue ? Strings.ShowingInput(App.Labels.Text(m, shown.Value)) : Strings.UnknownInput(v.Current);
                }
                else if (code == Vcp.Mute)
                {
                    muteBox.Checked = v.Current == Vcp.MuteOn;
                    muteBox.Enabled = true;
                }
            }
            finally { Updating = false; }
        }

        // The names were edited (here or from the tray panel).
        public void NamesChanged()
        {
            UpdateNames();
            if (M != null) OnValue(M, Vcp.Input);
        }

        void UpdateNames()
        {
            foreach (var kv in nameLabels) kv.Value.Text = M == null ? "" : App.Labels.Label(M, kv.Key);
        }
    }
}
