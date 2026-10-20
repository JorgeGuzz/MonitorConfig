using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace MonitorConfig
{
    // Identity, signal and firmware of the selected monitor, plus its raw capabilities string.
    class InfoTab : MonitorTab
    {
        readonly Dictionary<string, Label> info = new Dictionary<string, Label>();
        readonly TextBox capsBox;

        // Values this tab shows: other codes do not trigger a refresh.
        static readonly HashSet<byte> Shown = new HashSet<byte>
        {
            Vcp.VFreq, Vcp.HFreq, Vcp.ColorTemp, Vcp.ColorTempIncrement, Vcp.DisplayTech, Vcp.Controller, Vcp.Firmware, Vcp.Version,
        };

        public InfoTab(TrayApp app) : base(app, Strings.TabInfo)
        {
            var t = Lay.Table(2);
            var fields = new[]
            {
                Strings.InfoModel, Strings.InfoVendor, Strings.InfoSerial, Strings.InfoId, Strings.InfoWindowsDisplay, Strings.InfoVideoMode,
                Strings.InfoVFreq, Strings.InfoHFreq, Strings.InfoColorTemp, Strings.InfoTechnology, Strings.InfoController,
                Strings.InfoFirmware, Strings.InfoMccs, Strings.InfoCaps,
            };
            foreach (var key in fields)
            {
                var value = new Label { AutoSize = true, UseMnemonic = false, Anchor = AnchorStyles.Left, Margin = Dpi.Pad(3, 4, 3, 4), Text = "—" };
                Label caption;
                Lay.Pair(t, key, value, out caption);
                caption.Margin = Dpi.Pad(3, 4, 14, 4);
                caption.ForeColor = SystemColors.GrayText;
                info[key] = value;
            }
            Lay.Separator(t);
            var capsHeader = Lay.Flow();
            capsHeader.Controls.Add(new Label { Text = Strings.CapsString, AutoSize = true, Margin = Dpi.Pad(3, 7, 8, 3) });
            capsHeader.Controls.Add(Lay.Button(Strings.Copy, delegate { if (capsBox.Text.Length > 0) Clipboard.SetText(capsBox.Text); }));
            Lay.Span(t, capsHeader);
            capsBox = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, Font = Fonts.Mono, MinimumSize = Dpi.Size(0, 70) };
            t.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            int row = t.RowCount++;
            t.Controls.Add(capsBox, 0, row);
            t.SetColumnSpan(capsBox, 2);
            Controls.Add(t);
        }

        public override void Bind(DdcMonitor m) { Fill(m); }

        public override void OnValue(DdcMonitor m, byte code)
        {
            if (Shown.Contains(code)) Fill(m);
        }

        void Fill(DdcMonitor m)
        {
            if (m == null)
            {
                foreach (var l in info.Values) l.Text = "—";
                capsBox.Text = "";
                return;
            }
            Func<byte, string> f = code =>
            {
                VcpValue v;
                return m.Values.TryGetValue(code, out v) ? Vcp.Format(code, v) : (m.Supports(code) ? "…" : "—");
            };
            info[Strings.InfoModel].Text = m.Model;
            info[Strings.InfoVendor].Text = m.Vendor.Length > 0 ? m.Vendor + " (" + m.PnpId + ")" : "—";
            info[Strings.InfoSerial].Text = m.EdidSerial.Length > 0 ? m.EdidSerial
                : m.Key.EndsWith(m.EdidNumSerial.ToString("X8")) ? m.EdidNumSerial.ToString("X8") : Strings.NoSerial;
            info[Strings.InfoId].Text = m.Key;
            info[Strings.InfoWindowsDisplay].Text = m.DeviceName + (m.Primary ? " " + Strings.PrimarySuffix : "");
            info[Strings.InfoVideoMode].Text = m.Width > 0 ? Strings.VideoMode(m.Width, m.Height, m.Hz) : "—";
            info[Strings.InfoVFreq].Text = f(Vcp.VFreq);
            info[Strings.InfoHFreq].Text = f(Vcp.HFreq);
            VcpValue temp, inc;
            if (m.Values.TryGetValue(Vcp.ColorTemp, out temp) && m.Values.TryGetValue(Vcp.ColorTempIncrement, out inc) && inc.Current > 0)
                info[Strings.InfoColorTemp].Text = (3000 + temp.Current * inc.Current) + " K";
            else
                info[Strings.InfoColorTemp].Text = m.Supports(Vcp.ColorTemp) ? "…" : "—";
            info[Strings.InfoTechnology].Text = f(Vcp.DisplayTech);
            info[Strings.InfoController].Text = f(Vcp.Controller);
            string fw = m.Caps != null && !string.IsNullOrEmpty(m.Caps.Firmware) ? m.Caps.Firmware : "";
            VcpValue fwv;
            if (m.Values.TryGetValue(Vcp.Firmware, out fwv) && fwv.Current != 0) fw += (fw.Length > 0 ? " · " : "") + Vcp.Format(Vcp.Firmware, fwv);
            info[Strings.InfoFirmware].Text = fw.Length > 0 ? fw : "—";
            info[Strings.InfoMccs].Text = m.Caps != null && !string.IsNullOrEmpty(m.Caps.MccsVersion) ? m.Caps.MccsVersion : f(Vcp.Version);
            info[Strings.InfoCaps].Text = m.Caps == null ? Strings.CapsNone : Strings.CapsSummary(m.CapsFromCache, m.Caps.Codes.Count);
            capsBox.Text = m.Caps != null ? m.Caps.Raw : "";
        }
    }
}
