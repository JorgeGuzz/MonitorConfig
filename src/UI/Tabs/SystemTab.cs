using System.Collections.Generic;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Menu language, OSD lock, power off and factory reset.
    class SystemTab : MonitorTab
    {
        readonly ChoiceRow languageRow;
        readonly CheckBox osdBox;
        readonly Label powerLabel, powerHint, resetLabel, resetHint;
        readonly Button powerButton, resetAllButton;

        static readonly byte[] DefaultLanguages = { 0x02, 0x0A };

        public SystemTab(TrayApp app) : base(app, Strings.TabSystem)
        {
            var t = Lay.Table(3);
            languageRow = new ChoiceRow(t, Strings.MenuLanguage, Vcp.OsdLanguage);
            languageRow.Changed += delegate(ChoiceRow r, uint v) { if (M != null) Monitors.Set(M, Vcp.OsdLanguage, v); };
            osdBox = new CheckBox { Text = Strings.OsdEnabled, AutoSize = true, Anchor = AnchorStyles.Left, Enabled = false };
            osdBox.CheckedChanged += delegate { OnOsdToggled(); };
            Lay.Span(t, osdBox, 1);
            Lay.Separator(t);
            powerButton = Lay.Button(Strings.PowerOff, delegate { PowerOff(); });
            powerButton.Anchor = AnchorStyles.Left;
            Lay.Pair(t, Strings.Power, powerButton, out powerLabel);
            powerHint = Lay.Note(Strings.PowerOffHint);
            Lay.Span(t, powerHint, 1);
            Lay.Separator(t);
            resetAllButton = Lay.Button(Strings.FactoryReset, delegate { ConfirmReset(Vcp.FactoryReset, Strings.FactoryResetWhat); });
            resetAllButton.Anchor = AnchorStyles.Left;
            Lay.Pair(t, Strings.Factory, resetAllButton, out resetLabel);
            resetHint = Lay.Note(Strings.FactoryResetHint);
            Lay.Span(t, resetHint, 1);
            Lay.Filler(t);
            Controls.Add(t);
        }

        public override void Bind(DdcMonitor m)
        {
            bool responsive = m != null && m.Responsive;
            var languages = m == null ? null : m.OptionsOf(Vcp.OsdLanguage);
            languageRow.Visible = m == null || m.Supports(Vcp.OsdLanguage);
            languageRow.SetOptions(languages != null && languages.Count > 0 ? languages : (IEnumerable<byte>)DefaultLanguages);
            languageRow.Enabled = false;
            osdBox.Visible = m == null || m.Supports(Vcp.Osd);
            osdBox.Enabled = false;
            powerLabel.Visible = powerButton.Visible = powerHint.Visible = m == null || m.Supports(Vcp.Power);
            powerButton.Enabled = responsive;
            resetLabel.Visible = resetAllButton.Visible = resetHint.Visible = m == null || m.Supports(Vcp.FactoryReset);
            resetAllButton.Enabled = responsive;
        }

        public override void OnValue(DdcMonitor m, byte code)
        {
            VcpValue v;
            if (!m.Values.TryGetValue(code, out v)) return;
            Updating = true;
            try
            {
                if (code == Vcp.OsdLanguage) languageRow.SetValue(v.Current);
                else if (code == Vcp.Osd)
                {
                    osdBox.Checked = v.Current != Vcp.OsdOff;
                    osdBox.Enabled = true;
                }
            }
            finally { Updating = false; }
        }

        void PowerOff()
        {
            var m = M;
            if (m == null) return;
            var opts = m.OptionsOf(Vcp.Power);
            byte value = opts != null && !opts.Contains(Vcp.PowerOffButton) && opts.Contains(Vcp.PowerOffDpm) ? Vcp.PowerOffDpm : Vcp.PowerOffButton;
            if (Confirm(Strings.PowerOffQuestion(m.Model), Strings.PowerOff, MessageBoxIcon.Question)) Monitors.Set(m, Vcp.Power, value);
        }

        void OnOsdToggled()
        {
            if (Updating || M == null) return;
            if (!osdBox.Checked && !Confirm(Strings.OsdOffQuestion, Strings.OsdTitle, MessageBoxIcon.Warning))
            {
                Updating = true;
                osdBox.Checked = true;
                Updating = false;
                return;
            }
            Monitors.Set(M, Vcp.Osd, osdBox.Checked ? Vcp.OsdOn : Vcp.OsdOff);
        }
    }
}
