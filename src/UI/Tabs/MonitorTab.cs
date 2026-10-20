using System.Drawing;
using System.Windows.Forms;

namespace MonitorConfig
{
    // One tab of the main window. It shows the selected monitor (Bind, when the selection or the monitors change)
    // and follows its values (OnValue, each time one is read or written).
    abstract class MonitorTab : TabPage
    {
        protected readonly TrayApp App;
        protected readonly MonitorService Monitors;
        protected bool Updating;   // filling the controls from the monitor: their events must not write back

        protected MonitorTab(TrayApp app, string title) : base(title)
        {
            App = app;
            Monitors = app.Monitors;
            UseVisualStyleBackColor = false;
            BackColor = SystemColors.Window;
        }

        protected DdcMonitor M { get { return Monitors.Active; } }

        public abstract void Bind(DdcMonitor m);

        public virtual void OnValue(DdcMonitor m, byte code) { }

        protected bool Confirm(string text, string title, MessageBoxIcon icon)
        {
            return MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, icon, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        protected void ConfirmReset(byte code, string what)
        {
            if (M == null || !Confirm(Strings.ResetQuestion(what, M.Model), Strings.ResetTitle, MessageBoxIcon.Warning)) return;
            Monitors.Set(M, code, 1);
            Monitors.Status(Strings.Restoring);
        }

        protected SliderRow Slider(TableLayoutPanel t, string text, byte code)
        {
            var row = new SliderRow(t, text, code);
            row.Changed += delegate(SliderRow r, uint v) { if (M != null) Monitors.Set(M, r.Code, v); };
            return row;
        }
    }
}
