using System.Collections.Generic;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Brightness, contrast, sharpness, color preset and RGB gains.
    class ImageTab : MonitorTab
    {
        readonly List<SliderRow> sliders = new List<SliderRow>();
        readonly ChoiceRow presetRow;
        readonly LinkLabel colorHint;
        readonly Button resetLumButton, resetColorButton;

        static readonly byte[] DefaultPresets = { 0x01, 0x04, 0x05, 0x06, 0x08, 0x0B };

        public ImageTab(TrayApp app) : base(app, Strings.TabImage)
        {
            var t = Lay.Table(3);
            sliders.Add(Slider(t, Strings.Brightness, Vcp.Brightness));
            sliders.Add(Slider(t, Strings.Contrast, Vcp.Contrast));
            sliders.Add(Slider(t, Strings.Sharpness, Vcp.Sharpness));
            Lay.Separator(t);
            presetRow = new ChoiceRow(t, Strings.ColorPreset, Vcp.ColorPreset);
            presetRow.Changed += delegate(ChoiceRow r, uint v) { if (M != null) Monitors.Set(M, Vcp.ColorPreset, v); };
            sliders.Add(Slider(t, Strings.Red, Vcp.RedGain));
            sliders.Add(Slider(t, Strings.Green, Vcp.GreenGain));
            sliders.Add(Slider(t, Strings.Blue, Vcp.BlueGain));
            colorHint = new LinkLabel { AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left | AnchorStyles.Right, Visible = false, Margin = Dpi.Pad(3, 2, 3, 8) };
            colorHint.Text = Strings.RgbNeedsUser + " " + Strings.SwitchToUser;
            colorHint.LinkArea = new LinkArea(colorHint.Text.Length - Strings.SwitchToUser.Length, Strings.SwitchToUser.Length);
            colorHint.LinkClicked += delegate { if (M != null) Monitors.Set(M, Vcp.ColorPreset, Vcp.PresetUser1); };
            Lay.Span(t, colorHint, 1);
            Lay.Separator(t);
            var buttons = Lay.Flow();
            resetLumButton = Lay.Button(Strings.ResetLuminance, delegate { ConfirmReset(Vcp.ResetLuminance, Strings.ResetLuminanceWhat); });
            resetColorButton = Lay.Button(Strings.ResetColor, delegate { ConfirmReset(Vcp.ResetColor, Strings.ResetColorWhat); });
            buttons.Controls.Add(resetLumButton);
            buttons.Controls.Add(resetColorButton);
            Lay.Span(t, buttons);
            Lay.Filler(t);
            Controls.Add(t);
        }

        public override void Bind(DdcMonitor m)
        {
            foreach (var s in sliders)
            {
                s.Visible = m == null || m.Supports(s.Code);
                s.Enabled = false;
            }
            var presets = m == null ? null : m.OptionsOf(Vcp.ColorPreset);
            presetRow.Visible = m == null || m.Supports(Vcp.ColorPreset);
            presetRow.SetOptions(presets != null && presets.Count > 0 ? presets : (IEnumerable<byte>)DefaultPresets);
            presetRow.Enabled = false;
            colorHint.Visible = false;
            bool responsive = m != null && m.Responsive;
            resetLumButton.Visible = m == null || m.Supports(Vcp.ResetLuminance);
            resetColorButton.Visible = m == null || m.Supports(Vcp.ResetColor);
            resetLumButton.Enabled = resetColorButton.Enabled = responsive;
        }

        public override void OnValue(DdcMonitor m, byte code)
        {
            VcpValue v;
            bool have = m.Values.TryGetValue(code, out v);
            foreach (var s in sliders)
                if (s.Code == code)
                {
                    if (have) s.SetValue(v);
                    else s.Enabled = false;
                }
            if (code == Vcp.ColorPreset && have)
            {
                presetRow.SetValue(v.Current);
                var presets = m.OptionsOf(Vcp.ColorPreset);
                colorHint.Visible = v.Current != Vcp.PresetUser1 && (presets == null || presets.Contains(Vcp.PresetUser1));
            }
        }
    }
}
